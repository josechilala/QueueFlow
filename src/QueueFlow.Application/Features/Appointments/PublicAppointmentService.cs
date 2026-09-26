using Microsoft.EntityFrameworkCore;
using QueueFlow.Application.Abstractions.Clock;
using QueueFlow.Application.Abstractions.Persistence;
using QueueFlow.Application.Common;
using QueueFlow.Domain.Enums;

namespace QueueFlow.Application.Features.Appointments;

public sealed record PublicAppointmentDto(string PublicToken, string ConfirmationCode, AppointmentStatus Status, string OrganizationName, string BranchPublicId, string BranchName, string ServicePublicId, string ServiceName, DateTimeOffset ScheduledStart, DateTimeOffset ScheduledEnd, string TimeZone, bool CanCancel, bool CanReschedule, string? QueueTicketToken, string? QueueTicketNumber);

public sealed record PublicSchedulingServiceDto(string PublicId, string Name, string? Description, ServiceAttendanceMode AttendanceMode, bool CanSchedule);
public sealed record PublicSchedulingBranchDto(string PublicId, string Name, string? Address, string TimeZone, IReadOnlyList<PublicSchedulingServiceDto> Services);
public sealed record PublicSchedulingOrganizationDto(string Slug, string Name, IReadOnlyList<PublicSchedulingBranchDto> Branches);

public sealed class PublicAppointmentService(IApplicationDbContext db, IClock clock, IAppointmentOperations operations)
{
    public async Task<Result<PublicSchedulingOrganizationDto>> GetCatalogAsync(string slug, CancellationToken ct)
    {
        var normalized = slug.Trim().ToLowerInvariant();
        var organization = await db.Organizations.AsNoTracking().SingleOrDefaultAsync(x => x.Slug == normalized && x.IsActive, ct);
        if (organization is null) return Result.Failure<PublicSchedulingOrganizationDto>(new("organization.not_found", "Organization was not found."));
        var branches = await db.Branches.IgnoreQueryFilters().AsNoTracking().Where(x => x.OrganizationId == organization.Id && x.IsActive).OrderBy(x => x.Name).ToListAsync(ct);
        var ids = branches.Select(x => x.Id).ToArray();
        var services = await db.Services.IgnoreQueryFilters().AsNoTracking().Where(x => x.OrganizationId == organization.Id && ids.Contains(x.BranchId) && x.IsActive && x.AttendanceMode != ServiceAttendanceMode.QueueOnly).OrderBy(x => x.Name).ToListAsync(ct);
        var settings = await db.ServiceSchedulingSettings.IgnoreQueryFilters().AsNoTracking().Where(x => x.OrganizationId == organization.Id && ids.Contains(x.BranchId) && x.IsActive).Select(x => new { x.BranchId, x.ServiceId }).ToListAsync(ct);
        return Result.Success(new PublicSchedulingOrganizationDto(organization.Slug, organization.Name, branches.Select(branch =>
            new PublicSchedulingBranchDto(branch.PublicId, branch.Name, branch.Address, branch.TimeZone, services.Where(service => service.BranchId == branch.Id).Select(service =>
                new PublicSchedulingServiceDto(service.PublicId, service.Name, service.Description, service.AttendanceMode, settings.Any(x => x.BranchId == branch.Id && x.ServiceId == service.Id))).ToArray())).ToArray()));
    }

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
        var ticket = appointment.QueueTicketId is null ? null : await db.QueueTickets.IgnoreQueryFilters().AsNoTracking().Where(x => x.OrganizationId == appointment.OrganizationId && x.Id == appointment.QueueTicketId).Select(x => new { x.CustomerPublicToken, x.TicketNumber }).SingleOrDefaultAsync(cancellationToken);
        return Result.Success(new PublicAppointmentDto(appointment.PublicToken, appointment.ConfirmationCode, appointment.Status, organizationName, branch.PublicId, branch.Name, service.PublicId, service.Name, appointment.ScheduledStart, appointment.ScheduledEnd, appointment.TimeZone, canCancel, canReschedule, ticket?.CustomerPublicToken, ticket?.TicketNumber));
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
    private static Result<PublicAppointmentDto> NotFound() => Result.Failure<PublicAppointmentDto>(new("appointments.not_found", "O agendamento não foi encontrado."));
}
