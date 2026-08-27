using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueueFlow.Application.Features.Queues;

namespace QueueFlow.Api.Controllers;

[ApiController, Authorize(Policy = "AttendantPanel"), Route("api/v1/operations")]
public sealed class OperationsController(QueueOperationsService service) : ControllerBase
{
    [HttpGet("context")]
    public async Task<IActionResult> Context(CancellationToken ct)
    {
        var result = await service.GetAttendantContextAsync(ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error.Description, statusCode: 400);
    }
}
