using System.Net;
using Microsoft.Extensions.Configuration;
using QueueFlow.Infrastructure.Billing;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;

namespace QueueFlow.IntegrationTests.Persistence;

public sealed class AsaasSandboxTransportTests
{
    [Fact]
    public async Task EveryOperationSendsUserAgentWithoutRetry()
    {
        using var handler = new StubHandler(HttpStatusCode.BadRequest);
        using var http = new HttpClient(handler) { BaseAddress = new Uri(AsaasSandboxClient.SandboxBaseUrl) };
        var client = new AsaasSandboxClient(http, Config("Sandbox"));
        var ct = TestContext.Current.CancellationToken;
        Func<Task>[] operations = [
            () => client.CreateCustomerAsync("private-name", "private-email", "private-document", "private-reference", ct),
            () => client.CreateSubscriptionAsync("cus_1", 10m, new DateOnly(2026, 10, 13), "MONTHLY", "PIX", "private-reference", ct),
            () => client.CreatePaymentAsync("cus_1", 10m, new DateOnly(2026, 10, 13), "PIX", "private-reference", ct),
            () => client.GetPaymentAsync("pay_1", ct),
            () => client.GetSubscriptionAsync("sub_1", ct),
            () => client.GetSubscriptionPaymentsAsync("sub_1", ct),
            () => client.FindPaymentsAsync("private-reference", ct),
            () => client.GetPixQrCodeAsync("pay_1", ct)
        ];
        foreach (var operation in operations)
        {
            await Assert.ThrowsAsync<HttpRequestException>(operation);
            Assert.Equal("QueueFlow/1.0", handler.UserAgent);
            Assert.Equal("api-sandbox.asaas.com", handler.Host);
            Assert.Equal("fake-test-key", handler.Token);
        }
        Assert.Equal(operations.Length, handler.Calls);
    }

    [Fact]
    public async Task TimeoutIsRethrownAndLoggedWithoutSensitiveExceptionMessage()
    {
        using var handler = new TimeoutHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri(AsaasSandboxClient.SandboxBaseUrl) };
        var logger = new CaptureLogger();
        var client = new AsaasSandboxClient(http, Config("Sandbox"), logger);
        await Assert.ThrowsAsync<TaskCanceledException>(() => client.CreateCustomerAsync(
            "private-name", "private-email", "private-document", "private-reference", TestContext.Current.CancellationToken));
        Assert.Equal(1, handler.Calls);
        Assert.Contains("CreateCustomer", logger.Message);
        Assert.Contains("TaskCanceledException", logger.Message);
        Assert.DoesNotContain("private", logger.Message, StringComparison.Ordinal);
        Assert.Null(logger.Exception);
    }

    private sealed class TimeoutHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; throw new TaskCanceledException("private-provider-message"); }
    }

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
        var logger = new CaptureLogger();
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { TraceIdentifier = "checkout-test" } };
        var client = new AsaasSandboxClient(http, Config("Sandbox"), logger, accessor);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.CreatePaymentAsync("cus_1", 69.90m,
            new DateOnly(2026, 10, 10), "PIX", "reference", TestContext.Current.CancellationToken));
        Assert.DoesNotContain("sensitive-provider-response", error.Message, StringComparison.Ordinal);
        Assert.Equal(1, handler.Calls);
        Assert.Equal("QueueFlow/1.0", handler.UserAgent);
        Assert.Equal("fake-test-key", handler.Token);
        Assert.Equal("api-sandbox.asaas.com", handler.Host);
        Assert.Contains("CreatePayment", logger.Message);
        Assert.Contains("400", logger.Message);
        Assert.Contains("HttpRequestException", logger.Message);
        Assert.Contains("checkout-test", logger.Message);
        Assert.Contains("DurationMs", logger.Message);
        Assert.Null(logger.Exception);
        foreach (var sensitive in new[] { "sensitive-provider-response", "fake-test-key", "cus_1", "reference" })
            Assert.DoesNotContain(sensitive, logger.Message, StringComparison.Ordinal);
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
        public string? UserAgent { get; private set; }
        public string? Token { get; private set; }
        public string? Host { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            UserAgent = request.Headers.UserAgent.ToString();
            Token = request.Headers.GetValues("access_token").Single();
            Host = request.RequestUri!.Host;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("sensitive-provider-response") });
        }
    }

    private sealed class CaptureLogger : ILogger<AsaasSandboxClient>
    {
        public string Message { get; private set; } = string.Empty;
        public Exception? Exception { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        { Message = formatter(state, exception); Exception = exception; }
    }
}
