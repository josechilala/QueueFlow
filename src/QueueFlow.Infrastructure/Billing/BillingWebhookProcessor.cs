using Microsoft.EntityFrameworkCore;
using QueueFlow.Domain.Entities;
using QueueFlow.Infrastructure.Persistence;
using QueueFlow.Application.Features.Billing;

namespace QueueFlow.Infrastructure.Billing;

public sealed class BillingWebhookProcessor(ApplicationDbContext db, AsaasSandboxClient provider, AsaasSandboxTenantAccess access) : IBillingWebhookProcessor
{
    public async Task<BillingWebhookOutcome> ProcessAsync(string eventId, string eventType, string paymentId, CancellationToken ct)
    {
        if (access.AllowedTenantId is null) throw new UnauthorizedAccessException("Sandbox tenant is not configured.");
        if (!ReceiveBillingWebhook.Supports(eventType)) return BillingWebhookOutcome.Ignored;
        if (await db.BillingWebhookEvents.IgnoreQueryFilters().AnyAsync(x => x.ProviderEventId == eventId, ct)) return BillingWebhookOutcome.Processed;

        // IgnoreQueryFilters is confined to this authenticated provider boundary. Tenant is resolved from persisted references.
        var candidate = await db.BillingCharges.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.AsaasPaymentId == paymentId, ct);
        if (candidate is not null) access.RequireAllowed(candidate.OrganizationId);
        var payment = await provider.GetPaymentAsync(paymentId, ct);
        if (payment.Id != paymentId) throw new QueueFlow.Domain.Common.DomainException("Provider returned a different payment.");
        if (candidate is null && Guid.TryParseExact(payment.ExternalReference, "N", out var reference))
            candidate = await db.BillingCharges.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Id == reference, ct);
        var renewal = false;
        if (candidate is null && !string.IsNullOrWhiteSpace(payment.Subscription))
        {
            candidate = await db.BillingCharges.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.AsaasSubscriptionId == payment.Subscription)
                .OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(ct);
            renewal = candidate is not null && candidate.AsaasPaymentId is not null;
        }
        // References may still be being persisted by checkout. Retry boundedly, never create a provider resource.
        if (candidate is null) return BillingWebhookOutcome.Retry;
        access.RequireAllowed(candidate.OrganizationId);

        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await BillingCheckoutService.LockTenantAsync(db, candidate.OrganizationId, ct);
        if (await db.BillingWebhookEvents.IgnoreQueryFilters().AnyAsync(x => x.ProviderEventId == eventId, ct)) return BillingWebhookOutcome.Processed;
        var charge = await db.BillingCharges.IgnoreQueryFilters().SingleAsync(x => x.Id == candidate.Id, ct);
        access.RequireAllowed(charge.OrganizationId);
        // Re-fetch after acquiring the lock: a delayed event cannot overwrite newer provider state.
        payment = await provider.GetPaymentAsync(paymentId, ct);
        if (payment.Id != paymentId) throw new QueueFlow.Domain.Common.DomainException("Provider returned a different payment.");
        // A cancelled reservation must never become a renewal anchor or regain entitlements.
        if (charge.Status == "Cancelled")
        {
            if (payment.Customer != charge.AsaasCustomerId ||
                (charge.AsaasSubscriptionId is not null && payment.Subscription != charge.AsaasSubscriptionId) ||
                (charge.AsaasPaymentId is not null && payment.Id != charge.AsaasPaymentId) ||
                (charge.AsaasSubscriptionId is null && payment.ExternalReference != charge.Id.ToString("N")))
                throw new QueueFlow.Domain.Common.DomainException("Cancelled payment identity does not match.");
            // Received money must be reviewed even when the method/amount is incompatible.
            if (payment.Status is "CONFIRMED" or "RECEIVED" or "REFUNDED" or "REFUND_IN_PROGRESS")
                return BillingWebhookOutcome.Review;
            BillingCheckoutService.ValidatePayment(charge, payment);
            return BillingWebhookOutcome.Ignored;
        }
        if (renewal)
        {
            // Provider due date, rather than arrival order, determines which renewal is newer.
            if (payment.DueDate is not DateOnly dueDate || payment.Customer != charge.AsaasCustomerId ||
                payment.Subscription != charge.AsaasSubscriptionId || payment.Value != charge.Amount ||
                payment.BillingType != (charge.PaymentMethod == "Pix" ? "PIX" : "CREDIT_CARD"))
                throw new QueueFlow.Domain.Common.DomainException("Renewal does not match the registered subscription.");
            var existing = await db.BillingCharges.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.AsaasPaymentId == paymentId, ct);
            if (existing is not null) { access.RequireAllowed(existing.OrganizationId); charge = existing; }
            else
            {
                var renewalCharge = new BillingCharge(Guid.NewGuid(), charge.OrganizationId, charge.PlanCode,
                    charge.Cycle, charge.PaymentMethod, charge.Amount, $"renewal:{paymentId}",
                    new DateTimeOffset(dueDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
                renewalCharge.AttachCustomer(charge.AsaasCustomerId!, DateTimeOffset.UtcNow);
                renewalCharge.AttachSubscription(charge.AsaasSubscriptionId!, DateTimeOffset.UtcNow);
                db.BillingCharges.Add(renewalCharge);
                charge = renewalCharge;
            }
        }
        BillingCheckoutService.ValidatePayment(charge, payment);
        charge.AttachProviderReferences(payment.Id, payment.Subscription, DateTimeOffset.UtcNow);
        var now = DateTimeOffset.UtcNow;
        if (eventType is "PAYMENT_CONFIRMED" or "PAYMENT_RECEIVED" && !payment.Deleted &&
            payment.Status is not ("CONFIRMED" or "RECEIVED" or "REFUNDED" or "REFUND_IN_PROGRESS"))
            throw new InvalidOperationException("Provider confirmation is not yet available.");
        var subscription = await db.Subscriptions.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.OrganizationId == charge.OrganizationId, ct);
        if (subscription is null || subscription.Status == QueueFlow.Domain.Enums.SubscriptionStatus.Cancelled)
            throw new QueueFlow.Domain.Common.DomainException("Subscription requires review before settlement.");
        var newerPaidCharge = await db.BillingCharges.IgnoreQueryFilters().AnyAsync(x =>
            x.OrganizationId == charge.OrganizationId && x.CreatedAt > charge.CreatedAt && (x.Status == "Paid" || x.Status == "Refunded"), ct);

        if (payment.Status is "CONFIRMED" or "RECEIVED" && !payment.Deleted && charge.Status is "Pending" or "Uncertain")
        {
            charge.ConfirmPayment(now);
            if (!newerPaidCharge) subscription?.ActivateFromConfirmedBilling(charge.PlanCode, now);
        }
        else if (payment.Status == "REFUNDED" && charge.Status is "Paid" or "Pending" or "Uncertain")
        {
            var wasPaid = charge.Status == "Paid";
            charge.Refund(now);
            if (wasPaid && !newerPaidCharge) subscription?.MarkBillingPastDue(now);
        }
        else if (payment.Deleted && charge.Status is "Pending" or "Uncertain") charge.Cancel(now);

        db.BillingWebhookEvents.Add(new BillingWebhookEvent(eventId, charge.Id, eventType, now));
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        QueueFlow.Application.Observability.QueueFlowTelemetry.BillingEvents.Add(1);
        return BillingWebhookOutcome.Processed;
    }
}
