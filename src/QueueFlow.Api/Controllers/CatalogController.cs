using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueueFlow.Application.Features.Catalog;

namespace QueueFlow.Api.Controllers;

[ApiController, Authorize, Route("api/v1")]
public sealed class CatalogController(CatalogService catalog) : ControllerBase
{
    [Authorize(Policy = "AdminPanel"), HttpGet("branches")]
    public async Task<IActionResult> Branches(CancellationToken ct) => Ok(await catalog.GetBranchesAsync(ct));

    [Authorize(Policy = "AdminPanel"), HttpGet("branches/{id:guid}")]
    public async Task<IActionResult> GetBranch(Guid id, CancellationToken ct)
    {
        var result = await catalog.GetBranchAsync(id, ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error.Description, statusCode: StatusCodes.Status404NotFound);
    }

    [Authorize(Policy = "AdminPanel"), HttpPost("branches")]
    public async Task<IActionResult> CreateBranch(CreateBranchRequest request, CancellationToken ct)
    {
        var branch = await catalog.CreateBranchAsync(request.Name, request.Address, request.TimeZone, ct);
        return CreatedAtAction(nameof(GetBranch), new { id = branch.Id }, branch);
    }

    [Authorize(Policy = "AdminPanel"), HttpPut("branches/{id:guid}")]
    public async Task<IActionResult> UpdateBranch(Guid id, UpdateBranchRequest request, CancellationToken ct)
    {
        var result = await catalog.UpdateBranchAsync(id, request.Name, request.Address, request.TimeZone, ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error.Description, statusCode: StatusCodes.Status404NotFound);
    }

    [Authorize(Policy = "AdminPanel"), HttpPatch("branches/{id:guid}/status")]
    public async Task<IActionResult> SetBranchStatus(Guid id, SetBranchStatusRequest request, CancellationToken ct)
    {
        var result = await catalog.SetBranchActiveAsync(id, request.IsActive, ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error.Description, statusCode: StatusCodes.Status404NotFound);
    }
    [Authorize(Policy = "AdminPanel"), HttpGet("services")]
    public async Task<IActionResult> Services(CancellationToken ct) => Ok(await catalog.GetServicesAsync(ct));

    [Authorize(Policy = "AdminPanel"), HttpPost("services")]
    public async Task<IActionResult> CreateService(CreateServiceRequest request, CancellationToken ct)
    {
        var result = await catalog.CreateServiceAsync(request.BranchId, request.Name, request.Description, request.Prefix, request.AverageDurationMinutes, ct);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : Problem(result.Error.Description, statusCode: 400);
    }
    [Authorize(Policy = "AdminPanel"), HttpGet("counters")]
    public async Task<IActionResult> Counters(CancellationToken ct) => Ok(await catalog.GetCountersAsync(ct));

    [Authorize(Policy = "AdminPanel"), HttpGet("counters/{id:guid}")]
    public async Task<IActionResult> GetCounter(Guid id, CancellationToken ct)
    {
        var result = await catalog.GetCounterAsync(id, ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error.Description, statusCode: StatusCodes.Status404NotFound);
    }

    [Authorize(Policy = "AdminPanel"), HttpPost("counters")]
    public async Task<IActionResult> CreateCounter(CreateCounterRequest request, CancellationToken ct)
    {
        var result = await catalog.CreateCounterAsync(request.BranchId, request.Name, ct);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : Problem(result.Error.Description, statusCode: 400);
    }

    [Authorize(Policy = "AdminPanel"), HttpPut("counters/{id:guid}")]
    public async Task<IActionResult> UpdateCounter(Guid id, UpdateCounterRequest request, CancellationToken ct)
    {
        var result = await catalog.UpdateCounterAsync(id, request.Name, ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error.Description, statusCode: StatusCodes.Status404NotFound);
    }

    [Authorize(Policy = "AdminPanel"), HttpPatch("counters/{id:guid}/status")]
    public async Task<IActionResult> SetCounterStatus(Guid id, SetCounterStatusRequest request, CancellationToken ct)
    {
        var result = await catalog.SetCounterActiveAsync(id, request.IsActive, ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error.Description, statusCode: StatusCodes.Status404NotFound);
    }
}
public sealed record CreateBranchRequest(string Name, string? Address, string TimeZone);
public sealed record UpdateBranchRequest(string Name, string? Address, string TimeZone);
public sealed record SetBranchStatusRequest(bool IsActive);
public sealed record CreateServiceRequest(Guid BranchId, string Name, string? Description, string Prefix, int AverageDurationMinutes);
public sealed record CreateCounterRequest(Guid BranchId, string Name);
public sealed record UpdateCounterRequest(string Name);
public sealed record SetCounterStatusRequest(bool IsActive);
