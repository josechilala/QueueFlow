using System.Net;
using Microsoft.Extensions.Configuration;
using QueueFlow.Infrastructure.Billing;

namespace QueueFlow.IntegrationTests.Persistence;

public sealed class AsaasSandboxTransportTests
{
    [Theory]
    [InlineData("https://api.asaas.com/v3/", "Sandbox")]
    [InlineData("https://api-sandbox.asaas.com/v3/", "Production")]
    public async Task ProductionIsRejectedBeforeSendingRequest(string url, string environment)
    {
        using var handler = new StubHandler(HttpStatusCode.OK);
        using var http = new HttpClient(handler) { BaseAddress = new Uri(url) };
        var client = new AsaasSandboxClient(http, Config(environment));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetPaymentAsync("pay_1", TestContext.Current.CancellationToken));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task ProviderErrorsDoNotExposeResponseBodyOrRetryMutation()
    {
        using var handler = new StubHandler(HttpStatusCode.BadRequest);
        using var http = new HttpClient(handler) { BaseAddress = new Uri(AsaasSandboxClient.SandboxBaseUrl) };
        var client = new AsaasSandboxClient(http, Config("Sandbox"));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.CreatePaymentAsync("cus_1", 69.90m,
            new DateOnly(2026, 10, 10), "PIX", "reference", TestContext.Current.CancellationToken));
        Assert.DoesNotContain("sensitive-provider-response", error.Message, StringComparison.Ordinal);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("../customers")]
    [InlineData("https://api.asaas.com")]
    public async Task ProviderIdsCannotEscapeSandboxPath(string id)
    {
        using var handler = new StubHandler(HttpStatusCode.OK);
        using var http = new HttpClient(handler) { BaseAddress = new Uri(AsaasSandboxClient.SandboxBaseUrl) };
        var client = new AsaasSandboxClient(http, Config("Sandbox"));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetPaymentAsync(id, TestContext.Current.CancellationToken));
        Assert.Equal(0, handler.Calls);
    }

    private static IConfiguration Config(string environment) => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["Asaas:Environment"] = environment, ["Asaas:SandboxApiKey"] = "fake-test-key" }).Build();

    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("sensitive-provider-response") });
        }
    }
}
