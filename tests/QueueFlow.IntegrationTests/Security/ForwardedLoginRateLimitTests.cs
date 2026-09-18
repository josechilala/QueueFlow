using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using QueueFlow.Api.Middleware;
using QueueFlow.IntegrationTests.Health;

namespace QueueFlow.IntegrationTests.Security;

public sealed class ForwardedLoginRateLimitTests
{
    [Theory]
    [InlineData("10.0.0.2", "192.0.2.1", "192.0.2.2", 400)]
    [InlineData("198.51.100.1", "192.0.2.1", "192.0.2.2", 429)]
    [InlineData("10.0.0.2", "192.0.2.1, 198.51.100.1", "192.0.2.2, 198.51.100.1", 429)]
    [InlineData("10.0.0.2", "192.0.2.1, 10.0.0.3", "192.0.2.2, 10.0.0.3", 400)]
    [InlineData("::ffff:10.0.0.2", "2001:db8::1", "2001:db8::2", 400)]
    public async Task LoginLimitsUseOnlyTrustedForwardedClients(string peer, string first, string second, int secondStatus)
    {
        await using var root = new QueueFlowApiFactory();
        await using var factory = root.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ReverseProxy:KnownProxies:0", "10.0.0.2");
            builder.UseSetting("ReverseProxy:KnownProxies:1", "10.0.0.3");
            builder.UseSetting("ReverseProxy:ForwardLimit", "2");
            builder.UseSetting("RateLimiting:AuthPermitLimit", "10");
        });
        for (var attempt = 0; attempt < 10; attempt++)
            Assert.Equal(400, await Login(factory, peer, first));
        Assert.Equal(429, await Login(factory, peer, first));
        Assert.Equal(secondStatus, await Login(factory, peer, second));
    }

    [Fact]
    public void MissingConfigurationDoesNotTrustAllProxies()
    {
        var options = TrustedProxyOptions.Create(new ConfigurationBuilder().Build());
        Assert.NotEmpty(options.KnownProxies);
        Assert.DoesNotContain(IPAddress.Loopback, options.KnownProxies);
        Assert.Empty(options.KnownIPNetworks);
    }

    [Theory]
    [InlineData("0.0.0.0/0")]
    [InlineData("::/0")]
    public void UniversalProxyTrustIsRejected(string network)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ReverseProxy:KnownNetworks:0"] = network,
        }).Build();
        Assert.Throws<InvalidOperationException>(() => TrustedProxyOptions.Create(configuration));
    }

    private static async Task<int> Login(WebApplicationFactory<Program> factory, string peer, string forwarded)
    {
        var result = await factory.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
            context.Request.Scheme = "https";
            context.Request.Method = "POST";
            context.Request.Path = "/api/v1/auth/login";
            context.Request.Host = new Microsoft.AspNetCore.Http.HostString("localhost");
            context.Request.Headers["X-Forwarded-For"] = forwarded;
            context.Request.ContentType = "application/json";
            var body = Encoding.UTF8.GetBytes("{}");
            context.Request.Body = new MemoryStream(body);
            context.Request.ContentLength = body.Length;
        }, TestContext.Current.CancellationToken);
        return result.Response.StatusCode;
    }
}
