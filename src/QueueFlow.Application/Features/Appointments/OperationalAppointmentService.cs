using Microsoft.EntityFrameworkCore;
using QueueFlow.Application.Abstractions.Auditing;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Abstractions.Clock;
using QueueFlow.Application.Abstractions.Persistence;
using QueueFlow.Application.Common;
using QueueFlow.Domain.Enums;

namespace QueueFlow.Application.Features.Appointments;

public sealed record OperationalAppointmentDto(
    Guid Id,
    Guid BranchId,
    string BranchName,
    string CustomerName,
    string ServiceName,
    AppointmentStatus AppointmentStatus,
    TicketStatus? TicketStatus,
    string? TicketNumber,
    DateTimeOffset ScheduledStart,
    DateTimeOffset ScheduledEnd,
    string ScheduledLocalTime,
    DateTimeOffset? CheckedInAt,
    Guid? QueueTicketId,
    string OperationalStatus,
    bool ArrivalConfirmed,
    bool CanConfirmArrival,
    DateTimeOffset? CheckInAvailableAt,
    DateTimeOffset? CheckInClosesAt,
    int? DelayMinutes);

public sealed class OperationalAppointmentService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IClock clock,
    IAuditWriter audit,
    IAppointmentOperations operations)
{
    private static readonly Error NotFound = new("appointments.not_found", "O agendamento não foi encontrado.");
    private static readonly Error BranchForbidden = new("appointments.branch_forbidden", "Você não possui acesso à unidade deste agendamento.");
    private static readonly Error QueueUnavailable = new("appointments.queue_unavailable", "Não há fila ativa para este serviço.");
    private static readonly Error CheckInNotAllowed = new("appointments.check_in_not_allowed", "A confirmação de chegada não está disponível agora.");

    private Guid Tenant => currentUser.OrganizationId ?? throw new UnauthorizedAccessException();
    private Guid UserId => currentUser.UserId ?? throw new UnauthorizedAccessException();
    private UserRole Role => currentUser.Role ?? throw new UnauthorizedAccessException();

    public async Task<IReadOnlyList<OperationalAppointmentDto>> ListTodayAsync(CancellationToken ct)
    {
        var branches = await AllowedBranches().AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
        if (branches.Count == 0) return [];

        var windows = branches.Select(branch => CreateDayWindow(branch.Id, branch.Name, branch.TimeZone)).ToArray();
        var firstStart = windows.Min(x => x.Start);
        var lastEnd = windows.Max(x => x.End);
        var branchIds = windows.Select(x => x.BranchId).ToArray();
        var appointments = await db.Appointments.AsNoTracking()
            .Where(x => branchIds.Contains(x.BranchId) && x.ScheduledStart >= firstStart && x.ScheduledStart < lastEnd)
            .OrderBy(x => x.ScheduledStart)
            .ToListAsync(ct);
        appointments = appointments
            .Where(appointment => windows.Any(window => window.BranchId == appointment.BranchId && appointment.ScheduledStart >= window.Start && appointment.ScheduledStart < window.End))
            .ToList();

        var serviceIds = appointments.Select(x => x.ServiceId).Distinct().ToArray();
        var ticketIds = appointments.Where(x => x.QueueTicketId != null).Select(x => x.QueueTicketId!.Value).Distinct().ToArray();
        var services = await db.Services.AsNoTracking().Where(x => serviceIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var settings = await db.ServiceSchedulingSettings.AsNoTracking().Where(x => serviceIds.Contains(x.ServiceId)).ToDictionaryAsync(x => x.ServiceId, ct);
        var tickets = await db.QueueTickets.AsNoTracking().Where(x => ticketIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var branchById = windows.ToDictionary(x => x.BranchId);

        return appointments.Select(appointment =>
        {
            tickets.TryGetValue(appointment.QueueTicketId ?? Guid.Empty, out var ticket);
            settings.TryGetValue(appointment.ServiceId, out var scheduling);
            return Map(
                appointment,
                branchById[appointment.BranchId],
                services.GetValueOrDefault(appointment.ServiceId, "Serviço indisponível"),
                scheduling,
                ticket?.Status,
                ticket?.TicketNumber);
        }).ToArray();
    }

    public async Task<Result<OperationalAppointmentDto>> ConfirmArrivalAsync(Guid appointmentId, CancellationToken ct)
    {
        var appointment = await db.Appointments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == appointmentId, ct);
        if (appointment is null) return Result.Failure<OperationalAppointmentDto>(NotFound);
        if (!await CanAccessBranchAsync(appointment.BranchId, ct)) return Result.Failure<OperationalAppointmentDto>(BranchForbidden);

        var result = await operations.CheckInAsync(appointmentId, ct);
        if (result is not null)
        {
            audit.Write("appointment.checked-in", "Appointment", appointmentId, new { result.Ticket.Id, result.Ticket.TicketNumber, Source = "AttendantPanel" });
            await db.SaveChangesAsync(ct);
            return Result.Success(await MapByIdAsync(appointmentId, ct));
        }

        var current = await db.Appointments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == appointmentId, ct);
        if (current is null) return Result.Failure<OperationalAppointmentDto>(NotFound);
        if (current.QueueTicketId is not null)
            return Result.Success(await MapByIdAsync(appointmentId, ct));

        var hasQueue = await db.Queues.AsNoTracking().AnyAsync(x =>
            x.OrganizationId == Tenant &&
            x.BranchId == current.BranchId &&
            x.ServiceId == current.ServiceId &&
            x.IsActive &&
            x.Status == QueueStatus.Open, ct);
        if (!hasQueue) return Result.Failure<OperationalAppointmentDto>(QueueUnavailable);

        return Result.Failure<OperationalAppointmentDto>(CheckInNotAllowed);
    }

    private IQueryable<Domain.Entities.Branch> AllowedBranches()
    {
        var branches = db.Branches.Where(x => x.OrganizationId == Tenant && x.IsActive);
        if (Role != UserRole.Attendant) return branches;
        return branches.Where(branch => db.UserBranches.Any(assignment =>
            assignment.OrganizationId == Tenant && assignment.UserId == UserId && assignment.BranchId == branch.Id));
    }

    private Task<bool> CanAccessBranchAsync(Guid branchId, CancellationToken ct)
    {
        if (Role != UserRole.Attendant)
            return db.Branches.AnyAsync(x => x.OrganizationId == Tenant && x.Id == branchId && x.IsActive, ct);
        return db.Branches.AnyAsync(branch =>
            branch.OrganizationId == Tenant && branch.Id == branchId && branch.IsActive &&
            db.UserBranches.Any(assignment =>
                assignment.OrganizationId == Tenant && assignment.UserId == UserId && assignment.BranchId == branch.Id), ct);
    }

    private async Task<OperationalAppointmentDto> MapByIdAsync(Guid appointmentId, CancellationToken ct)
    {
        var appointment = await db.Appointments.AsNoTracking().SingleAsync(x => x.Id == appointmentId, ct);
        var branch = await db.Branches.AsNoTracking().SingleAsync(x => x.Id == appointment.BranchId, ct);
        var serviceName = await db.Services.AsNoTracking().Where(x => x.Id == appointment.ServiceId).Select(x => x.Name).SingleAsync(ct);
        var scheduling = await db.ServiceSchedulingSettings.AsNoTracking().SingleOrDefaultAsync(x => x.ServiceId == appointment.ServiceId, ct);
        var ticket = appointment.QueueTicketId is Guid ticketId
            ? await db.QueueTickets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == ticketId, ct)
            : null;
        return Map(appointment, CreateDayWindow(branch.Id, branch.Name, branch.TimeZone), serviceName, scheduling, ticket?.Status, ticket?.TicketNumber);
    }

    private OperationalAppointmentDto Map(
        Domain.Entities.Appointment appointment,
        BranchDayWindow branch,
        string serviceName,
        Domain.Entities.ServiceSchedulingSettings? settings,
        TicketStatus? ticketStatus,
        string? ticketNumber)
    {
        DateTimeOffset? checkInAvailableAt = settings is null ? null : appointment.ScheduledStart.AddMinutes(-settings.CheckInAdvanceMinutes);
        DateTimeOffset? checkInClosesAt = settings is null ? null : appointment.ScheduledStart.AddMinutes(settings.LateToleranceMinutes);
        var canConfirm = appointment.Status == AppointmentStatus.Confirmed && appointment.QueueTicketId is null &&
            checkInAvailableAt <= clock.UtcNow && clock.UtcNow <= checkInClosesAt;
        var operationalStatus = ResolveOperationalStatus(appointment.Status, ticketStatus);
        int? delayMinutes = operationalStatus == "Waiting" && clock.UtcNow > appointment.ScheduledStart
            ? (int)Math.Floor((clock.UtcNow - appointment.ScheduledStart).TotalMinutes)
            : null;
        var localTime = TimeZoneInfo.ConvertTime(appointment.ScheduledStart, branch.TimeZone).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);

        return new(
            appointment.Id,
            appointment.BranchId,
            branch.BranchName,
            appointment.CustomerName,
            serviceName,
            appointment.Status,
            ticketStatus,
            ticketNumber,
            appointment.ScheduledStart,
            appointment.ScheduledEnd,
            localTime,
            appointment.CheckedInAt,
            appointment.QueueTicketId,
            operationalStatus,
            appointment.QueueTicketId is not null,
            canConfirm,
            checkInAvailableAt,
            checkInClosesAt,
            delayMinutes);
    }

    private static string ResolveOperationalStatus(AppointmentStatus appointmentStatus, TicketStatus? ticketStatus)
    {
        if (ticketStatus is not null) return ticketStatus.Value switch
        {
            TicketStatus.Waiting => "Waiting",
            TicketStatus.Called => "Called",
            TicketStatus.InService => "InService",
            TicketStatus.Completed => "Completed",
            TicketStatus.NoShow => "NoShow",
            TicketStatus.Cancelled => "Cancelled",
            _ => "Waiting",
        };
        return appointmentStatus switch
        {
            AppointmentStatus.Scheduled => "AwaitingConfirmation",
            AppointmentStatus.Confirmed => "AwaitingArrival",
            AppointmentStatus.CheckedIn => "Waiting",
            AppointmentStatus.Completed => "Completed",
            AppointmentStatus.Cancelled => "Cancelled",
            AppointmentStatus.NoShow => "NoShow",
            AppointmentStatus.Rescheduled => "Rescheduled",
            _ => "AwaitingArrival",
        };
    }

    private BranchDayWindow CreateDayWindow(Guid branchId, string branchName, string timeZoneId)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(clock.UtcNow, timeZone);
        var localStart = DateTime.SpecifyKind(localNow.Date, DateTimeKind.Unspecified);
        var localEnd = localStart.AddDays(1);
        var start = new DateTimeOffset(localStart, timeZone.GetUtcOffset(localStart)).ToUniversalTime();
        var end = new DateTimeOffset(localEnd, timeZone.GetUtcOffset(localEnd)).ToUniversalTime();
        return new(branchId, branchName, timeZone, start, end);
    }

    private sealed record BranchDayWindow(Guid BranchId, string BranchName, TimeZoneInfo TimeZone, DateTimeOffset Start, DateTimeOffset End);
}
