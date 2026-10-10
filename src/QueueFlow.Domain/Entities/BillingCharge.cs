using QueueFlow.Domain.Common;

namespace QueueFlow.Domain.Entities;

/// <summary>A tenant-owned record of an attempted charge. Provider callbacks must be verified before settlement.</summary>
public sealed class BillingCharge : AuditableEntity, ITenantEntity
{
    private BillingCharge() : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch) { }

    public BillingCharge(Guid id, Guid organizationId, string planCode, string cycle, string paymentMethod,
        decimal amount, string idempotencyKey, DateTimeOffset now) : base(id, now)
    {
        if (organizationId == Guid.Empty) throw new DomainException("Organization is required.");
        if (amount <= 0) throw new DomainException("Charge amount must be positive.");
        OrganizationId = organizationId;
        PlanCode = Required(planCode, nameof(planCode), 40);
        Cycle = Required(cycle, nameof(cycle), 20);
        PaymentMethod = Required(paymentMethod, nameof(paymentMethod), 30);
        IdempotencyKey = Required(idempotencyKey, nameof(idempotencyKey), 120);
        Amount = amount;
    }

    public Guid OrganizationId { get; private set; }
    public string PlanCode { get; private set; } = string.Empty;
    public string Cycle { get; private set; } = string.Empty;
    public string PaymentMethod { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "BRL";
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string? AsaasPaymentId { get; private set; }
    public string? AsaasSubscriptionId { get; private set; }
    public string Status { get; private set; } = "Pending";
    public DateTimeOffset? PaidAt { get; private set; }

    public void AttachProviderReferences(string paymentId, string? subscriptionId, DateTimeOffset now)
    {
        var normalizedPaymentId = Required(paymentId, nameof(paymentId), 120);
        if (AsaasPaymentId is not null && AsaasPaymentId != normalizedPaymentId)
            throw new DomainException("A charge cannot be reassigned to another provider payment.");
        AsaasPaymentId = normalizedPaymentId;
        if (subscriptionId is not null)
        {
            var normalizedSubscriptionId = Required(subscriptionId, nameof(subscriptionId), 120);
            if (AsaasSubscriptionId is not null && AsaasSubscriptionId != normalizedSubscriptionId)
                throw new DomainException("A charge cannot be reassigned to another provider subscription.");
            AsaasSubscriptionId = normalizedSubscriptionId;
        }
        MarkUpdated(now);
    }

    public void ConfirmPayment(DateTimeOffset now)
    {
        if (AsaasPaymentId is null) throw new DomainException("Provider payment reference is required.");
        if (Status == "Paid") return;
        if (Status != "Pending") throw new DomainException("Only pending charges can be paid.");
        Status = "Paid";
        PaidAt = now;
        MarkUpdated(now);
    }

    private static string Required(string? value, string name, int maxLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > maxLength)
            throw new DomainException($"{name} must contain between 1 and {maxLength} characters.");
        return normalized;
    }
}
