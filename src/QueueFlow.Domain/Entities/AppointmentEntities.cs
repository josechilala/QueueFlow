using System.Security.Cryptography;
using QueueFlow.Domain.Common;
using QueueFlow.Domain.Enums;

namespace QueueFlow.Domain.Entities;

public sealed class Appointment : AuditableEntity, ITenantEntity
{
    private Appointment() : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch) { }
    public Appointment(Guid id, Guid organizationId, Guid branchId, Guid serviceId, string customerName, string? customerPhone, string? customerEmail, DateTimeOffset scheduledStart, DateTimeOffset scheduledEnd, string timeZone, bool requireConfirmation, DateTimeOffset now, Guid? createdByUserId = null, Guid? rescheduledFromAppointmentId = null) : base(id, now)
    {
        if (organizationId == Guid.Empty || branchId == Guid.Empty || serviceId == Guid.Empty || string.IsNullOrWhiteSpace(customerName) || customerName.Trim().Length > 200) throw new DomainException("Appointment ownership and customer name are required.");
        if (scheduledEnd <= scheduledStart || string.IsNullOrWhiteSpace(timeZone) || timeZone.Trim().Length > 100) throw new DomainException("Appointment schedule is invalid.");
        if (customerPhone?.Trim().Length > 30 || customerEmail?.Trim().Length > 320) throw new DomainException("Appointment customer contact is invalid.");
        OrganizationId = organizationId; BranchId = branchId; ServiceId = serviceId; CustomerName = customerName.Trim(); CustomerPhone = Clean(customerPhone); CustomerEmail = Clean(customerEmail)?.ToLowerInvariant(); ScheduledStart = scheduledStart; ScheduledEnd = scheduledEnd; TimeZone = timeZone.Trim(); CreatedByUserId = createdByUserId; RescheduledFromAppointmentId = rescheduledFromAppointmentId;
        Status = requireConfirmation ? AppointmentStatus.Scheduled : AppointmentStatus.Confirmed;
        PublicToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        ConfirmationCode = Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
    }
    public Guid OrganizationId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid ServiceId { get; private set; }
    public Guid? CustomerUserId { get; private set; }
    public string CustomerName { get; private set; } = string.Empty;
    public string? CustomerPhone { get; private set; }
    public string? CustomerEmail { get; private set; }
    public AppointmentStatus Status { get; private set; }
    public DateTimeOffset ScheduledStart { get; private set; }
    public DateTimeOffset ScheduledEnd { get; private set; }
    public string TimeZone { get; private set; } = string.Empty;
    public string ConfirmationCode { get; private set; } = string.Empty;
    public string PublicToken { get; private set; } = string.Empty;
    public string? Notes { get; private set; }
    public DateTimeOffset? CheckedInAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public DateTimeOffset? NoShowAt { get; private set; }
    public Guid? CreatedByUserId { get; private set; }
    public Guid? RescheduledFromAppointmentId { get; private set; }
    public Guid? QueueTicketId { get; private set; }
    public uint Version { get; private set; }
    public int ReminderStage { get; private set; }
    public DateTimeOffset? AnonymizedAt { get; private set; }

    public void Confirm(DateTimeOffset now) { Ensure(AppointmentStatus.Scheduled); Status = AppointmentStatus.Confirmed; MarkUpdated(now); }
    public void CheckIn(Guid queueTicketId, DateTimeOffset now) { Ensure(AppointmentStatus.Confirmed); if (queueTicketId == Guid.Empty || QueueTicketId is not null) throw new DomainException("Appointment check-in ticket is invalid."); QueueTicketId = queueTicketId; CheckedInAt = now; Status = AppointmentStatus.CheckedIn; MarkUpdated(now); }
    public void Complete(DateTimeOffset now) { Ensure(AppointmentStatus.CheckedIn); Status = AppointmentStatus.Completed; CompletedAt = now; MarkUpdated(now); }
    public void Cancel(DateTimeOffset now) { EnsureOneOf(AppointmentStatus.Scheduled, AppointmentStatus.Confirmed); Status = AppointmentStatus.Cancelled; CancelledAt = now; MarkUpdated(now); }
    public void MarkNoShow(DateTimeOffset now) { Ensure(AppointmentStatus.Confirmed); Status = AppointmentStatus.NoShow; NoShowAt = now; MarkUpdated(now); }
    public void MarkRescheduled(DateTimeOffset now) { EnsureOneOf(AppointmentStatus.Scheduled, AppointmentStatus.Confirmed); Status = AppointmentStatus.Rescheduled; MarkUpdated(now); }
    public void SetNotes(string? notes, DateTimeOffset now) { if (notes?.Trim().Length > 1000) throw new DomainException("Appointment notes are too long."); Notes = Clean(notes); MarkUpdated(now); }
    public void MarkReminderSent(int expectedStage, DateTimeOffset now) { if (expectedStage != ReminderStage || expectedStage is < 0 or > 2) throw new DomainException("Appointment reminder stage is invalid."); ReminderStage++; MarkUpdated(now); }
    public void Anonymize(string replacementToken, DateTimeOffset now) { if (Status is not (AppointmentStatus.Completed or AppointmentStatus.Cancelled or AppointmentStatus.NoShow or AppointmentStatus.Rescheduled) || string.IsNullOrWhiteSpace(replacementToken)) throw new DomainException("Only closed appointments can be anonymized."); CustomerName = "Dados removidos"; CustomerPhone = null; CustomerEmail = null; Notes = null; ConfirmationCode = "REMOVED"; PublicToken = replacementToken; AnonymizedAt = now; MarkUpdated(now); }
    private void Ensure(AppointmentStatus expected) { if (Status != expected) throw new DomainException($"Appointment must be {expected}."); }
    private void EnsureOneOf(params AppointmentStatus[] expected) { if (!expected.Contains(Status)) throw new DomainException("Appointment transition is invalid for its current status."); }
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class ServiceSchedule : AuditableEntity, ITenantEntity
{
    private ServiceSchedule() : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch) { }
    public ServiceSchedule(Guid id, Guid organizationId, Guid branchId, Guid serviceId, DayOfWeek dayOfWeek, TimeOnly startTime, TimeOnly endTime, DateTimeOffset now) : base(id, now)
    {
        if (organizationId == Guid.Empty || branchId == Guid.Empty || serviceId == Guid.Empty || !Enum.IsDefined(dayOfWeek) || endTime <= startTime) throw new DomainException("Service schedule is invalid.");
        OrganizationId = organizationId; BranchId = branchId; ServiceId = serviceId; DayOfWeek = dayOfWeek; StartTime = startTime; EndTime = endTime;
    }
    public Guid OrganizationId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid ServiceId { get; private set; }
    public DayOfWeek DayOfWeek { get; private set; }
    public TimeOnly StartTime { get; private set; }
    public TimeOnly EndTime { get; private set; }
    public bool IsActive { get; private set; } = true;
    public void Update(TimeOnly startTime, TimeOnly endTime, bool isActive, DateTimeOffset now) { if (endTime <= startTime) throw new DomainException("Service schedule range is invalid."); StartTime = startTime; EndTime = endTime; IsActive = isActive; MarkUpdated(now); }
}

public sealed class ServiceSchedulingSettings : AuditableEntity, ITenantEntity
{
    private ServiceSchedulingSettings() : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch) { }
    public ServiceSchedulingSettings(Guid id, Guid organizationId, Guid branchId, Guid serviceId, DateTimeOffset now) : base(id, now)
    {
        if (organizationId == Guid.Empty || branchId == Guid.Empty || serviceId == Guid.Empty) throw new DomainException("Scheduling settings ownership is required.");
        OrganizationId = organizationId; BranchId = branchId; ServiceId = serviceId;
    }
    public Guid OrganizationId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid ServiceId { get; private set; }
    public int SlotDurationMinutes { get; private set; } = 30;
    public int CapacityPerSlot { get; private set; } = 1;
    public int MinimumAdvanceMinutes { get; private set; } = 60;
    public int MaximumAdvanceDays { get; private set; } = 30;
    public int LateToleranceMinutes { get; private set; } = 10;
    public int CancellationDeadlineMinutes { get; private set; } = 60;
    public int CheckInAdvanceMinutes { get; private set; } = 30;
    public bool AllowCustomerCancellation { get; private set; } = true;
    public bool AllowCustomerReschedule { get; private set; } = true;
    public bool RequireConfirmation { get; private set; }
    public bool IsActive { get; private set; } = true;
    public void Configure(int slotDurationMinutes, int capacityPerSlot, int minimumAdvanceMinutes, int maximumAdvanceDays, int lateToleranceMinutes, int cancellationDeadlineMinutes, int checkInAdvanceMinutes, bool allowCustomerCancellation, bool allowCustomerReschedule, bool requireConfirmation, bool isActive, DateTimeOffset now)
    {
        if (slotDurationMinutes is < 5 or > 1440 || capacityPerSlot is < 1 or > 1000 || minimumAdvanceMinutes < 0 || maximumAdvanceDays is < 1 or > 730 || lateToleranceMinutes < 0 || cancellationDeadlineMinutes < 0 || checkInAdvanceMinutes < 0) throw new DomainException("Scheduling settings are invalid.");
        SlotDurationMinutes = slotDurationMinutes; CapacityPerSlot = capacityPerSlot; MinimumAdvanceMinutes = minimumAdvanceMinutes; MaximumAdvanceDays = maximumAdvanceDays; LateToleranceMinutes = lateToleranceMinutes; CancellationDeadlineMinutes = cancellationDeadlineMinutes; CheckInAdvanceMinutes = checkInAdvanceMinutes; AllowCustomerCancellation = allowCustomerCancellation; AllowCustomerReschedule = allowCustomerReschedule; RequireConfirmation = requireConfirmation; IsActive = isActive; MarkUpdated(now);
    }
}

public sealed class ScheduleBlock : AuditableEntity, ITenantEntity
{
    private ScheduleBlock() : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch) { }
    public ScheduleBlock(Guid id, Guid organizationId, Guid branchId, Guid? serviceId, DateTimeOffset startAt, DateTimeOffset endAt, string reason, ScheduleBlockType blockType, Guid createdByUserId, DateTimeOffset now) : base(id, now)
    {
        if (organizationId == Guid.Empty || branchId == Guid.Empty || createdByUserId == Guid.Empty || endAt <= startAt || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500 || !Enum.IsDefined(blockType)) throw new DomainException("Schedule block is invalid.");
        OrganizationId = organizationId; BranchId = branchId; ServiceId = serviceId; StartAt = startAt; EndAt = endAt; Reason = reason.Trim(); BlockType = blockType; CreatedByUserId = createdByUserId;
    }
    public Guid OrganizationId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid? ServiceId { get; private set; }
    public DateTimeOffset StartAt { get; private set; }
    public DateTimeOffset EndAt { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public ScheduleBlockType BlockType { get; private set; }
    public Guid CreatedByUserId { get; private set; }
}

public sealed class AppointmentStatusHistory : AuditableEntity, ITenantEntity
{
    private AppointmentStatusHistory() : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch) { }
    public AppointmentStatusHistory(Guid id, Guid organizationId, Guid appointmentId, AppointmentStatus previousStatus, AppointmentStatus newStatus, Guid? changedByUserId, string? reason, DateTimeOffset now) : base(id, now)
    {
        if (organizationId == Guid.Empty || appointmentId == Guid.Empty || previousStatus == newStatus || !Enum.IsDefined(previousStatus) || !Enum.IsDefined(newStatus) || reason?.Trim().Length > 500) throw new DomainException("Appointment status history is invalid.");
        OrganizationId = organizationId; AppointmentId = appointmentId; PreviousStatus = previousStatus; NewStatus = newStatus; ChangedByUserId = changedByUserId; Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }
    public Guid OrganizationId { get; private set; }
    public Guid AppointmentId { get; private set; }
    public AppointmentStatus PreviousStatus { get; private set; }
    public AppointmentStatus NewStatus { get; private set; }
    public Guid? ChangedByUserId { get; private set; }
    public string? Reason { get; private set; }
}
