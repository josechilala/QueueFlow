using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QueueFlow.Application.Features.Auth;
using QueueFlow.Application.Features.Platform;

namespace QueueFlow.Api.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ApiController, Route("api/v1/platform/auth")]
public sealed class PlatformAuthController(PlatformAuthService auth) : ControllerBase
{
    [HttpPost("login"), EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct) { var result = await auth.LoginAsync(request.Email, request.Password, ct); return result.IsSuccess ? Ok(result.Value) : Problem(result.Error.Description, statusCode: StatusCodes.Status401Unauthorized); }
    [HttpPost("refresh"), EnableRateLimiting("auth")]
    public async Task<IActionResult> Refresh(RefreshRequest request, CancellationToken ct) { var result = await auth.RefreshAsync(request.RefreshToken, ct); return result.IsSuccess ? Ok(result.Value) : Problem(result.Error.Description, statusCode: StatusCodes.Status401Unauthorized); }
    [HttpGet("me"), Authorize(Policy = "RequirePlatformAdmin")]
    public async Task<IActionResult> Me(CancellationToken ct) { var result = await auth.GetCurrentAsync(ct); return result.IsSuccess ? Ok(result.Value) : Problem(result.Error.Description, statusCode: StatusCodes.Status401Unauthorized); }
}
