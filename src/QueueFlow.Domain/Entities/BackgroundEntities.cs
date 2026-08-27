using QueueFlow.Domain.Common;

namespace QueueFlow.Domain.Entities;

public sealed class OutboxMessage : AuditableEntity, ITenantEntity
{
    private OutboxMessage() : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch) { }
    public OutboxMessage(Guid id, Guid organizationId, string type, string payload, DateTimeOffset now) : base(id, now)
    {
        if (organizationId == Guid.Empty || string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(payload)) throw new DomainException("Outbox message data is required.");
        OrganizationId = organizationId; Type = type.Trim(); Payload = payload; NextAttemptAt = now;
    }
    public Guid OrganizationId { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public string? LastError { get; private set; }
    public void MarkProcessed(DateTimeOffset now) { ProcessedAt = now; LastError = null; MarkUpdated(now); }
    public void MarkFailed(string error, DateTimeOffset now) { Attempts++; LastError = string.IsNullOrWhiteSpace(error) ? "Unknown delivery error." : error[..Math.Min(error.Length, 1000)]; NextAttemptAt = now.AddSeconds(Math.Pow(2, Math.Min(Attempts, 10))); MarkUpdated(now); }
}

public sealed class QueueMetricSnapshot : AuditableEntity, ITenantEntity
{
    private QueueMetricSnapshot() : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch) { }
    public QueueMetricSnapshot(Guid id, Guid organizationId, Guid queueId, int waitingCount, int activeAttendants, DateTimeOffset now) : base(id, now)
    { OrganizationId = organizationId; QueueId = queueId; WaitingCount = waitingCount; ActiveAttendants = activeAttendants; }
    public Guid OrganizationId { get; private set; }
    public Guid QueueId { get; private set; }
    public int WaitingCount { get; private set; }
    public int ActiveAttendants { get; private set; }
}
