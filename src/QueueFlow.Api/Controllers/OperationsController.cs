using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueueFlow.Application.Common;
using QueueFlow.Application.Features.Appointments;
using QueueFlow.Application.Features.Queues;

namespace QueueFlow.Api.Controllers;

[ApiController, Authorize(Policy = "AttendantPanel"), Route("api/v1/operations")]
public sealed class OperationsController(QueueOperationsService service, OperationalAppointmentService appointments) : ControllerBase
{
    [HttpGet("context")]
    public async Task<IActionResult> Context(CancellationToken ct)
    {
        var result = await service.GetAttendantContextAsync(ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error.Description, statusCode: 400);
    }

    [HttpGet("appointments/today")]
    public async Task<IActionResult> TodayAppointments(CancellationToken ct) => Ok(await appointments.ListTodayAsync(ct));

    [HttpPost("appointments/{id:guid}/check-in")]
    public async Task<IActionResult> ConfirmArrival(Guid id, CancellationToken ct)
    {
        var result = await appointments.ConfirmArrivalAsync(id, ct);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error);
    }

    private ObjectResult Failure(Error error) => Problem(error.Description, statusCode: error.Code switch
    {
        "appointments.not_found" => StatusCodes.Status404NotFound,
        "appointments.branch_forbidden" => StatusCodes.Status403Forbidden,
        "appointments.queue_unavailable" => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest,
    });
}
