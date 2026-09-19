using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QueueFlow.Application.Abstractions.Notifications;
using QueueFlow.Infrastructure;

namespace QueueFlow.IntegrationTests.Notifications;

public sealed class ActivationEmailSenderTests
{
    private const string ApiKey = "re_synthetic_test_key";
    private const string From = "QueueFlow <activation@example.test>";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("Development", false)]
    [InlineData("Development", true)]
    [InlineData("Test", false)]
    [InlineData("Test", true)]
    public async Task DevelopmentAndTestPreserveSimulation(string environment, bool configured)
    {
        using var handler = new RecordingHandler();
        await using var provider = CreateProvider(environment, handler, configured ? ApiKey : "", configured ? From : "");
        await using var scope = provider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<IActivationEmailSender>();
        Assert.True(sender.IsConfigured);
        Assert.False(sender.SupportsInvitationDelivery);
        await sender.SendVerificationCodeAsync("user@example.test", "123456", Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendInvitationAsync("user@example.test", "https://example.test/ativar#secret", Ct));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("Production", "", From)]
    [InlineData("Production", ApiKey, "")]
    [InlineData("Staging", "", "")]
    [InlineData("Production", "bad\r\nkey", From)]
    [InlineData("Production", ApiKey, "invalid-address")]
    [InlineData("Production", ApiKey, "sender@example.test\r\nBcc:other@example.test")]
    public async Task MissingOrInvalidConfigurationNeverSends(string environment, string key, string from)
    {
        using var handler = new RecordingHandler();
        await using var provider = CreateProvider(environment, handler, key, from);
        await using var scope = provider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<IActivationEmailSender>();
        Assert.False(sender.IsConfigured);
        Assert.False(sender.SupportsInvitationDelivery);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendVerificationCodeAsync("user@example.test", "123456", Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendInvitationAsync("user@example.test", "https://example.test/ativar#secret", Ct));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task ConfiguredProviderSendsBothMessagesWithCorrectPayloadAndNoSecretLogs(string environment)
    {
        using var handler = new RecordingHandler();
        var logs = new CapturingLoggerProvider();
        await using var provider = CreateProvider(environment, handler, logs: logs);
        await using var scope = provider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<IActivationEmailSender>();
        Assert.True(sender.IsConfigured);
        Assert.True(sender.SupportsInvitationDelivery);
        const string email = "recipient@example.test";
        const string code = "083719";
        const string url = "https://customer.example.test/ativar#secret-token";
        await sender.SendVerificationCodeAsync(email, code, Ct);
        await sender.SendInvitationAsync(email, url, Ct);
        Assert.Equal(2, handler.Requests.Count);
        foreach (var request in handler.Requests)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://api.resend.com/emails", request.Url);
            Assert.Equal($"Bearer {ApiKey}", request.Authorization);
            Assert.Equal("application/json", request.ContentType);
            using var body = JsonDocument.Parse(request.Body);
            Assert.Equal(From, body.RootElement.GetProperty("from").GetString());
            Assert.Equal(email, Assert.Single(body.RootElement.GetProperty("to").EnumerateArray()).GetString());
            Assert.Contains("QueueFlow", body.RootElement.GetProperty("subject").GetString());
            Assert.DoesNotContain(ApiKey, request.Body);
        }
        using var codeBody = JsonDocument.Parse(handler.Requests[0].Body);
        using var invitationBody = JsonDocument.Parse(handler.Requests[1].Body);
        Assert.Contains(code, codeBody.RootElement.GetProperty("text").GetString());
        Assert.Contains(url, invitationBody.RootElement.GetProperty("text").GetString());
        foreach (var secret in new[] { ApiKey, email, code, url })
            Assert.DoesNotContain(logs.Messages, message => message.Contains(secret, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(400, false)]
    [InlineData(401, true)]
    [InlineData(403, false)]
    [InlineData(429, true)]
    [InlineData(500, false)]
    [InlineData(302, true)]
    public async Task ProviderFailuresAreNotSuccessAndDoNotExposeResponseBodies(int status, bool invitation)
    {
        const string secret = "sensitive-provider-response";
        using var handler = new RecordingHandler { Status = (HttpStatusCode)status, ResponseBody = secret };
        var logs = new CapturingLoggerProvider();
        await using var provider = CreateProvider("Production", handler, logs: logs);
        await using var scope = provider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<IActivationEmailSender>();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => invitation
            ? sender.SendInvitationAsync("user@example.test", "https://example.test/ativar#token", Ct)
            : sender.SendVerificationCodeAsync("user@example.test", "123456", Ct));
        Assert.DoesNotContain(secret, error.ToString());
        Assert.DoesNotContain(logs.Messages, message => message.Contains(secret, StringComparison.Ordinal));
        Assert.Single(handler.Requests); // No automatic retry of a non-idempotent send.
    }

    [Fact]
    public async Task TransportFailuresAreSanitized()
    {
        using var handler = new RecordingHandler { Failure = new HttpRequestException("secret-transport-details") };
        await using var provider = CreateProvider("Production", handler);
        await using var scope = provider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<IActivationEmailSender>();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendVerificationCodeAsync("user@example.test", "123456", Ct));
        Assert.DoesNotContain("secret-transport-details", error.ToString());
    }

    [Fact]
    public async Task CancellationPropagates()
    {
        using var handler = new RecordingHandler();
        await using var provider = CreateProvider("Production", handler);
        await using var scope = provider.CreateAsyncScope();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var sender = scope.ServiceProvider.GetRequiredService<IActivationEmailSender>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sender.SendVerificationCodeAsync("user@example.test", "123456", cancellation.Token));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task EnvironmentVariablesBindResendOptions()
    {
        // Unique prefix avoids changing any real Resend settings or parallel tests.
        var prefix = $"QUEUEFLOW_TEST_{Guid.NewGuid():N}_";
        using var handler = new RecordingHandler();
        try
        {
            Environment.SetEnvironmentVariable(prefix + "Resend__ApiKey", ApiKey);
            Environment.SetEnvironmentVariable(prefix + "Resend__From", From);
            var configuration = new ConfigurationBuilder().AddEnvironmentVariables(prefix).Build();
            await using var provider = CreateProvider("Production", handler, extra: configuration);
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IActivationEmailSender>().SendVerificationCodeAsync("user@example.test", "123456", Ct);
            Assert.Equal($"Bearer {ApiKey}", Assert.Single(handler.Requests).Authorization);
        }
        finally
        {
            Environment.SetEnvironmentVariable(prefix + "Resend__ApiKey", null);
            Environment.SetEnvironmentVariable(prefix + "Resend__From", null);
        }
    }

    private static ServiceProvider CreateProvider(string environment, RecordingHandler handler, string key = ApiKey,
        string from = From, CapturingLoggerProvider? logs = null, IConfiguration? extra = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:QueueFlowDatabase"] = "Host=127.0.0.1;Database=unused",
            ["Resend:ApiKey"] = extra is null ? key : "",
            ["Resend:From"] = extra is null ? from : "",
        });
        if (extra is not null) configuration.AddConfiguration(extra);
        var services = new ServiceCollection();
        services.AddLogging(builder => { if (logs is not null) builder.AddProvider(logs).SetMinimumLevel(LogLevel.Trace); });
        services.AddSingleton<IHostEnvironment>(new TestEnvironment { EnvironmentName = environment });
        services.AddInfrastructure(configuration.Build());
        services.AddHttpClient("ResendActivation").ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "QueueFlow.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed record SentRequest(HttpMethod Method, string? Url, string? Authorization, string? ContentType, string Body);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<SentRequest> Requests { get; } = [];
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public string ResponseBody { get; init; } = "{\"id\":\"synthetic-email-id\"}";
        public Exception? Failure { get; init; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure is not null) throw Failure;
            Requests.Add(new(request.Method, request.RequestUri?.AbsoluteUri, request.Headers.Authorization?.ToString(),
                request.Content?.Headers.ContentType?.MediaType, await request.Content!.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(Status) { Content = new StringContent(ResponseBody) };
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public System.Collections.Concurrent.ConcurrentBag<string> Messages { get; } = [];
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);
        public void Dispose() { }
        private sealed class CapturingLogger(System.Collections.Concurrent.ConcurrentBag<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => messages.Add(formatter(state, exception));
        }
    }
}
