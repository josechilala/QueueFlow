using System.Net;
using System.Net.Http.Json;
using QueueFlow.IntegrationTests.Fixtures;

namespace QueueFlow.IntegrationTests.Security;

public sealed class SecurityEndpointTests : IClassFixture<SecurityApiFactory>
{
    private readonly HttpClient _client;
    public SecurityEndpointTests(SecurityApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task ResponsesContainDefensiveHeaders()
    {
        using var response = await _client.GetAsync("/health/live", TestContext.Current.CancellationToken);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task UnknownCorsOriginIsNotAllowed()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/login");
        request.Headers.Add("Origin", "https://attacker.invalid"); request.Headers.Add("Access-Control-Request-Method", "POST");
        using var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task SignalRNegotiationAllowsBrowserHeaders()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/hubs/queue/negotiate?negotiateVersion=1");
        request.Headers.Add("Origin", "http://localhost:3001");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "x-requested-with,x-signalr-user-agent");
        using var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("http://localhost:3001", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        var allowed = string.Join(",", response.Headers.GetValues("Access-Control-Allow-Headers"));
        Assert.Contains("X-Requested-With", allowed, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("X-SignalR-User-Agent", allowed, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("true", response.Headers.GetValues("Access-Control-Allow-Credentials").Single());
    }

    [Fact]
    public async Task AuthenticationEndpointsAreRateLimitedPerClient()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email = "invalid@test.local", password = "invalid-password" }, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        using var limited = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email = "invalid@test.local", password = "invalid-password" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
        using var otherIdentity = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email = "other@test.local", password = "invalid-password" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, otherIdentity.StatusCode);
    }
}

public sealed class SecurityApiFactory : IsolatedEndpointApiFactory { }
