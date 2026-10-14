using Microsoft.EntityFrameworkCore;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Features.Billing;
using QueueFlow.Domain.Common;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;
using QueueFlow.Infrastructure.Persistence;
using QueueFlow.Application.Abstractions.Auditing;

namespace QueueFlow.Infrastructure.Billing;

/// <summary>Reserve durably before making provider mutations. Ambiguous outcomes require reconciliation.</summary>
public sealed class BillingCheckoutService(ApplicationDbContext db, ICurrentUser user, AsaasSandboxClient provider, AsaasSandboxTenantAccess access, IAuditWriter? audit = null)
    : IBillingCheckout
{
    private Guid Tenant()
    {
        if (!user.IsAuthenticated || user.IdentityType != IdentityType.Tenant || user.Role != UserRole.Owner ||
            user.OrganizationId is not Guid id || id == Guid.Empty) throw new UnauthorizedAccessException();
        access.RequireAllowed(id);
        return id;
    }

    public async Task<BillingChargeResponse> StartAsync(BillingCheckoutRequest request, CancellationToken ct)
    {
        var tenant = Tenant();
        BillingPlan plan;
        try { plan = BillingPlans.Get(request.PlanCode); }
        catch (ArgumentException) { throw new DomainException("Unknown billing plan."); }
        if (!Enum.TryParse<BillingCycle>(request.Cycle, out var cycle) || !Enum.IsDefined(cycle) ||
            !Enum.TryParse<BillingPaymentMethod>(request.PaymentMethod, out var method) ||
            method is not (BillingPaymentMethod.Pix or BillingPaymentMethod.CreditCard))
            throw new DomainException("Unsupported billing selection.");
        var now = DateTimeOffset.UtcNow;
        var charge = new BillingCharge(Guid.NewGuid(), tenant, plan.Code, cycle.ToString(), method.ToString(),
            plan.PriceFor(cycle), request.IdempotencyKey, now);
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            await LockTenantAsync(db, tenant, ct);
            var existing = await db.BillingCharges.SingleOrDefaultAsync(x => x.IdempotencyKey == charge.IdempotencyKey, ct);
            if (existing is not null)
            {
                if (existing.PlanCode != charge.PlanCode || existing.Cycle != charge.Cycle || existing.PaymentMethod != charge.PaymentMethod)
                    throw new QueueFlow.Application.Common.ConflictException("Idempotency key was used for a different checkout.");
                await transaction.CommitAsync(ct);
                return await ResponseAsync(existing, ct);
            }
            if (await db.BillingCharges.AnyAsync(x => x.Status == "Pending" || x.Status == "Uncertain" ||
                (x.Status == "Paid" && x.AsaasSubscriptionId != null), ct))
                throw new QueueFlow.Application.Common.ConflictException("An existing checkout or recurring subscription must be resolved first.");
            if (await db.Subscriptions.AnyAsync(x => x.Status == SubscriptionStatus.Cancelled, ct))
                throw new DomainException("Cancelled subscriptions require explicit reactivation.");
            db.BillingCharges.Add(charge);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            QueueFlow.Application.Observability.QueueFlowTelemetry.BillingReservations.Add(1);
        }

        try
        {
            var customer = await provider.CreateCustomerAsync(request.CustomerName, request.CustomerEmail,
                request.CpfCnpj, tenant.ToString("N"), ct);
            charge.AttachCustomer(customer.Id, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(ct);
            AsaasPaymentResponse payment;
            if (BillingPlans.CollectionMode(cycle, method) == BillingCollectionMode.RecurringSubscription)
            {
                var subscription = await provider.CreateSubscriptionAsync(customer.Id, charge.Amount,
                    DateOnly.FromDateTime(now.UtcDateTime), BillingPlans.AsaasCycle(cycle),
                    BillingPlans.AsaasRecurringBillingType(method), charge.Id.ToString("N"), ct);
                charge.AttachSubscription(subscription.Id, DateTimeOffset.UtcNow);
                await db.SaveChangesAsync(ct);
                var initialPayment = await FindInitialSubscriptionPaymentAsync(provider, charge, ct);
                // Subscription creation succeeded. An empty successful read is not an ambiguous creation.
                if (initialPayment is null) return await ResponseAsync(charge, ct);
                payment = initialPayment;
            }
            else
            {
                payment = await provider.CreatePaymentAsync(customer.Id, charge.Amount,
                    DateOnly.FromDateTime(now.UtcDateTime), "PIX", charge.Id.ToString("N"), ct);
            }
            ValidatePayment(charge, payment);
            charge.AttachProviderReferences(payment.Id, payment.Subscription, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(ct);
            return await ResponseAsync(charge, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or
            System.Text.Json.JsonException or InvalidOperationException or DomainException or DbUpdateException)
        {
            // Use a separate short budget even if the HTTP client disconnected. Never retry POST.
            if (IsDefinitiveRejection(ex, charge))
            {
                charge.Cancel(DateTimeOffset.UtcNow);
                audit?.Write("BillingCheckoutRejected", "BillingCharge", charge.Id,
                    new { Status = "Cancelled", HttpStatus = (int?)((HttpRequestException)ex).StatusCode });
            }
            else
            {
                charge.MarkUncertain(DateTimeOffset.UtcNow);
                QueueFlow.Application.Observability.QueueFlowTelemetry.BillingUncertain.Add(1);
            }
            using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await db.SaveChangesAsync(recovery.Token);
            return new(charge.Id, charge.PlanCode, charge.Cycle, charge.PaymentMethod, charge.Amount, charge.Currency, charge.Status);
        }
    }

    internal static bool IsDefinitiveRejection(Exception error, BillingCharge charge) =>
        error is AsaasCreationRejectedException && charge.AsaasSubscriptionId is null && charge.AsaasPaymentId is null;

    public async Task<BillingChargeResponse?> GetAsync(Guid id, CancellationToken ct)
    {
        Tenant();
        var charge = await db.BillingCharges.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (charge is { Status: "Uncertain" } or { Status: "Pending", AsaasPaymentId: null, AsaasSubscriptionId: not null })
            await ReconcileAsync(charge, ct);
        return charge is null ? null : await ResponseAsync(charge, ct);
    }

    private async Task ReconcileAsync(BillingCharge charge, CancellationToken ct)
    {
        if (charge.AsaasCustomerId is null) return;
        try
        {
            AsaasPaymentResponse? payment;
            if (charge.AsaasPaymentId is not null)
                payment = await provider.GetPaymentAsync(charge.AsaasPaymentId, ct);
            else if (charge.AsaasSubscriptionId is not null)
                payment = await FindInitialSubscriptionPaymentAsync(provider, charge, ct);
            else
            {
                var matches = await provider.FindPaymentsAsync(charge.Id.ToString("N"), ct);
                if (matches.HasMore || matches.Data is not { Length: 1 }) return;
                payment = matches.Data[0];
            }
            if (payment is null) return;
            ValidatePayment(charge, payment);
            if (payment.Deleted) return;
            charge.AttachProviderReferences(payment.Id, payment.Subscription, DateTimeOffset.UtcNow);
            charge.ResolveUncertain(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or
            System.Text.Json.JsonException or InvalidOperationException or DomainException or DbUpdateConcurrencyException)
        { /* Reads may recover references but never issue a second provider mutation. */ }
    }

    internal static async Task<AsaasPaymentResponse?> FindInitialSubscriptionPaymentAsync(
        AsaasSandboxClient provider, BillingCharge charge, CancellationToken ct)
    {
        if (charge.AsaasSubscriptionId is null) throw new InvalidOperationException("Subscription reference is required.");
        var matches = await provider.GetSubscriptionPaymentsAsync(charge.AsaasSubscriptionId, ct);
        if (matches.HasMore || matches.Data is null || matches.Data.Length > 1)
            throw new InvalidOperationException("Initial subscription payment is not uniquely available.");
        if (matches.Data.Length == 0) return null;
        var payment = matches.Data[0];
        ValidatePayment(charge, payment);
        if (payment.Deleted) throw new DomainException("Initial subscription payment was deleted.");
        return payment;
    }

    private async Task<BillingChargeResponse> ResponseAsync(BillingCharge charge, CancellationToken ct)
    {
        string? url = null, payload = null, image = null;
        if (charge.AsaasPaymentId is not null && charge.Status == "Pending")
        {
            try
            {
                var payment = await provider.GetPaymentAsync(charge.AsaasPaymentId, ct);
                ValidatePayment(charge, payment);
                if (payment.Deleted) throw new DomainException("Provider payment was deleted.");
                if (Uri.TryCreate(payment.InvoiceUrl, UriKind.Absolute, out var uri) &&
                    uri.Scheme == "https" && uri.Host == "sandbox.asaas.com" && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo))
                    url = uri.AbsoluteUri;
                if (charge.PaymentMethod == "Pix")
                {
                    var qr = await provider.GetPixQrCodeAsync(charge.AsaasPaymentId, ct);
                    payload = qr.Payload;
                    image = qr.EncodedImage;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or
                System.Text.Json.JsonException or InvalidOperationException or DomainException) { /* A read failure never retries creation. */ }
        }
        return new(charge.Id, charge.PlanCode, charge.Cycle, charge.PaymentMethod, charge.Amount, charge.Currency,
            charge.Status, url, payload, image);
    }

    internal static void ValidatePayment(BillingCharge charge, AsaasPaymentResponse payment)
    {
        if (string.IsNullOrWhiteSpace(payment.Id) || payment.Id.Length > 100 ||
            !payment.Id.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') || payment.Value != charge.Amount ||
            payment.Customer != charge.AsaasCustomerId ||
            payment.BillingType != (charge.PaymentMethod == "Pix" ? "PIX" : "CREDIT_CARD") ||
            (charge.AsaasPaymentId is not null && payment.Id != charge.AsaasPaymentId) ||
            (charge.AsaasSubscriptionId is not null ? payment.Subscription != charge.AsaasSubscriptionId :
                payment.Subscription is not null || payment.ExternalReference != charge.Id.ToString("N")))
            throw new DomainException("Provider payment does not match the reserved charge.");
    }

    internal static Task<int> LockTenantAsync(ApplicationDbContext context, Guid tenant, CancellationToken ct) =>
        context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({tenant.ToString("N")}, 710041905))", ct);
}
