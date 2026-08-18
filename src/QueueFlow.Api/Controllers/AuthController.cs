using Microsoft.AspNetCore.Mvc;
using QueueFlow.Application.Features.Auth;
using QueueFlow.Application.Features.Tenants;

namespace QueueFlow.Api.Controllers;

[ApiController, Route("api/v1/auth")]
public sealed class AuthController(AuthService auth, TenantService tenants) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(CreateOrganizationCommand request, CancellationToken ct) { var result = await tenants.CreateOrganizationAsync(request, ct); return result.IsSuccess ? Created(string.Empty, result.Value) : Problem(result.Error.Description, statusCode: 400); }
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct) { var result = await auth.LoginAsync(request.Email, request.Password, ct); return result.IsSuccess ? Ok(result.Value) : Unauthorized(); }
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshRequest request, CancellationToken ct) { var result = await auth.RefreshAsync(request.RefreshToken, ct); return result.IsSuccess ? Ok(result.Value) : Unauthorized(); }
}

public sealed record LoginRequest(string Email, string Password);
public sealed record RefreshRequest(string RefreshToken);
