using Microsoft.EntityFrameworkCore;
using QueueFlow.Application.Abstractions.Clock;
using QueueFlow.Application.Abstractions.Persistence;
using QueueFlow.Application.Common;
using QueueFlow.Domain.Enums;

namespace QueueFlow.Application.Features.Appointments;

public interface IAppointmentAvailabilityService
{
    Task<Result<IReadOnlyList<AvailableSlotDto>>> GetAsync(Guid branchId, Guid serviceId, DateOnly requestedDate, CancellationToken cancellationToken);
    Task<Result<PublicAvailabilityDto>> GetPublicAsync(string branchPublicId, string servicePublicId, DateOnly requestedDate, CancellationToken cancellationToken);
}

public sealed record PublicAvailabilityDto(string BranchPublicId, string ServicePublicId, string OrganizationName, string BranchName, string ServiceName, DateOnly Date, string TimeZone, DateOnly MinimumDate, DateOnly MaximumDate, IReadOnlyList<DayOfWeek> AvailableDaysOfWeek, IReadOnlyList<AvailableSlotDto> Slots);

public sealed class AppointmentAvailabilityService(IApplicationDbContext db, IClock clock) : IAppointmentAvailabilityService
{
    public async Task<Result<IReadOnlyList<AvailableSlotDto>>> GetAsync(Guid branchId, Guid serviceId, DateOnly requestedDate, CancellationToken cancellationToken)
    {
        var branch = await db.Branches.AsNoTracking().SingleOrDefaultAsync(x => x.Id == branchId && x.IsActive, cancellationToken);
        var service = await db.Services.AsNoTracking().SingleOrDefaultAsync(x => x.Id == serviceId && x.BranchId == branchId && x.IsActive, cancellationToken);
        var settings = await db.ServiceSchedulingSettings.AsNoTracking().SingleOrDefaultAsync(x => x.ServiceId == serviceId && x.IsActive, cancellationToken);
        if (branch is null || service is null || settings is null || service.AttendanceMode == ServiceAttendanceMode.QueueOnly)
            return Result.Failure<IReadOnlyList<AvailableSlotDto>>(new("appointments.scheduling_unavailable", "O agendamento não está disponível para este serviço."));

        TimeZoneInfo timeZone;
        try { timeZone = TimeZoneInfo.FindSystemTimeZoneById(branch.TimeZone); }
        catch (TimeZoneNotFoundException) { return InvalidTimeZone(); }
        catch (InvalidTimeZoneException) { return InvalidTimeZone(); }

        var daySchedules = await db.ServiceSchedules.AsNoTracking()
            .Where(x => x.ServiceId == serviceId && x.IsActive && x.DayOfWeek == requestedDate.DayOfWeek)
            .Select(x => new { x.StartTime, x.EndTime }).ToListAsync(cancellationToken);
        if (daySchedules.Count == 0) return Result.Success<IReadOnlyList<AvailableSlotDto>>([]);

        var localDayStart = requestedDate.ToDateTime(TimeOnly.MinValue);
        var localDayEnd = requestedDate.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var dayStart = ToUtc(localDayStart, timeZone);
        var dayEnd = ToUtc(localDayEnd, timeZone);
        var blocks = await db.ScheduleBlocks.AsNoTracking()
            .Where(x => x.BranchId == branchId && (x.ServiceId == null || x.ServiceId == serviceId) && x.StartAt < dayEnd && x.EndAt > dayStart)
            .Select(x => new SlotWindow(x.StartAt, x.EndAt)).ToListAsync(cancellationToken);
        var statuses = new[] { AppointmentStatus.Scheduled, AppointmentStatus.Confirmed, AppointmentStatus.CheckedIn };
        var reservations = await db.Appointments.AsNoTracking()
            .Where(x => x.BranchId == branchId && x.ServiceId == serviceId && statuses.Contains(x.Status) && x.ScheduledStart < dayEnd && x.ScheduledEnd > dayStart)
            .Select(x => new SlotWindow(x.ScheduledStart, x.ScheduledEnd)).ToListAsync(cancellationToken);
        var slots = AppointmentSlotGenerator.Generate(requestedDate, timeZone, daySchedules.Select(x => (x.StartTime, x.EndTime)).ToArray(), settings.SlotDurationMinutes, settings.CapacityPerSlot, clock.UtcNow.AddMinutes(settings.MinimumAdvanceMinutes), clock.UtcNow.AddDays(settings.MaximumAdvanceDays), blocks, reservations);
        return Result.Success(slots);
    }

    public async Task<Result<PublicAvailabilityDto>> GetPublicAsync(string branchPublicId, string servicePublicId, DateOnly requestedDate, CancellationToken cancellationToken)
    {
        if (!ValidPublicId(branchPublicId) || !ValidPublicId(servicePublicId)) return NotFound();
        var branch = await db.Branches.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.PublicId == branchPublicId && x.IsActive, cancellationToken);
        if (branch is null || !await db.Organizations.AnyAsync(x => x.Id == branch.OrganizationId && x.IsActive, cancellationToken)) return NotFound();
        var service = await db.Services.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.PublicId == servicePublicId && x.OrganizationId == branch.OrganizationId && x.BranchId == branch.Id && x.IsActive, cancellationToken);
        if (service is null || service.AttendanceMode == ServiceAttendanceMode.QueueOnly) return NotFound();
        var settings = await db.ServiceSchedulingSettings.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == branch.OrganizationId && x.BranchId == branch.Id && x.ServiceId == service.Id && x.IsActive, cancellationToken);
        if (settings is null) return NotFound();
        TimeZoneInfo timeZone;
        try { timeZone = TimeZoneInfo.FindSystemTimeZoneById(branch.TimeZone); }
        catch (TimeZoneNotFoundException) { return Result.Failure<PublicAvailabilityDto>(new("appointments.invalid_time_zone", "O fuso horário da unidade é inválido.")); }
        catch (InvalidTimeZoneException) { return Result.Failure<PublicAvailabilityDto>(new("appointments.invalid_time_zone", "O fuso horário da unidade é inválido.")); }
        var localDayStart = requestedDate.ToDateTime(TimeOnly.MinValue);
        var localDayEnd = requestedDate.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var dayStart = ToUtc(localDayStart, timeZone); var dayEnd = ToUtc(localDayEnd, timeZone);
        var schedules = await db.ServiceSchedules.IgnoreQueryFilters().AsNoTracking().Where(x => x.OrganizationId == branch.OrganizationId && x.BranchId == branch.Id && x.ServiceId == service.Id && x.IsActive && x.DayOfWeek == requestedDate.DayOfWeek).Select(x => new { x.StartTime, x.EndTime }).ToListAsync(cancellationToken);
        var blocks = await db.ScheduleBlocks.IgnoreQueryFilters().AsNoTracking().Where(x => x.OrganizationId == branch.OrganizationId && x.BranchId == branch.Id && (x.ServiceId == null || x.ServiceId == service.Id) && x.StartAt < dayEnd && x.EndAt > dayStart).Select(x => new SlotWindow(x.StartAt, x.EndAt)).ToListAsync(cancellationToken);
        var statuses = new[] { AppointmentStatus.Scheduled, AppointmentStatus.Confirmed, AppointmentStatus.CheckedIn };
        var reservations = await db.Appointments.IgnoreQueryFilters().AsNoTracking().Where(x => x.OrganizationId == branch.OrganizationId && x.BranchId == branch.Id && x.ServiceId == service.Id && statuses.Contains(x.Status) && x.ScheduledStart < dayEnd && x.ScheduledEnd > dayStart).Select(x => new SlotWindow(x.ScheduledStart, x.ScheduledEnd)).ToListAsync(cancellationToken);
        var slots = AppointmentSlotGenerator.Generate(requestedDate, timeZone, schedules.Select(x => (x.StartTime, x.EndTime)).ToArray(), settings.SlotDurationMinutes, settings.CapacityPerSlot, clock.UtcNow.AddMinutes(settings.MinimumAdvanceMinutes), clock.UtcNow.AddDays(settings.MaximumAdvanceDays), blocks, reservations);
        var organizationName = await db.Organizations.AsNoTracking().Where(x => x.Id == branch.OrganizationId).Select(x => x.Name).SingleAsync(cancellationToken);
        var availableDays = await db.ServiceSchedules.IgnoreQueryFilters().AsNoTracking().Where(x => x.OrganizationId == branch.OrganizationId && x.BranchId == branch.Id && x.ServiceId == service.Id && x.IsActive).Select(x => x.DayOfWeek).Distinct().OrderBy(x => x).ToListAsync(cancellationToken);
        var minimumLocal = TimeZoneInfo.ConvertTime(clock.UtcNow.AddMinutes(settings.MinimumAdvanceMinutes), timeZone);
        var maximumLocal = TimeZoneInfo.ConvertTime(clock.UtcNow.AddDays(settings.MaximumAdvanceDays), timeZone);
        return Result.Success(new PublicAvailabilityDto(branch.PublicId, service.PublicId, organizationName, branch.Name, service.Name, requestedDate, branch.TimeZone, DateOnly.FromDateTime(minimumLocal.DateTime), DateOnly.FromDateTime(maximumLocal.DateTime), availableDays, slots));
    }

    private static DateTimeOffset ToUtc(DateTime value, TimeZoneInfo zone) => new(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), zone), TimeSpan.Zero);
    private static Result<IReadOnlyList<AvailableSlotDto>> InvalidTimeZone() => Result.Failure<IReadOnlyList<AvailableSlotDto>>(new("appointments.invalid_time_zone", "O fuso horário da unidade é inválido."));
    private static bool ValidPublicId(string value) => value.Length == 32 && value.All(Uri.IsHexDigit);
    private static Result<PublicAvailabilityDto> NotFound() => Result.Failure<PublicAvailabilityDto>(new("appointments.service_not_found", "A agenda não foi encontrada."));
}
