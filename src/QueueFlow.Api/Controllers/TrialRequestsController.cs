using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QueueFlow.Application.Common;
using QueueFlow.Application.Features.Platform;

namespace QueueFlow.Api.Controllers;

[ApiController, Route("api/v1/public/trial-requests")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PublicTrialRequestsController(TrialRequestService service) : ControllerBase
{
    [HttpPost, AllowAnonymous, EnableRateLimiting("trial-requests"), RequestSizeLimit(8192)]
    public async Task<IActionResult> Create(CreateTrialRequestCommand command, CancellationToken ct)
    {
        var result = await service.CreateAsync(command, ct);
        return result.IsSuccess ? Accepted(new { message = "Solicitação recebida. Vamos analisar seus dados e você receberá as próximas instruções por e-mail." })
            : Problem(result.Error.Description, statusCode: StatusCodes.Status400BadRequest);
    }
}

[ApiController, Authorize(Policy = "RequirePlatformAdmin"), Route("api/v1/platform/trial-requests")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PlatformTrialRequestsController(TrialRequestService service) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> List(CancellationToken ct) => Ok(await service.ListAsync(ct));
    [HttpGet("{id:guid}")] public async Task<IActionResult> Get(Guid id, CancellationToken ct) { var result = await service.GetAsync(id, ct); return result.IsSuccess ? Ok(result.Value) : Failure(result.Error); }
    [HttpPost("{id:guid}/approve")] public async Task<IActionResult> Approve(Guid id, CancellationToken ct) { var result = await service.DecideAsync(id, true, ct); return result.IsSuccess ? Ok(result.Value) : Failure(result.Error); }
    [HttpPost("{id:guid}/reject")] public async Task<IActionResult> Reject(Guid id, CancellationToken ct) { var result = await service.DecideAsync(id, false, ct); return result.IsSuccess ? Ok(result.Value) : Failure(result.Error); }
    private ObjectResult Failure(Error error) => Problem(error.Description, statusCode: error.Code switch {
        "platform.forbidden" => 403, "trial_request.not_found" => 404, "trial_request.decided" => 409,
        "trial_request.delivery_failed" or "invitation.email_unavailable" => 503, _ => 400 });
}
