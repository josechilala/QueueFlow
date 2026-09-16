using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QueueFlow.Application.Common;
using QueueFlow.Application.Features.Platform;

namespace QueueFlow.Api.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ApiController, Route("api/v1/public/activation")]
public sealed class PublicActivationController(OrganizationInvitationService invitations) : ControllerBase
{
    [HttpPost("lookup"), EnableRateLimiting("public")]
    public async Task<IActionResult> Lookup(InvitationTokenRequest request, CancellationToken ct) => ToAction(await invitations.GetPublicAsync(request.InvitationToken, ct));
    [HttpPost("request-code"), EnableRateLimiting("public")]
    public async Task<IActionResult> RequestCodeBody(InvitationTokenRequest request, CancellationToken ct) => ToAction(await invitations.RequestVerificationCodeAsync(request.InvitationToken, ct));
    [HttpPost("verify"), EnableRateLimiting("public")]
    public async Task<IActionResult> VerifyBody(VerifyInvitationRequest request, CancellationToken ct) => ToAction(await invitations.VerifyCodeAsync(request.InvitationToken, request.Code, ct));
    [HttpPost("complete"), EnableRateLimiting("public")]
    public async Task<IActionResult> CompleteBody(CompleteInvitationRequest request, CancellationToken ct) => ToAction(await invitations.CompleteActivationAsync(request.InvitationToken, request.Activation, ct));
    [HttpGet("{token}"), EnableRateLimiting("public")] public async Task<IActionResult> Get(string token, CancellationToken ct) => ToAction(await invitations.GetPublicAsync(token, ct));
    [HttpPost("{token}/verification-code"), EnableRateLimiting("public")] public async Task<IActionResult> RequestCode(string token, CancellationToken ct) => ToAction(await invitations.RequestVerificationCodeAsync(token, ct));
    [HttpPost("{token}/verify"), EnableRateLimiting("public")] public async Task<IActionResult> Verify(string token, VerifyInvitationCodeRequest request, CancellationToken ct) { var result = await invitations.VerifyCodeAsync(token, request.Code, ct); return ToAction(result); }
    [HttpPost("{token}/complete"), EnableRateLimiting("public")] public async Task<IActionResult> Complete(string token, CompleteInvitationActivationCommand request, CancellationToken ct) => ToAction(await invitations.CompleteActivationAsync(token, request, ct));
    private IActionResult ToAction<T>(Result<T> result) => result.IsSuccess ? Ok(result.Value) : Failure(result.Error);
    private ObjectResult Failure(Error error) => Problem(error.Description, statusCode: error.Code switch { "invitation.not_found" => StatusCodes.Status404NotFound, "invitation.cooldown" or "invitation.too_many_attempts" => StatusCodes.Status429TooManyRequests, "invitation.email_unavailable" or "invitation.code_unavailable" => StatusCodes.Status503ServiceUnavailable, _ => StatusCodes.Status400BadRequest });
}

public sealed record VerifyInvitationCodeRequest(string Code);
public sealed record InvitationTokenRequest(string InvitationToken);
public sealed record VerifyInvitationRequest(string InvitationToken, string Code);
public sealed record CompleteInvitationRequest(string InvitationToken, CompleteInvitationActivationCommand Activation);
