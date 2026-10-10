using System.ComponentModel.DataAnnotations;

namespace QueueFlow.Application.Features.Billing;

public sealed record BillingCheckoutRequest(
    [Required, StringLength(40)] string PlanCode,
    [Required, StringLength(20)] string Cycle,
    [Required, StringLength(30)] string PaymentMethod,
    [Required, StringLength(120, MinimumLength = 8)] string IdempotencyKey,
    [Required, StringLength(200)] string CustomerName,
    [Required, EmailAddress, StringLength(320)] string CustomerEmail,
    [Required, RegularExpression(@"^(\d{11}|\d{14})$")] string CpfCnpj);

public sealed record BillingChargeResponse(Guid Id, string PlanCode, string Cycle, string PaymentMethod,
    decimal Amount, string Currency, string Status, string? PaymentUrl = null,
    string? PixPayload = null, string? PixEncodedImage = null);

public interface IBillingCheckout
{
    Task<BillingChargeResponse> StartAsync(BillingCheckoutRequest request, CancellationToken ct);
    Task<BillingChargeResponse?> GetAsync(Guid id, CancellationToken ct);
}
