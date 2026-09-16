using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueueFlow.Application.Common;
using QueueFlow.Application.Features.Platform;

namespace QueueFlow.Api.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ApiController, Authorize(Policy = "RequirePlatformAdmin"), Route("api/v1/platform")]
public sealed class PlatformController(PlatformAdministrationService administration, OrganizationInvitationService invitations) : ControllerBase
{
    [HttpGet("dashboard")] public async Task<IActionResult> Dashboard(CancellationToken ct) => Ok(await administration.GetDashboardAsync(ct));
    [HttpGet("organizations")] public async Task<IActionResult> Organizations(CancellationToken ct) => Ok(await administration.GetOrganizationsAsync(ct));
    [HttpGet("subscriptions")] public async Task<IActionResult> Subscriptions(CancellationToken ct) => Ok(await administration.GetSubscriptionsAsync(ct));
    [HttpGet("audit")] public async Task<IActionResult> Audit(CancellationToken ct) => Ok(await administration.GetAuditAsync(ct));
    [HttpGet("invitations")] public async Task<IActionResult> Invitations(CancellationToken ct) => Ok(await invitations.ListAsync(ct));
    [HttpPost("invitations")] public async Task<IActionResult> CreateInvitation(CreateOrganizationInvitationCommand request, CancellationToken ct) { var result = await invitations.CreateAsync(request, ct); return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : Failure(result.Error); }
    [HttpPost("invitations/{id:guid}/revoke")] public async Task<IActionResult> Revoke(Guid id, CancellationToken ct) { var result = await invitations.RevokeAsync(id, ct); return result.IsSuccess ? NoContent() : Failure(result.Error); }
    [HttpPost("invitations/{id:guid}/reissue")] public async Task<IActionResult> Reissue(Guid id, CancellationToken ct) { var result = await invitations.ReissueAsync(id, ct); return result.IsSuccess ? Ok(result.Value) : Failure(result.Error); }
    [HttpPost("invitations/{id:guid}/send"), Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("auth")]
    public async Task<IActionResult> Send(Guid id, SendInvitationRequest request, CancellationToken ct) { var result = await invitations.SendInvitationAsync(id, request.Token, ct); return result.IsSuccess ? NoContent() : Failure(result.Error); }
    private ObjectResult Failure(Error error) => Problem(error.Description, statusCode: error.Code switch { "platform.forbidden" => StatusCodes.Status403Forbidden, "invitation.not_found" => StatusCodes.Status404NotFound, _ => StatusCodes.Status400BadRequest });
}

public sealed record SendInvitationRequest(string Token);
