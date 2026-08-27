using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QueueFlow.Application.Features.Queues;
using QueueFlow.Domain.Enums;

namespace QueueFlow.Api.Controllers;

[ApiController, Authorize, Route("api/v1/queues")]
public sealed class QueuesController(QueueOperationsService service) : ControllerBase
{
    [Authorize(Policy = "AdminPanel"), HttpGet] public async Task<IActionResult> List(CancellationToken ct) => Ok(await service.ListAsync(ct));
    [Authorize(Policy = "AdminPanel"), HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) { var result = await service.GetAsync(id, ct); return result.IsSuccess ? Ok(result.Value) : NotFound(); }
    [Authorize(Policy = "AdminPanel"), HttpPost]
    public async Task<IActionResult> Create(CreateQueueRequest request, CancellationToken ct) { var result = await service.CreateAsync(request.BranchId, request.ServiceId, request.Name, request.Capacity, ct); return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : Problem(result.Error.Description, statusCode: 400); }
    [Authorize(Policy = "AdminPanel"), HttpPost("{id:guid}/open")] public async Task<IActionResult> Open(Guid id, CancellationToken ct) => ToActionResult(await service.OpenAsync(id, ct));
    [Authorize(Policy = "AdminPanel"), HttpPost("{id:guid}/pause")] public async Task<IActionResult> Pause(Guid id, CancellationToken ct) => ToActionResult(await service.PauseAsync(id, ct));
    [Authorize(Policy = "AdminPanel"), HttpPost("{id:guid}/close")] public async Task<IActionResult> Close(Guid id, CancellationToken ct) => ToActionResult(await service.CloseAsync(id, ct));
    [Authorize(Policy = "AttendantPanel"), HttpPost("{id:guid}/call-next")] public async Task<IActionResult> CallNext(Guid id, CallNextRequest request, CancellationToken ct) => ToActionResult(await service.CallNextAsync(id, request.CounterId, ct));
    private IActionResult ToActionResult<T>(QueueFlow.Application.Common.Result<T> result) => result.IsSuccess ? Ok(result.Value) : Problem(result.Error.Description, statusCode: 400);
}
public sealed record CreateQueueRequest(Guid BranchId, Guid ServiceId, string Name, int? Capacity);
public sealed record CallNextRequest(Guid CounterId);

[ApiController, Route("api/v1/public")]
public sealed class PublicQueueController(QueueOperationsService service) : ControllerBase
{
    [HttpGet("organizations/{slug}")]
    public async Task<IActionResult> GetOrganization(string slug, CancellationToken ct) { var result = await service.GetPublicOrganizationAsync(slug, ct); return result.IsSuccess ? Ok(result.Value) : NotFound(); }
    [HttpGet("services/{publicId}")]
    public async Task<IActionResult> GetService(string publicId, CancellationToken ct) { var result = await service.GetPublicServiceAsync(publicId, ct); return result.IsSuccess ? Ok(result.Value) : NotFound(); }
    [HttpGet("branches/{publicId}")]
    public async Task<IActionResult> GetBranch(string publicId, CancellationToken ct) { var result = await service.GetPublicBranchAsync(publicId, ct); return result.IsSuccess ? Ok(result.Value) : NotFound(); }
    [HttpGet("queues/{publicId}")]
    public async Task<IActionResult> GetQueue(string publicId, CancellationToken ct) { var result = await service.GetPublicQueueAsync(publicId, ct); return result.IsSuccess ? Ok(result.Value) : NotFound(); }
    [HttpGet("displays/{branchPublicId}")]
    public async Task<IActionResult> GetDisplay(string branchPublicId, CancellationToken ct) { var result = await service.GetPublicDisplayAsync(branchPublicId, ct); return result.IsSuccess ? Ok(result.Value) : NotFound(); }
    [HttpPost("queues/{publicId}/tickets"), EnableRateLimiting("public")] public async Task<IActionResult> Issue(string publicId, IssueTicketRequest request, CancellationToken ct) { var result = await service.IssueAsync(publicId, request.Priority, ct); return result.IsSuccess ? Ok(result.Value) : Problem(result.Error.Description, statusCode: 400); }
    [HttpGet("tickets/{token}"), EnableRateLimiting("public")] public async Task<IActionResult> Status(string token, CancellationToken ct) { var result = await service.GetPublicAsync(token, ct); return result.IsSuccess ? Ok(result.Value) : NotFound(); }
    [HttpGet("tickets/{token}/notifications"), EnableRateLimiting("public")] public async Task<IActionResult> Notifications(string token, CancellationToken ct) { var result = await service.GetNotificationsAsync(token, ct); return result.IsSuccess ? Ok(result.Value) : NotFound(); }
    [HttpPost("tickets/{token}/notifications/{notificationId:guid}/read"), EnableRateLimiting("public")] public async Task<IActionResult> ReadNotification(string token, Guid notificationId, CancellationToken ct) { var result = await service.MarkNotificationReadAsync(token, notificationId, ct); return result.IsSuccess ? NoContent() : NotFound(); }
}
public sealed record IssueTicketRequest(TicketPriority Priority);
