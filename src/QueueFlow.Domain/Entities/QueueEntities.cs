using QueueFlow.Domain.Common;
using QueueFlow.Domain.Enums;
using System.Security.Cryptography;

namespace QueueFlow.Domain.Entities;

public sealed class Queue : AuditableEntity, ITenantEntity
{
    private Queue() : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch) { }
    public Queue(Guid id, Guid organizationId, Guid branchId, Guid serviceId, string name, int? capacity, DateTimeOffset now) : base(id, now)
    {
        if (organizationId == Guid.Empty || branchId == Guid.Empty || serviceId == Guid.Empty || string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200 || capacity is <= 0) throw new DomainException("Queue data is invalid.");
        OrganizationId = organizationId; BranchId = branchId; ServiceId = serviceId; Name = name.Trim(); Capacity = capacity; PublicId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    }
    public Guid OrganizationId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid ServiceId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string PublicId { get; private set; } = string.Empty;
    public QueueStatus Status { get; private set; } = QueueStatus.Draft;
    public int? Capacity { get; private set; }
    public bool IsActive { get; private set; } = true;
    public long NextSequenceNumber { get; private set; } = 1;
    public void Open(DateTimeOffset now)
    {
        if (Status is QueueStatus.Open) throw new DomainException("Queue is already open.");
        if (Status is QueueStatus.Closed) throw new DomainException("A closed queue cannot be reopened.");
        Status = QueueStatus.Open;
        MarkUpdated(now);
    }
    public void Pause(DateTimeOffset now) { if (Status is not QueueStatus.Open) throw new DomainException("Only an open queue can be paused."); Status = QueueStatus.Paused; MarkUpdated(now); }
    public void Close(DateTimeOffset now) { if (Status is QueueStatus.Closed) throw new DomainException("Queue is already closed."); Status = QueueStatus.Closed; IsActive = false; MarkUpdated(now); }
    public long ReserveSequence() { if (Status is not QueueStatus.Open) throw new DomainException("Queue is not open."); return NextSequenceNumber++; }
}

public sealed class QueueTicket : AuditableEntity, ITenantEntity
{
    private QueueTicket() : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch) { }
    public QueueTicket(Guid id, Guid organizationId, Guid branchId, Guid queueId, Guid serviceId, string ticketNumber, long sequenceNumber, TicketPriority priority, string publicToken, DateTimeOffset now) : base(id, now)
    {
        if (organizationId == Guid.Empty || branchId == Guid.Empty || queueId == Guid.Empty || serviceId == Guid.Empty)
            throw new DomainException("Ticket ownership identifiers are required.");
        if (string.IsNullOrWhiteSpace(ticketNumber)) throw new DomainException("Ticket number is required.");
        if (sequenceNumber < 1) throw new DomainException("Ticket sequence must be positive.");
        if (!Enum.IsDefined(priority)) throw new DomainException("Ticket priority is invalid.");
        if (string.IsNullOrWhiteSpace(publicToken)) throw new DomainException("Ticket public token is required.");

        OrganizationId = organizationId;
        BranchId = branchId;
        QueueId = queueId;
        ServiceId = serviceId;
        TicketNumber = ticketNumber.Trim();
        SequenceNumber = sequenceNumber;
        Priority = priority;
        CustomerPublicToken = publicToken.Trim();
        IssuedAt = now;
    }
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
    public DateTimeOffset? AnonymizedAt { get; private set; }
    public void Call(Guid counterId, Guid attendantId, DateTimeOffset now) { Ensure(TicketStatus.Waiting); Status = TicketStatus.Called; CounterId = counterId; AttendantUserId = attendantId; CalledAt = now; MarkUpdated(now); }
    public void Start(DateTimeOffset now) { Ensure(TicketStatus.Called); Status = TicketStatus.InService; ServiceStartedAt = now; MarkUpdated(now); }
    public void Complete(DateTimeOffset now) { Ensure(TicketStatus.InService); Status = TicketStatus.Completed; CompletedAt = now; MarkUpdated(now); }
    public void Cancel(DateTimeOffset now) { if (Status is not (TicketStatus.Waiting or TicketStatus.Called or TicketStatus.InService)) throw new DomainException("Ticket cannot be cancelled in its current state."); Status = TicketStatus.Cancelled; CancelledAt = now; MarkUpdated(now); }
    public void MarkNoShow(DateTimeOffset now) { Ensure(TicketStatus.Called); Status = TicketStatus.NoShow; MarkUpdated(now); }
    public void Recall(DateTimeOffset now) { Ensure(TicketStatus.Called); CalledAt = now; MarkUpdated(now); }
    public void Anonymize(string replacementPublicToken, DateTimeOffset now)
    {
        if (Status is not (TicketStatus.Completed or TicketStatus.Cancelled or TicketStatus.NoShow)) throw new DomainException("Only closed tickets can be anonymized.");
        if (string.IsNullOrWhiteSpace(replacementPublicToken)) throw new DomainException("Replacement token is required.");
        CustomerName = null; CustomerPhone = null; CustomerPublicToken = replacementPublicToken; AnonymizedAt = now; MarkUpdated(now);
    }
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
    public Notification(Guid id, Guid organizationId, string recipientPublicToken, string message, DateTimeOffset now) : this(id, organizationId, NotificationChannel.InApp, message, now)
    {
        if (string.IsNullOrWhiteSpace(recipientPublicToken) || string.IsNullOrWhiteSpace(message)) throw new DomainException("Notification recipient and message are required.");
        RecipientPublicToken = recipientPublicToken.Trim();
        Message = message.Trim();
    }
    public Guid OrganizationId { get; private set; }
    public NotificationChannel Channel { get; private set; }
    public NotificationStatus Status { get; private set; } = NotificationStatus.Pending;
    public string Payload { get; private set; } = string.Empty;
    public string? RecipientPublicToken { get; private set; }
    public string? Message { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public void MarkSent(DateTimeOffset now) { Status = NotificationStatus.Sent; MarkUpdated(now); }
    public void MarkFailed(DateTimeOffset now) { Attempts++; Status = NotificationStatus.Failed; NextAttemptAt = now.AddMinutes(Math.Pow(2, Math.Min(Attempts, 6))); MarkUpdated(now); }
    public void MarkRead(DateTimeOffset now) { if (ReadAt is null) { ReadAt = now; MarkUpdated(now); } }
}

public sealed class AuditLog : AuditableEntity, ITenantEntity
{
    private AuditLog() : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch) { }
    public AuditLog(Guid id, Guid organizationId, Guid? userId, string action, string correlationId, DateTimeOffset now) : base(id, now) { OrganizationId = organizationId; UserId = userId; Action = action; CorrelationId = correlationId; }
    public AuditLog(Guid id, Guid organizationId, Guid? userId, string action, string resourceType, Guid? resourceId, string? data, string correlationId, DateTimeOffset now) : this(id, organizationId, userId, action, correlationId, now)
    {
        if (string.IsNullOrWhiteSpace(resourceType)) throw new DomainException("Audit resource type is required.");
        ResourceType = resourceType.Trim(); ResourceId = resourceId; Data = data;
    }
    public Guid OrganizationId { get; private set; }
    public Guid? UserId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string ResourceType { get; private set; } = string.Empty;
    public Guid? ResourceId { get; private set; }
    public string? Data { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
}
