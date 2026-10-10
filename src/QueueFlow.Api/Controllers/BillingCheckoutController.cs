using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueueFlow.Application.Features.Billing;

namespace QueueFlow.Api.Controllers;

[ApiController]
[Route("api/v1/billing")]
[Authorize(Policy = "SubscriptionManagement")]
public sealed class BillingCheckoutController(IServiceProvider services, IConfiguration configuration) : ControllerBase
{
    private static readonly string[] SupportedPaymentMethods = ["Pix", "CreditCard"];
    private bool Enabled => configuration.GetValue<bool>("Asaas:CheckoutEnabled") &&
        string.Equals(configuration["Asaas:Environment"], "Sandbox", StringComparison.OrdinalIgnoreCase);

    [HttpGet("capabilities")]
    public IActionResult Capabilities() => Ok(new { checkoutEnabled = Enabled, paymentMethods = SupportedPaymentMethods });

    [HttpPost("checkout")]
    public async Task<IActionResult> Checkout(BillingCheckoutRequest request, CancellationToken ct)
    {
        if (!Enabled) return StatusCode(503, new { message = "Sandbox checkout has not been enabled." });
        return Ok(await services.GetRequiredService<IBillingCheckout>().StartAsync(request, ct));
    }

    [HttpGet("charges/{id:guid}")]
    public async Task<IActionResult> Charge(Guid id, CancellationToken ct)
    {
        if (!Enabled) return StatusCode(503);
        var result = await services.GetRequiredService<IBillingCheckout>().GetAsync(id, ct);
        return result is null ? NotFound() : Ok(result);
    }
}
