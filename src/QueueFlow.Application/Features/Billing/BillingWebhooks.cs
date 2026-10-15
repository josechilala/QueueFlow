namespace QueueFlow.Application.Features.Billing;

public sealed record BillingWebhookEnvelope(string EventId, string EventType, string? PaymentId);
public enum BillingWebhookOutcome { Processed, Ignored, Review, Retry }

public interface IBillingWebhookInbox
{
    Task AcceptAsync(BillingWebhookEnvelope envelope, CancellationToken ct);
}

public interface IBillingWebhookInboxStore
{
    Task PersistAsync(BillingWebhookEnvelope envelope, CancellationToken ct);
}

public interface IBillingWebhookProcessor
{
    Task<BillingWebhookOutcome> ProcessAsync(string eventId, string eventType, string paymentId, CancellationToken ct);
}

public interface IBillingWebhookDispatcher
{
    Task<bool> ProcessNextAsync(CancellationToken ct);
}

public sealed class ReceiveBillingWebhook(IBillingWebhookInboxStore store) : IBillingWebhookInbox
{
    public Task AcceptAsync(BillingWebhookEnvelope envelope, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(envelope.EventId) || envelope.EventId.Length > 200 ||
            string.IsNullOrWhiteSpace(envelope.EventType) || envelope.EventType.Length > 60 ||
            (envelope.EventType.StartsWith("PAYMENT_", StringComparison.Ordinal) &&
             (string.IsNullOrWhiteSpace(envelope.PaymentId) || envelope.PaymentId.Length > 100 ||
              !envelope.PaymentId.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))))
            throw new ArgumentException("Invalid webhook envelope.", nameof(envelope));
        return store.PersistAsync(envelope, ct);
    }

    public static bool Supports(string eventType) => eventType is "PAYMENT_CREATED" or "PAYMENT_UPDATED" or
        "PAYMENT_CONFIRMED" or "PAYMENT_RECEIVED" or "PAYMENT_OVERDUE" or "PAYMENT_DELETED" or "PAYMENT_REFUNDED";
}
