using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Domain.Enums;

namespace QueueFlow.IntegrationTests.Persistence;

public sealed class BillingEndpointSecurityTests
{
    private static readonly Guid AllowedTenant = Guid.Parse("e98e5f2c-2c52-43de-8c78-f67c30e73588");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CapabilitiesReflectTenantPermission(bool allowed)
    {
        await using var factory = new OfflineFactory(enable: true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            factory.Services.GetRequiredService<ITokenService>().CreateAccessToken(Guid.NewGuid(), allowed ? AllowedTenant : Guid.NewGuid(), UserRole.Owner, "owner@example.test"));
        using var response = await client.GetAsync("/api/v1/billing/capabilities", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(allowed, body.GetProperty("checkoutEnabled").GetBoolean());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("11111111-1111-1111-1111-111111111111")]
    public async Task DisallowedTenantCannotCheckoutOrReadCharges(string? configuredTenant)
    {
        await using var factory = new OfflineFactory(enable: true, allowedTenant: configuredTenant);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            factory.Services.GetRequiredService<ITokenService>().CreateAccessToken(Guid.NewGuid(), AllowedTenant, UserRole.Owner, "owner@example.test"));
        using var capabilities = await client.GetAsync("/api/v1/billing/capabilities", TestContext.Current.CancellationToken);
        var body = await capabilities.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(TestContext.Current.CancellationToken);
        Assert.False(body.GetProperty("checkoutEnabled").GetBoolean());
        using var checkout = await client.PostAsJsonAsync("/api/v1/billing/checkout", new
        {
            planCode = "professional", cycle = "Monthly", paymentMethod = "Pix", idempotencyKey = "test-key-123",
            customerName = "Sandbox", customerEmail = "owner@example.test", cpfCnpj = "12345678901"
        }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, checkout.StatusCode);
        using var charge = await client.GetAsync($"/api/v1/billing/charges/{Guid.NewGuid()}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, charge.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task InvalidTenantConfigurationRejectsWebhookBeforeProcessing(string? configuredTenant)
    {
        await using var factory = new OfflineFactory(enable: true, allowedTenant: configuredTenant);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("asaas-access-token", "offline-webhook-token-of-at-least-32-characters");
        using var response = await client.PostAsJsonAsync("/api/v1/billing/webhooks/asaas", new { id = "event", @event = "PAYMENT_RECEIVED", payment = new { id = "pay_test" } }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(UserRole.Admin, HttpStatusCode.Forbidden)]
    [InlineData(UserRole.Manager, HttpStatusCode.Forbidden)]
    [InlineData(UserRole.Attendant, HttpStatusCode.Forbidden)]
    [InlineData(UserRole.Owner, HttpStatusCode.OK)]
    public async Task CapabilitiesRequireTenantOwner(UserRole? role, HttpStatusCode expected)
    {
        await using var factory = new OfflineFactory();
        using var client = factory.CreateClient();
        if (role.HasValue)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                factory.Services.GetRequiredService<ITokenService>().CreateAccessToken(Guid.NewGuid(), Guid.NewGuid(), role.Value, "owner@example.test"));
        using var response = await client.GetAsync("/api/v1/billing/capabilities", TestContext.Current.CancellationToken);
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task DisabledCheckoutCannotCallProviderEvenForOwner()
    {
        await using var factory = new OfflineFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            factory.Services.GetRequiredService<ITokenService>().CreateAccessToken(Guid.NewGuid(), Guid.NewGuid(), UserRole.Owner, "owner@example.test"));
        using var response = await client.PostAsJsonAsync("/api/v1/billing/checkout", new
        {
            planCode = "professional", cycle = "Monthly", paymentMethod = "Pix", idempotencyKey = "test-key-123",
            customerName = "Sandbox", customerEmail = "owner@example.test", cpfCnpj = "12345678901"
        }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task WebhookRejectsMissingTokenBeforeReadingPayloadOrContactingProvider()
    {
        await using var factory = new OfflineFactory(enable: true);
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/billing/webhooks/asaas", new { id = "event", @event = "PAYMENT_RECEIVED" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PlatformIdentityCannotReadTenantBilling()
    {
        await using var factory = new OfflineFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            factory.Services.GetRequiredService<ITokenService>().CreatePlatformAccessToken(Guid.NewGuid(), "platform@example.test"));
        using var response = await client.GetAsync("/api/v1/billing/capabilities", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("pay/invalid")]
    [InlineData("pay?invalid")]
    [InlineData("pay invalid")]
    public async Task WebhookRejectsInvalidProviderIdBeforeDatabaseOrProviderAccess(string paymentId)
    {
        await using var factory = new OfflineFactory(enable: true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("asaas-access-token", "offline-webhook-token-of-at-least-32-characters");
        using var response = await client.PostAsJsonAsync("/api/v1/billing/webhooks/asaas",
            new { id = "evt_invalid", @event = "PAYMENT_RECEIVED", payment = new { id = paymentId } }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("wrong-token")]
    [InlineData("duplicate")]
    public async Task WebhookRejectsIncorrectOrMultipleTokens(string scenario)
    {
        await using var factory = new OfflineFactory(enable: true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("asaas-access-token", scenario == "duplicate"
            ? "offline-webhook-token-of-at-least-32-characters" : scenario);
        if (scenario == "duplicate")
            client.DefaultRequestHeaders.Add("asaas-access-token", "offline-webhook-token-of-at-least-32-characters");
        using var response = await client.PostAsJsonAsync("/api/v1/billing/webhooks/asaas",
            new { id = "evt_invalid", @event = "PAYMENT_RECEIVED", payment = new { id = "pay_test" } }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("[]")]
    [InlineData("{\"id\":\"evt_invalid\",\"event\":\"PAYMENT_CONFIRMED\"}")]
    public async Task WebhookRejectsMalformedPayload(string payload)
    {
        await using var factory = new OfflineFactory(enable: true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("asaas-access-token", "offline-webhook-token-of-at-least-32-characters");
        using var body = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/v1/billing/webhooks/asaas", body, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task WebhookDoesNotAcknowledgeWhenDurableStorageIsUnavailable()
    {
        await using var factory = new OfflineFactory(enable: true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("asaas-access-token", "offline-webhook-token-of-at-least-32-characters");
        using var response = await client.PostAsJsonAsync("/api/v1/billing/webhooks/asaas",
            new { id = "evt_storage_unavailable", @event = "PAYMENT_RECEIVED", payment = new { id = "pay_test" } }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    internal sealed class OfflineFactory(bool enable = false, string? allowedTenant = "e98e5f2c-2c52-43de-8c78-f67c30e73588") : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Test");
            builder.UseSetting("Authentication:JwtKey", "offline-billing-tests-jwt-key-of-at-least-32-characters");
            builder.UseSetting("ConnectionStrings:QueueFlowDatabase", "Host=127.0.0.1;Port=1;Database=model_only;Username=model_only;Password=model_only;Timeout=1");
            builder.UseSetting("Asaas:Environment", "Sandbox");
            builder.UseSetting("Asaas:SandboxAllowedTenantId", allowedTenant ?? "");
            builder.UseSetting("Asaas:SandboxApiKey", "fake-key-never-sent");
            builder.UseSetting("Asaas:WebhookToken", "offline-webhook-token-of-at-least-32-characters");
            builder.UseSetting("Asaas:CheckoutEnabled", enable.ToString());
            builder.UseSetting("Redis:UseBackplane", "false");
            for (var index = 0; index < 4; index++) builder.UseSetting($"Cors:Origins:{index}", "https://billing-test.example");
            builder.UseSetting("RateLimiting:UseRedis", "false");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddScoped<IAccessTokenValidator, QueueFlow.IntegrationTests.Authentication.PolicyOnlyAccessTokenValidator>();
            });
        }
    }
}
