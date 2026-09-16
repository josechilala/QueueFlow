using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueueFlow.Application.Features.Tenants;

namespace QueueFlow.Api.Controllers;

[ApiController, Route("api/v1/onboarding"), Authorize(Policy = "OrganizationManagement")]
public sealed class OnboardingController(OnboardingService onboarding) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(await onboarding.GetAsync(ct));

    [HttpPost("complete")]
    public async Task<IActionResult> Complete(CancellationToken ct)
    {
        var result = await onboarding.CompleteAsync(ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error.Description, statusCode: 400);
    }
}
