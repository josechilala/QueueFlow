using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueueFlow.Application.Observability;

namespace QueueFlow.Api.Controllers;

[ApiController]
[Authorize(Policy = "RequirePlatformAdmin")]
[Route("api/v1/platform/billing/inbox/worker-status")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PlatformBillingDiagnosticsController(TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        var lastSuccessfulPollAtUtc = QueueFlowTelemetry.GetBillingLastSuccessfulPollAtUtc();
        long? ageSeconds = lastSuccessfulPollAtUtc is { } timestamp
            ? Math.Max(0, (long)(clock.GetUtcNow() - timestamp).TotalSeconds)
            : null;
        return Ok(new { lastSuccessfulPollAtUtc, ageSeconds });
    }
}
