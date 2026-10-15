using QueueFlow.Domain.Common;

namespace QueueFlow.Domain.Entities;

/// <summary>Durable receipt, independent of tenant resolution; never contains credentials or the raw payload.</summary>
public sealed class BillingWebhookInboxEntry : BaseEntity
{
    private BillingWebhookInboxEntry() : base(Guid.NewGuid()) { }

    public string ProviderEventId { get; private set; } = string.Empty;
    public string EventType { get; private set; } = string.Empty;
    public string? PaymentId { get; private set; }
    public string Status { get; private set; } = "Pending";
    public int Attempts { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? LastErrorCode { get; private set; }

    public void Complete(string status, string? reason, DateTimeOffset now)
    {
        if (Status != "Pending" || status is not ("Processed" or "Ignored" or "Review"))
            throw new DomainException("Invalid inbox transition.");
        Status = status;
        Attempts++;
        CompletedAt = now;
        LastErrorCode = reason;
    }

    public void Retry(string reason, DateTimeOffset now)
    {
        if (Status != "Pending") throw new DomainException("Only pending receipts can be retried.");
        if (Attempts >= 11) { Complete("Review", "RetryLimitExceeded", now); return; }
        Attempts++;
        LastErrorCode = reason;
        NextAttemptAt = now.AddSeconds(Math.Min(3600, 5 * Math.Pow(2, Attempts - 1)));
    }
}
