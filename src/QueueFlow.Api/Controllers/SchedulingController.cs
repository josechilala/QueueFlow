using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueueFlow.Application.Features.Appointments;
using QueueFlow.Domain.Enums;

namespace QueueFlow.Api.Controllers;

[ApiController, Authorize(Policy = "AdminPanel"), Route("api/v1")]
public sealed class SchedulingController(SchedulingConfigurationService service) : ControllerBase
{
    [HttpGet("services/{serviceId:guid}/scheduling-settings")] public async Task<IActionResult> Settings(Guid serviceId, CancellationToken ct) => ToAction(await service.GetSettingsAsync(serviceId, ct));
    [HttpPut("services/{serviceId:guid}/scheduling-settings")] public async Task<IActionResult> UpdateSettings(Guid serviceId, UpdateSchedulingSettings request, CancellationToken ct) => ToAction(await service.UpdateSettingsAsync(serviceId, request, ct));
    [HttpGet("services/{serviceId:guid}/schedules")] public async Task<IActionResult> Schedules(Guid serviceId, CancellationToken ct) => ToAction(await service.GetSchedulesAsync(serviceId, ct));
    [HttpPost("services/{serviceId:guid}/schedules")] public async Task<IActionResult> AddSchedule(Guid serviceId, ScheduleRequest request, CancellationToken ct) => ToAction(await service.AddScheduleAsync(serviceId, request.DayOfWeek, request.StartTime, request.EndTime, ct));
    [HttpDelete("services/{serviceId:guid}/schedules/{scheduleId:guid}")] public async Task<IActionResult> DeleteSchedule(Guid serviceId, Guid scheduleId, CancellationToken ct) => ToAction(await service.DeleteScheduleAsync(serviceId, scheduleId, ct));
    [HttpGet("schedule-blocks")] public async Task<IActionResult> Blocks([FromQuery] Guid? serviceId, CancellationToken ct) => Ok(await service.GetBlocksAsync(serviceId, ct));
    [HttpPost("schedule-blocks")] public async Task<IActionResult> AddBlock(ScheduleBlockRequest request, CancellationToken ct) => ToAction(await service.AddBlockAsync(request.BranchId, request.ServiceId, request.StartAt, request.EndAt, request.Reason, request.BlockType, ct));
    [HttpDelete("schedule-blocks/{id:guid}")] public async Task<IActionResult> DeleteBlock(Guid id, CancellationToken ct) => ToAction(await service.DeleteBlockAsync(id, ct));
    private IActionResult ToAction<T>(QueueFlow.Application.Common.Result<T> result) => result.IsSuccess ? Ok(result.Value) : Problem(result.Error.Description, statusCode: 400);
    private IActionResult ToAction(QueueFlow.Application.Common.Result result) => result.IsSuccess ? NoContent() : Problem(result.Error.Description, statusCode: 400);
}
public sealed record ScheduleRequest(DayOfWeek DayOfWeek, TimeOnly StartTime, TimeOnly EndTime);
public sealed record ScheduleBlockRequest(Guid BranchId, Guid? ServiceId, DateTimeOffset StartAt, DateTimeOffset EndAt, string Reason, ScheduleBlockType BlockType);
