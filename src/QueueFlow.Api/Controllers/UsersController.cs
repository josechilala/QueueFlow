using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueueFlow.Application.Common;
using QueueFlow.Application.Features.Users;

namespace QueueFlow.Api.Controllers;

[ApiController, Authorize(Policy = "UserManagement"), Route("api/v1/users")]
public sealed class UsersController(UserManagementService users) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await users.ListAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => ToActionResult(await users.GetAsync(id, ct));

    [HttpPost]
    public async Task<IActionResult> Create(CreateManagedUserRequest request, CancellationToken ct)
    {
        var result = await users.CreateAsync(request, ct);
        return result.IsSuccess ? CreatedAtAction(nameof(Get), new { id = result.Value.Id }, result.Value) : Failure(result.Error);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateManagedUserRequest request, CancellationToken ct) => ToActionResult(await users.UpdateAsync(id, request, ct));

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> SetStatus(Guid id, SetManagedUserStatusRequest request, CancellationToken ct) => ToActionResult(await users.SetStatusAsync(id, request.IsActive, ct));

    [HttpPost("{id:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(Guid id, ResetManagedUserPasswordRequest request, CancellationToken ct)
    {
        var result = await users.ResetPasswordAsync(id, request.Password, ct);
        return result.IsSuccess ? NoContent() : Failure(result.Error);
    }

    private IActionResult ToActionResult(Result<ManagedUserDto> result) => result.IsSuccess ? Ok(result.Value) : Failure(result.Error);
    private ObjectResult Failure(Error error) => Problem(error.Description, statusCode: error.Code switch
    {
        "users.not_found" => StatusCodes.Status404NotFound,
        "users.forbidden" => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status400BadRequest,
    });
}
