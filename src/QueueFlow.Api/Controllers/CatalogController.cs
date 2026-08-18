using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueueFlow.Application.Features.Catalog;

namespace QueueFlow.Api.Controllers;

[ApiController, Authorize, Route("api/v1")]
public sealed class CatalogController(CatalogService catalog) : ControllerBase
{
    [HttpGet("branches")] public async Task<IActionResult> Branches(CancellationToken ct) => Ok(await catalog.GetBranchesAsync(ct));
    [HttpPost("branches")] public async Task<IActionResult> CreateBranch(CreateBranchRequest request, CancellationToken ct) => Ok(await catalog.CreateBranchAsync(request.Name, request.TimeZone, ct));
    [HttpGet("services")] public async Task<IActionResult> Services(CancellationToken ct) => Ok(await catalog.GetServicesAsync(ct));
    [HttpPost("services")] public async Task<IActionResult> CreateService(CreateServiceRequest request, CancellationToken ct) { var result = await catalog.CreateServiceAsync(request.BranchId, request.Name, request.Prefix, request.AverageDurationMinutes, ct); return result.IsSuccess ? Ok(result.Value) : Problem(result.Error.Description, statusCode: 400); }
    [HttpPost("counters")] public async Task<IActionResult> CreateCounter(CreateCounterRequest request, CancellationToken ct) => Ok(await catalog.CreateCounterAsync(request.BranchId, request.Name, ct));
}
public sealed record CreateBranchRequest(string Name, string TimeZone);
public sealed record CreateServiceRequest(Guid BranchId, string Name, string Prefix, int AverageDurationMinutes);
public sealed record CreateCounterRequest(Guid BranchId, string Name);
