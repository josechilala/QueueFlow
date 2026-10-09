using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueueFlow.Application.Features.Billing;

namespace QueueFlow.Api.Controllers;

/// <summary>Read-only public catalog; never accepts prices from clients.</summary>
[ApiController]
[Route("api/v1/billing/plans")]
[AllowAnonymous]
public sealed class BillingPlansController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new[]
    {
        ToResponse(BillingPlans.Professional),
        ToResponse(BillingPlans.Enterprise)
    });

    private static object ToResponse(BillingPlan plan) => new
    {
        code = plan.Code,
        name = plan.Name,
        monthlyPrice = plan.MonthlyPrice,
        annualPrice = plan.AnnualPrice,
        includedAttendants = plan.IncludedAttendants,
        currency = "BRL",
        annualDiscountPercent = 5
    };
}
