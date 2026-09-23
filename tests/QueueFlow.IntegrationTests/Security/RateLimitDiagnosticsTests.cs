using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QueueFlow.Api.Middleware;

namespace QueueFlow.IntegrationTests.Security;

public sealed class RateLimitDiagnosticsTests
{
    [Fact]
    public void LogsSafeBucketEndpointAndForwardingWithoutCredentialsOrRawHeaders()
    {
        using var capture = new CaptureLogger();
        using var services = new ServiceCollection().AddLogging(builder => builder.AddProvider(capture)).BuildServiceProvider();
        string Reject(string key)
        {
            var context = new DefaultHttpContext { RequestServices = services };
            context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.2");
            context.Request.Headers["X-Forwarded-For"] = "secret-injected-text, 192.0.2.1, 10.0.0.3";
            context.Request.Headers.Authorization = "Bearer secret-jwt";
            context.Request.Headers.Cookie = "secret-cookie";
            context.Request.Path = "/api/v1/test/secret-token";
            context.Request.QueryString = new QueryString("?password=secret-password");
            context.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask,
                RoutePatternFactory.Parse("/api/v1/test/{token}"), 0, EndpointMetadataCollection.Empty, "test"));
            RateLimitDiagnostics.CaptureForwarding(context);
            context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.1");
            context.Request.Headers.Remove("X-Forwarded-For");
            context.Response.Headers.RetryAfter = "30";
            RateLimitDiagnostics.Partition(context, "auth", key);
            RateLimitDiagnostics.Rejected(context);
            Assert.Equal("auth", context.Response.Headers["X-RateLimit-Policy"].ToString());
            return capture.Message;
        }
        var first = Reject("login:192.0.2.1:secret-email-hash");
        Assert.Contains("Policy: auth", first, StringComparison.Ordinal);
        Assert.Contains("Endpoint: /api/v1/test/{token}", first, StringComparison.Ordinal);
        Assert.Contains("RetryAfter: 30", first, StringComparison.Ordinal);
        Assert.Contains("ResolvedIp: 192.0.2.1", first, StringComparison.Ordinal);
        Assert.Contains("PeerIp: 10.0.0.2", first, StringComparison.Ordinal);
        Assert.Contains("ForwardedFor: invalid,192.0.2.1,10.0.0.3", first, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", first, StringComparison.Ordinal);
        static string Partition(string message) => message.Split("Partition: ")[1].Split(';')[0];
        Assert.Equal(Partition(first), Partition(Reject("login:192.0.2.1:secret-email-hash")));
        Assert.NotEqual(Partition(first), Partition(Reject("login:192.0.2.1:other-email-hash")));
    }

    private sealed class CaptureLogger : ILoggerProvider, ILogger
    {
        public string Message { get; private set; } = "";
        public ILogger CreateLogger(string categoryName) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Message = formatter(state, exception);
        public void Dispose() { }
    }
}
