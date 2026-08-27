using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueueFlow.Application.Features.Auditing;

namespace QueueFlow.Api.Controllers;

[ApiController, Authorize(Policy = "AuditRead"), Route("api/v1/audit")]
public sealed class AuditController(AuditQueryService audit) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(DateTimeOffset? from, DateTimeOffset? to, string? action, int page = 1, int pageSize = 50, CancellationToken ct = default) => Ok(await audit.GetAsync(from, to, action, page, pageSize, ct));
}
