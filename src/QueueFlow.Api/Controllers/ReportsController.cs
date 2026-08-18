using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueueFlow.Application.Features.Reports;

namespace QueueFlow.Api.Controllers;

[ApiController, Authorize, Route("api/v1")]
public sealed class ReportsController(ReportingService reporting) : ControllerBase
{
    [HttpGet("reports/operations")] public async Task<IActionResult> Operations(DateTimeOffset? from, CancellationToken ct) => Ok(await reporting.GetAsync(from ?? DateTimeOffset.UtcNow.AddDays(-1), ct));
    [HttpGet("dashboard")] public async Task<IActionResult> Dashboard(CancellationToken ct) => Ok(await reporting.GetAsync(DateTimeOffset.UtcNow.Date, ct));
}
