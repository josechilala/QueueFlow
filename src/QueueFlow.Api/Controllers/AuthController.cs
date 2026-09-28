using QueueFlow.Api.Middleware;
using QueueFlow.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using QueueFlow.Application.Features.Auth;
using QueueFlow.Application.Features.Tenants;

namespace QueueFlow.Api.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ApiController, Route("api/v1/auth")]
public sealed class AuthController(AuthService auth, LoginProtection protection, TenantService tenants, IConfiguration configuration, IHostEnvironment environment) : ControllerBase
{
    [HttpPost("register"), EnableRateLimiting("auth")]
    public async Task<IActionResult> Register(CreateOrganizationCommand request, CancellationToken ct)
    {
        var allowed = configuration.GetValue("Onboarding:AllowPublicRegistration", false) && (environment.IsDevelopment() || environment.IsEnvironment("Test"));
        if (!allowed) return NotFound();
        var result = await tenants.CreateOrganizationAsync(request, ct);
        return result.IsSuccess ? Created(string.Empty, result.Value) : Problem(result.Error.Description, statusCode: 400);
    }
    [HttpPost("login"), EnableRateLimiting("login-input")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct) =>
        this.LoginResponse(await protection.ExecuteAsync(IdentityType.Tenant, request.Email,
            token => auth.LoginAsync(request.Email, request.Password, token), ct));
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshRequest request, CancellationToken ct)
    {
        var result = await auth.RefreshAsync(request.RefreshToken, ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error.Description,
            statusCode: result.Error.Code == "auth.refresh_conflict" ? StatusCodes.Status409Conflict : StatusCodes.Status401Unauthorized,
            extensions: new Dictionary<string, object?> { ["code"] = result.Error.Code });
    }
    [HttpGet("me"), Authorize(Policy = "TenantIdentity")]
    public async Task<IActionResult> Me(CancellationToken ct) { var result = await auth.GetCurrentAsync(ct); return result.IsSuccess ? Ok(result.Value) : Problem(result.Error.Description, statusCode: StatusCodes.Status401Unauthorized); }
}

public sealed record LoginRequest(
    [System.ComponentModel.DataAnnotations.Required] string Email,
    [System.ComponentModel.DataAnnotations.Required] string Password);
public sealed record RefreshRequest(string RefreshToken);
