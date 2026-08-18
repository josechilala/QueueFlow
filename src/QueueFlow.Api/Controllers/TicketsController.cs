using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueueFlow.Application.Common;
using QueueFlow.Application.Features.Queues;

namespace QueueFlow.Api.Controllers;

[ApiController, Authorize, Route("api/v1/tickets")]
public sealed class TicketsController(QueueOperationsService service) : ControllerBase
{
    [HttpPost("{id:guid}/start")] public async Task<IActionResult> Start(Guid id, CancellationToken ct) => ToActionResult(await service.StartAsync(id, ct));
    [HttpPost("{id:guid}/complete")] public async Task<IActionResult> Complete(Guid id, CancellationToken ct) => ToActionResult(await service.CompleteAsync(id, ct));
    [HttpPost("{id:guid}/cancel")] public async Task<IActionResult> Cancel(Guid id, CancelTicketRequest request, CancellationToken ct) => ToActionResult(await service.CancelAsync(id, request.Reason, ct));
    [HttpPost("{id:guid}/no-show")] public async Task<IActionResult> NoShow(Guid id, CancellationToken ct) => ToActionResult(await service.NoShowAsync(id, ct));
    private IActionResult ToActionResult(Result<TicketDto> result) => result.IsSuccess ? Ok(result.Value) : Problem(result.Error.Description, statusCode: 400);
}
public sealed record CancelTicketRequest(string? Reason);
