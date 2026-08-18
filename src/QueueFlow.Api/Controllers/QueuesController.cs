using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QueueFlow.Application.Features.Queues;
using QueueFlow.Domain.Enums;

namespace QueueFlow.Api.Controllers;

[ApiController, Authorize, Route("api/v1/queues")]
public sealed class QueuesController(QueueOperationsService service) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> List(CancellationToken ct) => Ok(await service.ListAsync(ct));
    [HttpPost] public async Task<IActionResult> Create(CreateQueueRequest request, CancellationToken ct) => ToActionResult(await service.CreateAsync(request.BranchId, request.ServiceId, request.Name, ct));
    [HttpPost("{id:guid}/open")] public async Task<IActionResult> Open(Guid id, CancellationToken ct) => ToActionResult(await service.OpenAsync(id, ct));
    [HttpPost("{id:guid}/pause")] public async Task<IActionResult> Pause(Guid id, CancellationToken ct) => ToActionResult(await service.PauseAsync(id, ct));
    [HttpPost("{id:guid}/close")] public async Task<IActionResult> Close(Guid id, CancellationToken ct) => ToActionResult(await service.CloseAsync(id, ct));
    [HttpPost("{id:guid}/call-next")] public async Task<IActionResult> CallNext(Guid id, CallNextRequest request, CancellationToken ct) => ToActionResult(await service.CallNextAsync(id, request.CounterId, ct));
    private IActionResult ToActionResult<T>(QueueFlow.Application.Common.Result<T> result) => result.IsSuccess ? Ok(result.Value) : Problem(result.Error.Description, statusCode: 400);
}
public sealed record CreateQueueRequest(Guid BranchId, Guid ServiceId, string Name);
public sealed record CallNextRequest(Guid CounterId);

[ApiController, Route("api/v1/public")]
public sealed class PublicQueueController(QueueOperationsService service) : ControllerBase
{
    [HttpPost("queues/{publicId}/tickets"), EnableRateLimiting("public")] public async Task<IActionResult> Issue(string publicId, IssueTicketRequest request, CancellationToken ct) { var result = await service.IssueAsync(publicId, request.Priority, ct); return result.IsSuccess ? Ok(result.Value) : Problem(result.Error.Description, statusCode: 400); }
    [HttpGet("tickets/{token}"), EnableRateLimiting("public")] public async Task<IActionResult> Status(string token, CancellationToken ct) { var result = await service.GetPublicAsync(token, ct); return result.IsSuccess ? Ok(result.Value) : NotFound(); }
}
public sealed record IssueTicketRequest(TicketPriority Priority);
