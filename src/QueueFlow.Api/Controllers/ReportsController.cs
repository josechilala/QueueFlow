using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueueFlow.Application.Features.Reports;

namespace QueueFlow.Api.Controllers;

[ApiController, Authorize, Route("api/v1")]
public sealed class ReportsController(ReportingService reporting) : ControllerBase
{
    [Authorize(Policy = "ReportRead"), HttpGet("reports/operations")] public async Task<IActionResult> Operations(DateTimeOffset? from, CancellationToken ct) => Ok(await reporting.GetAsync(from ?? DateTimeOffset.UtcNow.AddDays(-1), ct));
    [Authorize(Policy = "ReportRead"), HttpGet("reports")]
    public async Task<IActionResult> Management(DateTimeOffset? from, DateTimeOffset? to, Guid? branchId, Guid? serviceId, CancellationToken ct)
    {
        var end = to ?? DateTimeOffset.UtcNow; return Ok(await reporting.GetManagementReportAsync(from ?? end.AddDays(-30), end, branchId, serviceId, ct));
    }
    [Authorize(Policy = "AdminPanel")]
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(CancellationToken ct) => Ok(await reporting.GetDashboardAsync(ct));
}
