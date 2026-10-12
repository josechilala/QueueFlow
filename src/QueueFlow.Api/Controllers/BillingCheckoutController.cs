using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueueFlow.Application.Features.Billing;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Infrastructure.Billing;

namespace QueueFlow.Api.Controllers;

[ApiController]
[Route("api/v1/billing")]
[Authorize(Policy = "SubscriptionManagement")]
public sealed class BillingCheckoutController(IServiceProvider services, IConfiguration configuration, ICurrentUser user, AsaasSandboxTenantAccess access) : ControllerBase
{
    private static readonly string[] SupportedPaymentMethods = ["Pix", "CreditCard"];
    private bool Enabled => configuration.GetValue<bool>("Asaas:CheckoutEnabled") &&
        string.Equals(configuration["Asaas:Environment"], "Sandbox", StringComparison.OrdinalIgnoreCase);

    [HttpGet("capabilities")]
    public IActionResult Capabilities() => Ok(new { checkoutEnabled = Enabled && access.IsAllowed(user.OrganizationId), paymentMethods = SupportedPaymentMethods });

    [HttpPost("checkout")]
    public async Task<IActionResult> Checkout(BillingCheckoutRequest request, CancellationToken ct)
    {
        if (!Enabled) return StatusCode(503, new { message = "Sandbox checkout has not been enabled." });
        if (!access.IsAllowed(user.OrganizationId)) return Forbid();
        return Ok(await services.GetRequiredService<IBillingCheckout>().StartAsync(request, ct));
    }

    [HttpGet("charges/{id:guid}")]
    public async Task<IActionResult> Charge(Guid id, CancellationToken ct)
    {
        if (!Enabled) return StatusCode(503);
        if (!access.IsAllowed(user.OrganizationId)) return Forbid();
        var result = await services.GetRequiredService<IBillingCheckout>().GetAsync(id, ct);
        return result is null ? NotFound() : Ok(result);
    }
}
