using Microsoft.EntityFrameworkCore;
using QueueFlow.Application.Abstractions.Clock;
using QueueFlow.Application.Abstractions.Persistence;
using QueueFlow.Application.Common;
using QueueFlow.Domain.Enums;

namespace QueueFlow.Application.Features.Appointments;

public sealed record PublicAppointmentDto(string PublicToken, string ConfirmationCode, AppointmentStatus Status, string OrganizationName, string BranchPublicId, string BranchName, string ServicePublicId, string ServiceName, DateTimeOffset ScheduledStart, DateTimeOffset ScheduledEnd, string TimeZone, bool CanCancel, bool CanReschedule, bool CanCheckIn, string? QueueTicketToken, string? QueueTicketNumber);

public sealed class PublicAppointmentService(IApplicationDbContext db, IClock clock, IAppointmentOperations operations)
{
    public async Task<Result<PublicAppointmentDto>> GetAsync(string publicToken, CancellationToken cancellationToken)
    {
        if (publicToken.Length != 32 || !publicToken.All(Uri.IsHexDigit)) return NotFound();
        var appointment = await db.Appointments.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.PublicToken == publicToken, cancellationToken);
        if (appointment is null) return NotFound();
        var branch = await db.Branches.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == appointment.OrganizationId && x.Id == appointment.BranchId, cancellationToken);
        var service = await db.Services.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == appointment.OrganizationId && x.Id == appointment.ServiceId, cancellationToken);
        var settings = await db.ServiceSchedulingSettings.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == appointment.OrganizationId && x.ServiceId == appointment.ServiceId, cancellationToken);
        if (branch is null || service is null || settings is null) return NotFound();
        var organizationName = await db.Organizations.AsNoTracking().Where(x => x.Id == appointment.OrganizationId).Select(x => x.Name).SingleAsync(cancellationToken);
        var active = appointment.Status is AppointmentStatus.Scheduled or AppointmentStatus.Confirmed;
        var canCancel = active && settings.AllowCustomerCancellation && clock.UtcNow <= appointment.ScheduledStart.AddMinutes(-settings.CancellationDeadlineMinutes);
        var canReschedule = active && settings.AllowCustomerReschedule && clock.UtcNow <= appointment.ScheduledStart.AddMinutes(-settings.CancellationDeadlineMinutes);
        var canCheckIn = appointment.Status == AppointmentStatus.Confirmed && clock.UtcNow >= appointment.ScheduledStart.AddMinutes(-settings.CheckInAdvanceMinutes) && clock.UtcNow <= appointment.ScheduledStart.AddMinutes(settings.LateToleranceMinutes);
        var ticket = appointment.QueueTicketId is null ? null : await db.QueueTickets.IgnoreQueryFilters().AsNoTracking().Where(x => x.OrganizationId == appointment.OrganizationId && x.Id == appointment.QueueTicketId).Select(x => new { x.CustomerPublicToken, x.TicketNumber }).SingleOrDefaultAsync(cancellationToken);
        return Result.Success(new PublicAppointmentDto(appointment.PublicToken, appointment.ConfirmationCode, appointment.Status, organizationName, branch.PublicId, branch.Name, service.PublicId, service.Name, appointment.ScheduledStart, appointment.ScheduledEnd, appointment.TimeZone, canCancel, canReschedule, canCheckIn, ticket?.CustomerPublicToken, ticket?.TicketNumber));
    }

    public async Task<Result<PublicAppointmentDto>> CancelAsync(string publicToken, CancellationToken cancellationToken)
    {
        var appointment = await operations.CancelAsync(publicToken, cancellationToken);
        return appointment is null ? Result.Failure<PublicAppointmentDto>(new("appointments.cancellation_not_allowed", "O cancelamento não está disponível para este agendamento.")) : await GetAsync(appointment.PublicToken, cancellationToken);
    }

    public async Task<Result<PublicAppointmentDto>> RescheduleAsync(string publicToken, DateTimeOffset scheduledStart, CancellationToken cancellationToken)
    {
        var appointment = await operations.RescheduleAsync(publicToken, scheduledStart, cancellationToken);
        return appointment is null ? Result.Failure<PublicAppointmentDto>(new("appointments.reschedule_not_allowed", "Não foi possível reagendar para este horário.")) : await GetAsync(appointment.PublicToken, cancellationToken);
    }
    public async Task<Result<PublicAppointmentDto>> CheckInAsync(string publicToken, CancellationToken cancellationToken)
    {
        var result = await operations.CheckInAsync(publicToken, cancellationToken);
        return result is null ? Result.Failure<PublicAppointmentDto>(new("appointments.check_in_not_allowed", "O check-in não está disponível agora ou a fila está fechada.")) : await GetAsync(publicToken, cancellationToken);
    }

    private static Result<PublicAppointmentDto> NotFound() => Result.Failure<PublicAppointmentDto>(new("appointments.not_found", "O agendamento não foi encontrado."));
}
