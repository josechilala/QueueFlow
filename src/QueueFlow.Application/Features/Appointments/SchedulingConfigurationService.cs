using Microsoft.EntityFrameworkCore;
using QueueFlow.Application.Abstractions.Auditing;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Abstractions.Clock;
using QueueFlow.Application.Abstractions.Persistence;
using QueueFlow.Application.Common;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;

namespace QueueFlow.Application.Features.Appointments;

public sealed record SchedulingSettingsDto(Guid ServiceId, ServiceAttendanceMode AttendanceMode, int SlotDurationMinutes, int CapacityPerSlot, int MinimumAdvanceMinutes, int MaximumAdvanceDays, int LateToleranceMinutes, int CancellationDeadlineMinutes, int CheckInAdvanceMinutes, bool AllowCustomerCancellation, bool AllowCustomerReschedule, bool RequireConfirmation, bool IsActive);
public sealed record ScheduleDto(Guid Id, DayOfWeek DayOfWeek, TimeOnly StartTime, TimeOnly EndTime, bool IsActive);
public sealed record ScheduleBlockDto(Guid Id, Guid BranchId, Guid? ServiceId, DateTimeOffset StartAt, DateTimeOffset EndAt, string Reason, ScheduleBlockType BlockType);
public sealed record UpdateSchedulingSettings(ServiceAttendanceMode AttendanceMode, int SlotDurationMinutes, int CapacityPerSlot, int MinimumAdvanceMinutes, int MaximumAdvanceDays, int LateToleranceMinutes, int CancellationDeadlineMinutes, int CheckInAdvanceMinutes, bool AllowCustomerCancellation, bool AllowCustomerReschedule, bool RequireConfirmation, bool IsActive);

public sealed class SchedulingConfigurationService(IApplicationDbContext db, ICurrentUser currentUser, IClock clock, IAuditWriter audit)
{
    private Guid Tenant => currentUser.OrganizationId ?? throw new UnauthorizedAccessException();
    public async Task<Result<SchedulingSettingsDto>> GetSettingsAsync(Guid serviceId, CancellationToken ct)
    {
        var service = await db.Services.AsNoTracking().SingleOrDefaultAsync(x => x.Id == serviceId, ct);
        if (service is null) return NotFound<SchedulingSettingsDto>();
        var settings = await db.ServiceSchedulingSettings.AsNoTracking().SingleOrDefaultAsync(x => x.ServiceId == serviceId, ct);
        return Result.Success(ToDto(service, settings));
    }
    public async Task<Result<SchedulingSettingsDto>> UpdateSettingsAsync(Guid serviceId, UpdateSchedulingSettings request, CancellationToken ct)
    {
        var service = await db.Services.SingleOrDefaultAsync(x => x.Id == serviceId, ct);
        if (service is null) return NotFound<SchedulingSettingsDto>();
        var settings = await db.ServiceSchedulingSettings.SingleOrDefaultAsync(x => x.ServiceId == serviceId, ct);
        if (settings is null) { settings = new ServiceSchedulingSettings(Guid.NewGuid(), Tenant, service.BranchId, service.Id, clock.UtcNow); db.ServiceSchedulingSettings.Add(settings); }
        settings.Configure(request.SlotDurationMinutes, request.CapacityPerSlot, request.MinimumAdvanceMinutes, request.MaximumAdvanceDays, request.LateToleranceMinutes, request.CancellationDeadlineMinutes, request.CheckInAdvanceMinutes, request.AllowCustomerCancellation, request.AllowCustomerReschedule, request.RequireConfirmation, request.IsActive, clock.UtcNow);
        service.SetAttendanceMode(request.AttendanceMode, clock.UtcNow);
        audit.Write("scheduling.settings.updated", "Service", service.Id, request);
        await db.SaveChangesAsync(ct);
        return Result.Success(ToDto(service, settings));
    }
    public async Task<Result<IReadOnlyList<ScheduleDto>>> GetSchedulesAsync(Guid serviceId, CancellationToken ct)
    {
        if (!await db.Services.AnyAsync(x => x.Id == serviceId, ct)) return NotFound<IReadOnlyList<ScheduleDto>>();
        return Result.Success<IReadOnlyList<ScheduleDto>>(await db.ServiceSchedules.AsNoTracking().Where(x => x.ServiceId == serviceId).OrderBy(x => x.DayOfWeek).ThenBy(x => x.StartTime).Select(x => new ScheduleDto(x.Id, x.DayOfWeek, x.StartTime, x.EndTime, x.IsActive)).ToListAsync(ct));
    }
    public async Task<Result<ScheduleDto>> AddScheduleAsync(Guid serviceId, DayOfWeek day, TimeOnly start, TimeOnly end, CancellationToken ct)
    {
        var service = await db.Services.SingleOrDefaultAsync(x => x.Id == serviceId, ct); if (service is null) return NotFound<ScheduleDto>();
        var schedule = new ServiceSchedule(Guid.NewGuid(), Tenant, service.BranchId, service.Id, day, start, end, clock.UtcNow); db.ServiceSchedules.Add(schedule); audit.Write("scheduling.schedule.created", "ServiceSchedule", schedule.Id); await db.SaveChangesAsync(ct); return Result.Success(new ScheduleDto(schedule.Id, schedule.DayOfWeek, schedule.StartTime, schedule.EndTime, schedule.IsActive));
    }
    public async Task<Result> DeleteScheduleAsync(Guid serviceId, Guid scheduleId, CancellationToken ct)
    {
        var schedule = await db.ServiceSchedules.SingleOrDefaultAsync(x => x.Id == scheduleId && x.ServiceId == serviceId, ct); if (schedule is null) return Result.Failure(new("scheduling.schedule_not_found", "O horário não foi encontrado.")); db.ServiceSchedules.Remove(schedule); audit.Write("scheduling.schedule.deleted", "ServiceSchedule", schedule.Id); await db.SaveChangesAsync(ct); return Result.Success();
    }
    public async Task<IReadOnlyList<ScheduleBlockDto>> GetBlocksAsync(Guid? serviceId, CancellationToken ct) => await db.ScheduleBlocks.AsNoTracking().Where(x => serviceId == null || x.ServiceId == serviceId).OrderBy(x => x.StartAt).Select(x => new ScheduleBlockDto(x.Id, x.BranchId, x.ServiceId, x.StartAt, x.EndAt, x.Reason, x.BlockType)).ToListAsync(ct);
    public async Task<Result<ScheduleBlockDto>> AddBlockAsync(Guid branchId, Guid? serviceId, DateTimeOffset start, DateTimeOffset end, string reason, ScheduleBlockType type, CancellationToken ct)
    {
        var valid = await db.Branches.AnyAsync(x => x.Id == branchId && x.IsActive, ct) && (serviceId is null || await db.Services.AnyAsync(x => x.Id == serviceId && x.BranchId == branchId, ct)); if (!valid) return Result.Failure<ScheduleBlockDto>(new("scheduling.catalog_not_found", "Unidade ou serviço inválido.")); var block = new ScheduleBlock(Guid.NewGuid(), Tenant, branchId, serviceId, start, end, reason, type, currentUser.UserId ?? throw new UnauthorizedAccessException(), clock.UtcNow); db.ScheduleBlocks.Add(block); audit.Write("scheduling.block.created", "ScheduleBlock", block.Id); await db.SaveChangesAsync(ct); return Result.Success(new ScheduleBlockDto(block.Id, block.BranchId, block.ServiceId, block.StartAt, block.EndAt, block.Reason, block.BlockType));
    }
    public async Task<Result> DeleteBlockAsync(Guid id, CancellationToken ct) { var block = await db.ScheduleBlocks.SingleOrDefaultAsync(x => x.Id == id, ct); if (block is null) return Result.Failure(new("scheduling.block_not_found", "O bloqueio não foi encontrado.")); db.ScheduleBlocks.Remove(block); audit.Write("scheduling.block.deleted", "ScheduleBlock", id); await db.SaveChangesAsync(ct); return Result.Success(); }
    private static SchedulingSettingsDto ToDto(Service service, ServiceSchedulingSettings? value) => new(service.Id, service.AttendanceMode, value?.SlotDurationMinutes ?? 30, value?.CapacityPerSlot ?? 1, value?.MinimumAdvanceMinutes ?? 60, value?.MaximumAdvanceDays ?? 30, value?.LateToleranceMinutes ?? 10, value?.CancellationDeadlineMinutes ?? 60, value?.CheckInAdvanceMinutes ?? 30, value?.AllowCustomerCancellation ?? true, value?.AllowCustomerReschedule ?? true, value?.RequireConfirmation ?? false, value?.IsActive ?? false);
    private static Result<T> NotFound<T>() => Result.Failure<T>(new("scheduling.service_not_found", "O serviço não foi encontrado."));
}
