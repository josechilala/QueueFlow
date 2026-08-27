using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QueueFlow.Application.Features.Appointments;

namespace QueueFlow.Api.Controllers;

[ApiController, Route("api/v1/public")]
public sealed class PublicAppointmentsController(IAppointmentAvailabilityService availability, AppointmentBookingService booking, PublicAppointmentService appointments) : ControllerBase
{
    [HttpGet("branches/{branchPublicId}/services/{servicePublicId}/availability"), EnableRateLimiting("public")]
    public async Task<IActionResult> GetAvailability(string branchPublicId, string servicePublicId, [FromQuery] DateOnly date, CancellationToken cancellationToken)
    {
        var result = await availability.GetPublicAsync(branchPublicId, servicePublicId, date, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : NotFound();
    }

    [HttpPost("appointments"), EnableRateLimiting("public")]
    public async Task<IActionResult> Create(CreatePublicAppointment request, CancellationToken cancellationToken)
    {
        var result = await booking.CreateAsync(request, cancellationToken);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : Problem(result.Error.Description, statusCode: StatusCodes.Status409Conflict);
    }

    [HttpGet("appointments/{publicToken}"), EnableRateLimiting("public")]
    public async Task<IActionResult> Get(string publicToken, CancellationToken cancellationToken)
    {
        var result = await appointments.GetAsync(publicToken, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : NotFound();
    }
    [HttpPost("appointments/{publicToken}/cancel"), EnableRateLimiting("public")]
    public async Task<IActionResult> Cancel(string publicToken, CancellationToken cancellationToken) { var result = await appointments.CancelAsync(publicToken, cancellationToken); return result.IsSuccess ? Ok(result.Value) : Problem(result.Error.Description, statusCode: 409); }
    [HttpPost("appointments/{publicToken}/reschedule"), EnableRateLimiting("public")]
    public async Task<IActionResult> Reschedule(string publicToken, RescheduleAppointmentRequest request, CancellationToken cancellationToken) { var result = await appointments.RescheduleAsync(publicToken, request.ScheduledStart, cancellationToken); return result.IsSuccess ? Ok(result.Value) : Problem(result.Error.Description, statusCode: 409); }
    [HttpPost("appointments/{publicToken}/check-in"), EnableRateLimiting("public")]
    public async Task<IActionResult> CheckIn(string publicToken, CancellationToken cancellationToken) { var result = await appointments.CheckInAsync(publicToken, cancellationToken); return result.IsSuccess ? Ok(result.Value) : Problem(result.Error.Description, statusCode: 409); }
}

public sealed record RescheduleAppointmentRequest(DateTimeOffset ScheduledStart);
