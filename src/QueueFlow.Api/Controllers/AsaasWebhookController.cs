using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueueFlow.Infrastructure.Billing;

namespace QueueFlow.Api.Controllers;

[ApiController]
[Route("api/v1/billing/webhooks/asaas")]
[AllowAnonymous]
public sealed class AsaasWebhookController(IServiceProvider services, IConfiguration configuration) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(64 * 1024)]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        if (!configuration.GetValue<bool>("Asaas:CheckoutEnabled") ||
            !string.Equals(configuration["Asaas:Environment"], "Sandbox", StringComparison.OrdinalIgnoreCase)) return StatusCode(503);
        var expected = configuration["Asaas:WebhookToken"];
        var supplied = Request.Headers["asaas-access-token"];
        if (string.IsNullOrWhiteSpace(expected) || expected.Length < 32 || supplied.Count != 1 ||
            supplied[0] is not string token || token.Length > 512 ||
            !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
                SHA256.HashData(Encoding.UTF8.GetBytes(token)))) return Unauthorized();
        JsonDocument document;
        try { document = await JsonDocument.ParseAsync(Request.Body, cancellationToken: ct); }
        catch (JsonException) { return BadRequest(); }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !Read(root, "id", 200, out var id) ||
                !Read(root, "event", 60, out var eventType)) return BadRequest();
            if (!eventType.StartsWith("PAYMENT_", StringComparison.Ordinal)) return Ok();
            if (!root.TryGetProperty("payment", out var payment) || payment.ValueKind != JsonValueKind.Object ||
                !Read(payment, "id", 100, out var paymentId)) return BadRequest();
            try
            {
                await services.GetRequiredService<BillingWebhookProcessor>().ProcessAsync(id, eventType, paymentId, ct);
                return Ok();
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or
                JsonException or InvalidOperationException or Microsoft.EntityFrameworkCore.DbUpdateException or QueueFlow.Domain.Common.DomainException)
            { return StatusCode(503); } // Provider retries; no event is acknowledged before durable processing.
        }
    }

    private static bool Read(JsonElement element, string key, int max, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(key, out var property) || property.ValueKind != JsonValueKind.String) return false;
        value = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value) && value.Length <= max;
    }
}
