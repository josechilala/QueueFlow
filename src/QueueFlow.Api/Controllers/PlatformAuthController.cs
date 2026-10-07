using QueueFlow.Api.Middleware;
using QueueFlow.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QueueFlow.Application.Features.Auth;
using QueueFlow.Application.Features.Platform;

namespace QueueFlow.Api.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ApiController, Route("api/v1/platform/auth")]
public sealed class PlatformAuthController(PlatformAuthService auth, LoginProtection protection, ILogger<PlatformAuthController> logger) : ControllerBase
{
    [HttpPost("login"), EnableRateLimiting("login-input")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        ProtectedLoginResult protectedResult;
        try
        {
            protectedResult = await protection.ExecuteAsync(IdentityType.Platform, request.Email,
                token => auth.LoginAsync(request.Email, request.Password, token), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception exception) { AuthOperationDiagnostics.LogFailure(logger, HttpContext, "platform_login", started, exception); throw; }
        var response = this.LoginResponse(protectedResult);
        var outcome = protectedResult.Result.IsSuccess ? "success" : protectedResult.Result.Error.Code switch
        {
            "platform.invalid_credentials" => "invalid_credentials",
            "auth.login_limited" => "rate_limited",
            "auth.temporarily_unavailable" => "redis_unavailable",
            _ => "unexpected_error",
        };
        AuthOperationDiagnostics.Log(logger, HttpContext, "platform_login", outcome, AuthOperationDiagnostics.Status(response), started, "login_protection");
        return response;
    }
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshRequest request, CancellationToken ct)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        QueueFlow.Application.Common.Result<TokenPair> result;
        try { result = await auth.RefreshAsync(request.RefreshToken, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception exception) { AuthOperationDiagnostics.LogFailure(logger, HttpContext, "platform_refresh", started, exception); throw; }
        var response = result.IsSuccess ? Ok(result.Value) : Problem(result.Error.Description,
            statusCode: result.Error.Code == "platform.refresh_conflict" ? StatusCodes.Status409Conflict : StatusCodes.Status401Unauthorized,
            extensions: new Dictionary<string, object?> { ["code"] = result.Error.Code });
        var outcome = result.IsSuccess ? "success" : result.Error.Code == "platform.refresh_conflict" ? "refresh_replay" : "refresh_rejected";
        AuthOperationDiagnostics.Log(logger, HttpContext, "platform_refresh", outcome, AuthOperationDiagnostics.Status(response), started,
            !result.IsSuccess && result.Error.Code == "platform.refresh_conflict" ? "revoked_token" : "refresh_rotation");
        return response;
    }
    [HttpGet("me"), Authorize(Policy = "RequirePlatformAdmin")]
    public async Task<IActionResult> Me(CancellationToken ct) { var result = await auth.GetCurrentAsync(ct); return result.IsSuccess ? Ok(result.Value) : Problem(result.Error.Description, statusCode: StatusCodes.Status401Unauthorized); }
}
