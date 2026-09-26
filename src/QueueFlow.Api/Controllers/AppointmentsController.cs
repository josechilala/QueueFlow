using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueueFlow.Application.Features.Appointments;
using QueueFlow.Domain.Enums;

namespace QueueFlow.Api.Controllers;

[ApiController, Authorize(Policy = "AdminPanel"), Route("api/v1/appointments")]
public sealed class AppointmentsController(AppointmentManagementService appointments) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] AppointmentStatus? status, [FromQuery] Guid? branchId, [FromQuery] Guid? serviceId, CancellationToken cancellationToken) => Ok(await appointments.ListAsync(from, to, status, branchId, serviceId, cancellationToken));
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) => ToAction(await appointments.GetAsync(id, cancellationToken));
    [HttpPost("{id:guid}/confirm")]
    public async Task<IActionResult> Confirm(Guid id, CancellationToken cancellationToken) => ToAction(await appointments.ConfirmAsync(id, cancellationToken));
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, AppointmentReasonRequest request, CancellationToken cancellationToken) => ToAction(await appointments.CancelAsync(id, request.Reason, cancellationToken));
    [HttpPost("{id:guid}/no-show")]
    public async Task<IActionResult> NoShow(Guid id, AppointmentReasonRequest request, CancellationToken cancellationToken) => ToAction(await appointments.NoShowAsync(id, request.Reason, cancellationToken));
    private IActionResult ToAction<T>(QueueFlow.Application.Common.Result<T> result) => result.IsSuccess ? Ok(result.Value) : Problem(result.Error.Description, statusCode: 400);
}

public sealed record AppointmentReasonRequest(string? Reason);
