using Microsoft.EntityFrameworkCore;
using QueueFlow.Application.Abstractions.Auditing;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Abstractions.Clock;
using QueueFlow.Application.Abstractions.Persistence;
using QueueFlow.Application.Common;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;
using System.Text.Json;

namespace QueueFlow.Application.Features.Appointments;

public sealed record AppointmentListItemDto(Guid Id, string CustomerName, string? CustomerPhone, string? CustomerEmail, AppointmentStatus Status, DateTimeOffset ScheduledStart, DateTimeOffset ScheduledEnd, Guid BranchId, string BranchName, Guid ServiceId, string ServiceName, DateTimeOffset CreatedAt, string Origin);
public sealed record AppointmentNotificationTraceDto(int Generated, int Pending, int Sent, int Failed, DateTimeOffset? LastSentAt);
public sealed record AppointmentDetailsDto(Guid Id, string CustomerName, string? CustomerPhone, string? CustomerEmail, AppointmentStatus Status, DateTimeOffset ScheduledStart, DateTimeOffset ScheduledEnd, string TimeZone, string ConfirmationCode, string PublicToken, string? Notes, Guid BranchId, string BranchName, Guid ServiceId, string ServiceName, Guid? QueueTicketId, DateTimeOffset CreatedAt, string Origin, Guid? CreatedByUserId, string CreatedBy, AppointmentNotificationTraceDto Notifications, IReadOnlyList<AppointmentHistoryDto> History);
public sealed record AppointmentHistoryDto(AppointmentStatus PreviousStatus, AppointmentStatus NewStatus, string? Reason, DateTimeOffset CreatedAt);

public sealed class AppointmentManagementService(IApplicationDbContext db, ICurrentUser currentUser, IClock clock, IAuditWriter audit, IAppointmentOperations operations)
{
    private Guid Tenant => currentUser.OrganizationId ?? throw new UnauthorizedAccessException();

    public async Task<IReadOnlyList<AppointmentListItemDto>> ListAsync(DateOnly? from, DateOnly? to, AppointmentStatus? status, Guid? branchId, Guid? serviceId, CancellationToken cancellationToken)
    {
        var start = from?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc); var end = to?.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var query = from appointment in db.Appointments.AsNoTracking()
                    join branch in db.Branches.AsNoTracking() on appointment.BranchId equals branch.Id
                    join service in db.Services.AsNoTracking() on appointment.ServiceId equals service.Id
                    where (start == null || appointment.ScheduledStart >= start) && (end == null || appointment.ScheduledStart < end) && (status == null || appointment.Status == status) && (branchId == null || appointment.BranchId == branchId) && (serviceId == null || appointment.ServiceId == serviceId)
                    orderby appointment.ScheduledStart
                    select new AppointmentListItemDto(appointment.Id, appointment.CustomerName, appointment.CustomerPhone, appointment.CustomerEmail, appointment.Status, appointment.ScheduledStart, appointment.ScheduledEnd, appointment.BranchId, branch.Name, appointment.ServiceId, service.Name, appointment.CreatedAt, appointment.CreatedByUserId == null ? "PublicPortal" : "AdminPanel");
        return await query.Take(500).ToListAsync(cancellationToken);
    }

    public async Task<Result<AppointmentDetailsDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var appointment = await db.Appointments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (appointment is null) return NotFound<AppointmentDetailsDto>();
        var branchName = await db.Branches.AsNoTracking().Where(x => x.Id == appointment.BranchId).Select(x => x.Name).SingleAsync(cancellationToken);
        var serviceName = await db.Services.AsNoTracking().Where(x => x.Id == appointment.ServiceId).Select(x => x.Name).SingleAsync(cancellationToken);
        var history = await db.AppointmentStatusHistory.AsNoTracking().Where(x => x.AppointmentId == id).OrderBy(x => x.CreatedAt).Select(x => new AppointmentHistoryDto(x.PreviousStatus, x.NewStatus, x.Reason, x.CreatedAt)).ToListAsync(cancellationToken);
        var creatorName = appointment.CreatedByUserId is Guid creatorId
            ? await db.Users.AsNoTracking().Where(x => x.Id == creatorId).Select(x => x.Name).SingleOrDefaultAsync(cancellationToken) ?? "Usuário removido"
            : "Cliente pelo portal público";
        var notificationRows = await db.Notifications.AsNoTracking()
            .Where(x => x.RecipientPublicToken == appointment.PublicToken)
            .Select(x => new { x.Status, x.UpdatedAt })
            .ToListAsync(cancellationToken);
        var notifications = new AppointmentNotificationTraceDto(
            notificationRows.Count,
            notificationRows.Count(x => x.Status == NotificationStatus.Pending),
            notificationRows.Count(x => x.Status == NotificationStatus.Sent),
            notificationRows.Count(x => x.Status == NotificationStatus.Failed),
            notificationRows.Where(x => x.Status == NotificationStatus.Sent).Max(x => x.UpdatedAt));
        var origin = appointment.CreatedByUserId is null ? "PublicPortal" : "AdminPanel";
        return Result.Success(new AppointmentDetailsDto(appointment.Id, appointment.CustomerName, appointment.CustomerPhone, appointment.CustomerEmail, appointment.Status, appointment.ScheduledStart, appointment.ScheduledEnd, appointment.TimeZone, appointment.ConfirmationCode, appointment.PublicToken, appointment.Notes, appointment.BranchId, branchName, appointment.ServiceId, serviceName, appointment.QueueTicketId, appointment.CreatedAt, origin, appointment.CreatedByUserId, creatorName, notifications, history));
    }

    public Task<Result<AppointmentDetailsDto>> ConfirmAsync(Guid id, CancellationToken cancellationToken) => TransitionAsync(id, (item, now) => item.Confirm(now), "appointment.confirmed", null, cancellationToken);
    public Task<Result<AppointmentDetailsDto>> CancelAsync(Guid id, string? reason, CancellationToken cancellationToken) => TransitionAsync(id, (item, now) => item.Cancel(now), "appointment.cancelled", reason, cancellationToken);
    public Task<Result<AppointmentDetailsDto>> NoShowAsync(Guid id, string? reason, CancellationToken cancellationToken) => TransitionAsync(id, (item, now) => item.MarkNoShow(now), "appointment.no-show", reason, cancellationToken);
    public async Task<Result<AppointmentDetailsDto>> CheckInAsync(Guid id, CancellationToken cancellationToken)
    {
        var appointment = await db.Appointments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (appointment is null) return NotFound<AppointmentDetailsDto>();
        if (appointment.QueueTicketId is not null) return await GetAsync(id, cancellationToken);

        var result = await operations.CheckInAsync(id, cancellationToken);
        if (result is null)
        {
            var current = await db.Appointments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (current?.QueueTicketId is not null) return await GetAsync(id, cancellationToken);
            var hasQueue = current is not null && await db.Queues.AsNoTracking().AnyAsync(x =>
                x.OrganizationId == Tenant && x.BranchId == current.BranchId && x.ServiceId == current.ServiceId &&
                x.IsActive && x.Status == QueueStatus.Open, cancellationToken);
            if (!hasQueue) return Result.Failure<AppointmentDetailsDto>(new("appointments.queue_unavailable", "Não há fila ativa para este serviço."));
            return Result.Failure<AppointmentDetailsDto>(new("appointments.check_in_not_allowed", "A confirmação de chegada não está disponível agora."));
        }
        audit.Write("appointment.checked-in", "Appointment", id, new { result.Ticket.Id, result.Ticket.TicketNumber });
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    private async Task<Result<AppointmentDetailsDto>> TransitionAsync(Guid id, Action<Appointment, DateTimeOffset> transition, string action, string? reason, CancellationToken cancellationToken)
    {
        var appointment = await db.Appointments.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (appointment is null) return NotFound<AppointmentDetailsDto>();
        var previous = appointment.Status; transition(appointment, clock.UtcNow);
        db.AppointmentStatusHistory.Add(new AppointmentStatusHistory(Guid.NewGuid(), Tenant, appointment.Id, previous, appointment.Status, currentUser.UserId, reason, clock.UtcNow));
        db.OutboxMessages.Add(new OutboxMessage(Guid.NewGuid(), Tenant, "appointment.realtime", JsonSerializer.Serialize(new { eventName = action, appointmentToken = appointment.PublicToken, appointmentId = appointment.Id, appointment.ServiceId, status = appointment.Status.ToString(), appointment.ScheduledStart }), clock.UtcNow));
        audit.Write(action, "Appointment", appointment.Id, new { PreviousStatus = previous.ToString(), Status = appointment.Status.ToString(), Reason = reason });
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    private static Result<T> NotFound<T>() => Result.Failure<T>(new("appointments.not_found", "O agendamento não foi encontrado."));
}
