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

    private sealed class OfflineFactory(bool enable = false) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Test");
            builder.UseSetting("Authentication:JwtKey", "offline-billing-tests-jwt-key-of-at-least-32-characters");
            builder.UseSetting("ConnectionStrings:QueueFlowDatabase", "Host=127.0.0.1;Port=1;Database=model_only;Username=model_only;Password=model_only;Timeout=1");
            builder.UseSetting("Asaas:Environment", "Sandbox");
            builder.UseSetting("Asaas:SandboxApiKey", "fake-key-never-sent");
            builder.UseSetting("Asaas:WebhookToken", "offline-webhook-token-of-at-least-32-characters");
            builder.UseSetting("Asaas:CheckoutEnabled", enable.ToString());
            builder.UseSetting("Redis:UseBackplane", "false");
            for (var index = 0; index < 4; index++) builder.UseSetting($"Cors:Origins:{index}", "https://billing-test.example");
            builder.UseSetting("RateLimiting:UseRedis", "false");
            builder.ConfigureServices(services => services.RemoveAll<IHostedService>());
        }
    }
}
