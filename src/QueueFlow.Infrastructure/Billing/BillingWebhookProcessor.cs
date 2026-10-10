using Microsoft.EntityFrameworkCore;
using QueueFlow.Domain.Entities;
using QueueFlow.Infrastructure.Persistence;

namespace QueueFlow.Infrastructure.Billing;

public sealed class BillingWebhookProcessor(ApplicationDbContext db, AsaasSandboxClient provider)
{
    public async Task ProcessAsync(string eventId, string eventType, string paymentId, CancellationToken ct)
    {
        if (eventType is not ("PAYMENT_CREATED" or "PAYMENT_UPDATED" or "PAYMENT_CONFIRMED" or
            "PAYMENT_RECEIVED" or "PAYMENT_OVERDUE" or "PAYMENT_DELETED" or "PAYMENT_REFUNDED")) return;
        if (await db.BillingWebhookEvents.IgnoreQueryFilters().AnyAsync(x => x.ProviderEventId == eventId, ct)) return;

        // IgnoreQueryFilters is confined to this authenticated provider boundary. Tenant is resolved from persisted references.
        var payment = await provider.GetPaymentAsync(paymentId, ct);
        var candidate = await db.BillingCharges.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.AsaasPaymentId == paymentId, ct);
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
        if (candidate is null) return; // Charges outside QueueFlow must not affect any tenant.

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await BillingCheckoutService.LockTenantAsync(db, candidate.OrganizationId, ct);
        if (await db.BillingWebhookEvents.IgnoreQueryFilters().AnyAsync(x => x.ProviderEventId == eventId, ct)) return;
        var charge = await db.BillingCharges.IgnoreQueryFilters().SingleAsync(x => x.Id == candidate.Id, ct);
        // Re-fetch after acquiring the lock: a delayed event cannot overwrite newer provider state.
        payment = await provider.GetPaymentAsync(paymentId, ct);
        if (renewal)
        {
            // Provider due date, rather than arrival order, determines which renewal is newer.
            if (payment.DueDate is not DateOnly dueDate || payment.Customer != charge.AsaasCustomerId ||
                payment.Subscription != charge.AsaasSubscriptionId || payment.Value != charge.Amount ||
                payment.BillingType != (charge.PaymentMethod == "Pix" ? "PIX" : "CREDIT_CARD"))
                throw new QueueFlow.Domain.Common.DomainException("Renewal does not match the registered subscription.");
            var existing = await db.BillingCharges.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.AsaasPaymentId == paymentId, ct);
            if (existing is not null) charge = existing;
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
        await transaction.CommitAsync(ct);
        QueueFlow.Application.Observability.QueueFlowTelemetry.BillingEvents.Add(1);
    }
}
