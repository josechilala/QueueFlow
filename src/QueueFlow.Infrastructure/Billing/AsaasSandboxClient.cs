using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace QueueFlow.Infrastructure.Billing;

/// <summary>
/// Sandbox-only transport. Does not activate subscriptions or persist card data.
/// </summary>
public sealed class AsaasSandboxClient(HttpClient http, IConfiguration configuration)
{
    public const string SandboxBaseUrl = "https://api-sandbox.asaas.com/v3/";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? payload, CancellationToken ct)
    {
        var key = configuration["Asaas:SandboxApiKey"];
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Asaas sandbox API key is not configured.");
        using var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation("access_token", key);
        request.Headers.Accept.ParseAdd("application/json");
        if (payload is not null) request.Content = JsonContent.Create(payload, options: JsonOptions);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Asaas sandbox request failed ({(int)response.StatusCode}).", null, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Asaas returned an empty response.");
    }

    public Task<AsaasCustomerResponse> CreateCustomerAsync(string name, string email, string cpfCnpj, string externalReference, CancellationToken ct) =>
        SendAsync<AsaasCustomerResponse>(HttpMethod.Post, "customers",
            new { name, email, cpfCnpj, externalReference }, ct);

    public Task<AsaasSubscriptionResponse> CreateSubscriptionAsync(string customerId, decimal amount,
        DateOnly nextDueDate, string cycle, string billingType, string externalReference, CancellationToken ct)
    {
        if (amount <= 0 || string.IsNullOrWhiteSpace(customerId))
            throw new ArgumentException("Valid customer and amount are required.");
        if (cycle is not ("MONTHLY" or "YEARLY"))
            throw new ArgumentOutOfRangeException(nameof(cycle));
        if (billingType is not ("PIX" or "CREDIT_CARD"))
            throw new ArgumentOutOfRangeException(nameof(billingType));
        return SendAsync<AsaasSubscriptionResponse>(HttpMethod.Post, "subscriptions",
            new { customer = customerId, value = amount, nextDueDate = nextDueDate.ToString("yyyy-MM-dd"),
                cycle, billingType, externalReference }, ct);
    }


    // Annual Pix and one-off charges must be paid upfront. No installment or card data is accepted.
    public Task<AsaasPaymentResponse> CreatePaymentAsync(string customerId, decimal amount,
        DateOnly dueDate, string billingType, string externalReference, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(customerId) || amount <= 0)
            throw new ArgumentException("Valid customer and amount are required.");
        if (billingType is not ("PIX" or "CREDIT_CARD"))
            throw new ArgumentOutOfRangeException(nameof(billingType));
        if (string.IsNullOrWhiteSpace(externalReference))
            throw new ArgumentException("An external reference is required.", nameof(externalReference));
        return SendAsync<AsaasPaymentResponse>(HttpMethod.Post, "payments",
            new { customer = customerId, value = amount, dueDate = dueDate.ToString("yyyy-MM-dd"),
                billingType, externalReference }, ct);
    }

    public Task<AsaasPaymentResponse> GetPaymentAsync(string paymentId, CancellationToken ct)
    {
        ValidateProviderId(paymentId);
        return SendAsync<AsaasPaymentResponse>(HttpMethod.Get, $"payments/{paymentId}", null, ct);
    }

    public Task<AsaasSubscriptionResponse> GetSubscriptionAsync(string subscriptionId, CancellationToken ct)
    {
        ValidateProviderId(subscriptionId);
        return SendAsync<AsaasSubscriptionResponse>(HttpMethod.Get, $"subscriptions/{subscriptionId}", null, ct);
    }

    private static void ValidateProviderId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100 || !id.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            throw new ArgumentException("Invalid provider ID.", nameof(id));
    }

    public Task<AsaasPixQrCodeResponse> GetPixQrCodeAsync(string paymentId, CancellationToken ct)
    {
        ValidateProviderId(paymentId);
        return SendAsync<AsaasPixQrCodeResponse>(HttpMethod.Get, $"payments/{paymentId}/pixQrCode", null, ct);
    }
}

public sealed record AsaasCustomerResponse([property: JsonPropertyName("id")] string Id);
public sealed record AsaasSubscriptionResponse([property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string? Status);
public sealed record AsaasPixQrCodeResponse(
    [property: JsonPropertyName("encodedImage")] string? EncodedImage,
    [property: JsonPropertyName("payload")] string? Payload,
    [property: JsonPropertyName("expirationDate")] string? ExpirationDate);

public sealed record AsaasPaymentResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("invoiceUrl")] string? InvoiceUrl,
    [property: JsonPropertyName("billingType")] string? BillingType,
    [property: JsonPropertyName("value")] decimal Value);
