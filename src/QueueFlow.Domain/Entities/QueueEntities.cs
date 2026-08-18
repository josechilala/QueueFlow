using QueueFlow.Domain.Common;
using QueueFlow.Domain.Enums;

namespace QueueFlow.Domain.Entities;

public sealed class Queue : AuditableEntity, ITenantEntity
{
    private Queue() : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch) { }
    public Queue(Guid id, Guid organizationId, Guid branchId, Guid serviceId, string name, DateTimeOffset now) : base(id, now)
    { OrganizationId = organizationId; BranchId = branchId; ServiceId = serviceId; Name = string.IsNullOrWhiteSpace(name) ? throw new DomainException("Queue name is required.") : name.Trim(); PublicId = Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant(); }
    public Guid OrganizationId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid ServiceId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string PublicId { get; private set; } = string.Empty;
    public QueueStatus Status { get; private set; } = QueueStatus.Draft;
    public int? Capacity { get; private set; }
    public long NextSequenceNumber { get; private set; } = 1;
    public void Open(DateTimeOffset now) { if (Status is QueueStatus.Open) throw new DomainException("Queue is already open."); Status = QueueStatus.Open; MarkUpdated(now); }
    public void Pause(DateTimeOffset now) { if (Status is not QueueStatus.Open) throw new DomainException("Only an open queue can be paused."); Status = QueueStatus.Paused; MarkUpdated(now); }
    public void Close(DateTimeOffset now) { if (Status is QueueStatus.Closed) throw new DomainException("Queue is already closed."); Status = QueueStatus.Closed; MarkUpdated(now); }
    public long ReserveSequence() { if (Status is not QueueStatus.Open) throw new DomainException("Queue is not open."); return NextSequenceNumber++; }
}

public sealed class QueueTicket : AuditableEntity, ITenantEntity
{
    private QueueTicket() : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch) { }
    public QueueTicket(Guid id, Guid organizationId, Guid branchId, Guid queueId, Guid serviceId, string ticketNumber, long sequenceNumber, TicketPriority priority, string publicToken, DateTimeOffset now) : base(id, now)
    { OrganizationId = organizationId; BranchId = branchId; QueueId = queueId; ServiceId = serviceId; TicketNumber = ticketNumber; SequenceNumber = sequenceNumber; Priority = priority; CustomerPublicToken = publicToken; IssuedAt = now; }
    public Guid OrganizationId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid QueueId { get; private set; }
    public Guid ServiceId { get; private set; }
    public string TicketNumber { get; private set; } = string.Empty;
    public long SequenceNumber { get; private set; }
    public TicketStatus Status { get; private set; } = TicketStatus.Waiting;
    public TicketPriority Priority { get; private set; }
    public string? CustomerName { get; private set; }
    public string? CustomerPhone { get; private set; }
    public string CustomerPublicToken { get; private set; } = string.Empty;
    public DateTimeOffset IssuedAt { get; private set; }
    public DateTimeOffset? CalledAt { get; private set; }
    public DateTimeOffset? ServiceStartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public Guid? CounterId { get; private set; }
    public Guid? AttendantUserId { get; private set; }
    public uint Version { get; private set; }
    public void Call(Guid counterId, Guid attendantId, DateTimeOffset now) { Ensure(TicketStatus.Waiting); Status = TicketStatus.Called; CounterId = counterId; AttendantUserId = attendantId; CalledAt = now; }
    public void Start(DateTimeOffset now) { Ensure(TicketStatus.Called); Status = TicketStatus.InService; ServiceStartedAt = now; }
    public void Complete(DateTimeOffset now) { Ensure(TicketStatus.InService); Status = TicketStatus.Completed; CompletedAt = now; }
    public void Cancel(DateTimeOffset now) { if (Status is TicketStatus.Completed or TicketStatus.Cancelled) throw new DomainException("Ticket cannot be cancelled in its current state."); Status = TicketStatus.Cancelled; CancelledAt = now; }
    public void MarkNoShow(DateTimeOffset now) { Ensure(TicketStatus.Called); Status = TicketStatus.NoShow; MarkUpdated(now); }
    public void Recall(DateTimeOffset now) { Ensure(TicketStatus.Called); CalledAt = now; MarkUpdated(now); }
    private void Ensure(TicketStatus expected) { if (Status != expected) throw new DomainException($"Ticket must be {expected}."); }
}

public sealed class TicketEvent : AuditableEntity, ITenantEntity
{
    private TicketEvent() : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch) { }
    public TicketEvent(Guid id, Guid organizationId, Guid ticketId, TicketStatus status, string? reason, DateTimeOffset now) : base(id, now)
    { OrganizationId = organizationId; TicketId = ticketId; Status = status; Reason = reason; }
    public Guid OrganizationId { get; private set; }
    public Guid TicketId { get; private set; }
    public TicketStatus Status { get; private set; }
    public string? Reason { get; private set; }
}

public sealed class Notification : AuditableEntity, ITenantEntity
{
    private Notification() : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch) { }
    public Notification(Guid id, Guid organizationId, NotificationChannel channel, string payload, DateTimeOffset now) : base(id, now) { OrganizationId = organizationId; Channel = channel; Payload = payload; NextAttemptAt = now; }
    public Guid OrganizationId { get; private set; }
    public NotificationChannel Channel { get; private set; }
    public NotificationStatus Status { get; private set; } = NotificationStatus.Pending;
    public string Payload { get; private set; } = string.Empty;
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public void MarkSent(DateTimeOffset now) { Status = NotificationStatus.Sent; MarkUpdated(now); }
    public void MarkFailed(DateTimeOffset now) { Attempts++; Status = NotificationStatus.Failed; NextAttemptAt = now.AddMinutes(Math.Pow(2, Math.Min(Attempts, 6))); MarkUpdated(now); }
}

public sealed class AuditLog : AuditableEntity, ITenantEntity
{
    private AuditLog() : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch) { }
    public AuditLog(Guid id, Guid organizationId, Guid? userId, string action, string correlationId, DateTimeOffset now) : base(id, now) { OrganizationId = organizationId; UserId = userId; Action = action; CorrelationId = correlationId; }
    public Guid OrganizationId { get; private set; }
    public Guid? UserId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string CorrelationId { get; private set; } = string.Empty;
}
