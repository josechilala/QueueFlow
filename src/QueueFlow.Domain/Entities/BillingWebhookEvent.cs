using QueueFlow.Domain.Common;

namespace QueueFlow.Domain.Entities;

/// <summary>Minimal durable receipt; never stores a provider payload or card/customer data.</summary>
public sealed class BillingWebhookEvent : BaseEntity
{
    private BillingWebhookEvent() : base(Guid.NewGuid()) { }
    public BillingWebhookEvent(string providerEventId, Guid chargeId, string eventType, DateTimeOffset now)
        : base(Guid.NewGuid())
    {
        if (string.IsNullOrWhiteSpace(providerEventId) || providerEventId.Length > 200 ||
            string.IsNullOrWhiteSpace(eventType) || eventType.Length > 60 || chargeId == Guid.Empty)
            throw new DomainException("Invalid billing event.");
        ProviderEventId = providerEventId;
        ChargeId = chargeId;
        EventType = eventType;
        ProcessedAt = now;
    }
    public string ProviderEventId { get; private set; } = string.Empty;
    public Guid ChargeId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public DateTimeOffset ProcessedAt { get; private set; }
}
