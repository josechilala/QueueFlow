using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
using QueueFlow.Api.Controllers;
using QueueFlow.Api.Middleware;
using QueueFlow.IntegrationTests.Health;

namespace QueueFlow.IntegrationTests.Security;

public sealed class LoginIdentityRateLimitTests
{
    [Theory]
    [InlineData("/api/v1/auth/login")]
    [InlineData("/api/v1/platform/auth/login")]
    public async Task SharedBffIpHasSeparateEmailQuotasAndNormalizedEmailsCannotBypass(string path)
    {
        await using var root = new QueueFlowApiFactory();
        await using var factory = CreateFactory(root);
        // Missing password exercises the real login endpoint without a database dependency.
        for (var attempt = 0; attempt < 10; attempt++)
            Assert.Equal(400, await Send(factory, path, "{\"email\":\"owner@example.test\"}"));
        Assert.Equal(429, await Send(factory, path, "{\"EMAIL\":\"  OWNER@EXAMPLE.TEST  \"}"));
        Assert.Equal(429, await Send(factory, path, "{\"email\":\"different@example.test\",\"Email\":\"owner@example.test\"}"));
        Assert.Equal(429, await Send(factory, path, "{\"email\":\"owner@example.test\"}", encoding: Encoding.Unicode));
        Assert.Equal(400, await Send(factory, path, "{\"email\":\"other@example.test\"}"));
        Assert.Equal(400, await Send(factory, path, "{\"email\":\"owner@example.test\"}", peer: "198.51.100.2"));
        Assert.Equal(429, await Send(factory, path, "{\"email\":\"owner@example.test\"}", forwarded: "192.0.2.250"));
    }

    [Fact]
    public async Task InvalidOrMissingEmailRetainsIpQuotaButRefreshHasSeparateQuota()
    {
        await using var root = new QueueFlowApiFactory();
        await using var factory = CreateFactory(root);
        var bodies = new[] { "{}", "{\"email\":null}", "{\"email\":\" \"}", "{\"email\":123}", "{" };
        for (var attempt = 0; attempt < 10; attempt++)
            Assert.Equal(400, await Send(factory, "/api/v1/auth/login", bodies[attempt % bodies.Length]));
        Assert.Equal(429, await Send(factory, "/api/v1/auth/login", "{}"));
        for (var attempt = 0; attempt < 30; attempt++)
            Assert.Equal(400, await Send(factory, "/api/v1/auth/refresh", "{}"));
        Assert.Equal(429, await Send(factory, "/api/v1/auth/refresh", "{}"));
        Assert.Equal(400, await Send(factory, "/api/v1/auth/login", "{\"email\":\"other@example.test\"}"));
    }

    [Fact]
    public async Task RefreshPartitionIsPerTokenWithoutLoggingSecretsOrTrustingForwardedIp()
    {
        async Task<string> Key(string token)
        {
            var body = JsonSerializer.Serialize(new { refreshToken = token });
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.1");
            context.Request.Method = "POST";
            context.Request.ContentType = "application/json";
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
            context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new ControllerActionDescriptor
            {
                ActionName = "Refresh", ControllerTypeInfo = typeof(AuthController).GetTypeInfo(),
            }), "refresh"));
            await new LoginRateLimitKeyMiddleware(async current =>
            {
                using var reader = new StreamReader(current.Request.Body);
                Assert.Equal(body, await reader.ReadToEndAsync());
            }).InvokeAsync(context);
            var key = LoginRateLimitKeyMiddleware.RefreshPartitionKey(context);
            Assert.StartsWith("refresh:", key);
            Assert.DoesNotContain(token, key);
            return key;
        }
        Assert.Equal(await Key("secret-A"), await Key("secret-A"));
        Assert.NotEqual(await Key("secret-A"), await Key("secret-a"));
        Assert.NotEqual(await Key("secret-A"), await Key("secret-B"));
    }

    [Fact]
    public async Task GlobalIpLimitStillCapsRotatingEmails()
    {
        await using var root = new QueueFlowApiFactory();
        await using var factory = CreateFactory(root, globalLimit: 12);
        for (var attempt = 0; attempt < 12; attempt++)
            Assert.Equal(400, await Send(factory, "/api/v1/auth/login", JsonSerializer.Serialize(new { email = $"user{attempt}@example.test" })));
        Assert.Equal(429, await Send(factory, "/api/v1/auth/login", "{\"email\":\"another@example.test\"}"));
    }

    [Fact]
    public async Task OversizedLoginBodyIsRejectedInsteadOfBypassingEmailQuota()
    {
        await using var root = new QueueFlowApiFactory();
        await using var factory = CreateFactory(root);
        Assert.Equal(413, await Send(factory, "/api/v1/auth/login", JsonSerializer.Serialize(new { email = new string(' ', 17000) + "owner@example.test" })));
    }

    [Fact]
    public async Task MiddlewarePreservesBodyAndDoesNotStoreRawEmailInPartitionKey()
    {
        var body = "{\"email\":\" Owner@Example.test \",\"password\":\"unchanged-secret\"}";
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new ControllerActionDescriptor
        {
            ActionName = "Login", ControllerTypeInfo = typeof(AuthController).GetTypeInfo(),
        }), "login"));
        var invoked = false;
        await new LoginRateLimitKeyMiddleware(async current =>
        {
            invoked = true;
            using var reader = new StreamReader(current.Request.Body);
            Assert.Equal(body, await reader.ReadToEndAsync());
            var key = LoginRateLimitKeyMiddleware.PartitionKey(current);
            Assert.StartsWith("login:unknown:", key);
            Assert.DoesNotContain("example", key, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("unchanged-secret", key);
        }).InvokeAsync(context);
        Assert.True(invoked);
    }

    [Fact]
    public async Task SignedRefreshUsersDoNotShareBffGlobalQuotaAndRotationDoesNotResetQuota()
    {
        await using var root = new QueueFlowApiFactory();
        await using var factory = CreateFactory(root, globalLimit: 2).WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.Configure<MvcOptions>(options => options.Filters.Add(new StopBeforeDatabase()))));
        var tokens = factory.Services.GetRequiredService<ITokenService>();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            Assert.Equal(204, await Send(factory, "/api/v1/auth/refresh", JsonSerializer.Serialize(new { refreshToken = tokens.CreateRefreshToken(first, IdentityType.Tenant) })));
            Assert.Equal(204, await Send(factory, "/api/v1/auth/refresh", JsonSerializer.Serialize(new { refreshToken = tokens.CreateRefreshToken(second, IdentityType.Tenant) })));
        }
        Assert.Equal(429, await Send(factory, "/api/v1/auth/refresh", JsonSerializer.Serialize(new { refreshToken = tokens.CreateRefreshToken(first, IdentityType.Tenant) }), expectedPolicy: "global"));
    }

    [Fact]
    public async Task ForgedRefreshIdentitiesStillShareGlobalIpQuota()
    {
        await using var root = new QueueFlowApiFactory();
        await using var factory = CreateFactory(root, globalLimit: 2).WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.Configure<MvcOptions>(options => options.Filters.Add(new StopBeforeDatabase()))));
        var tokens = factory.Services.GetRequiredService<ITokenService>();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var forged = tokens.CreateRefreshToken(Guid.NewGuid(), IdentityType.Tenant) + "tampered";
            Assert.Equal(attempt < 2 ? 204 : 429, await Send(factory, "/api/v1/auth/refresh", JsonSerializer.Serialize(new { refreshToken = forged })));
        }
    }

    [Fact]
    public async Task EndpointRefreshBudgetSurvivesTokenRotation()
    {
        await using var root = new QueueFlowApiFactory();
        await using var factory = CreateFactory(root).WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.Configure<MvcOptions>(options => options.Filters.Add(new StopBeforeDatabase()))));
        var tokens = factory.Services.GetRequiredService<ITokenService>();
        var user = Guid.NewGuid();
        for (var attempt = 0; attempt < 31; attempt++)
            Assert.Equal(attempt < 30 ? 204 : 429, await Send(factory, "/api/v1/auth/refresh", JsonSerializer.Serialize(new { refreshToken = tokens.CreateRefreshToken(user, IdentityType.Tenant) }), expectedPolicy: "refresh"));
    }

    [Fact]
    public async Task SignedBudgetIsPurposeBoundAndDoesNotAuthorizeApiAccess()
    {
        await using var root = new QueueFlowApiFactory();
        await using var factory = CreateFactory(root);
        var tokens = factory.Services.GetRequiredService<ITokenService>();
        var user = Guid.NewGuid();
        var raw = tokens.CreateRefreshToken(user, IdentityType.Tenant);
        Assert.Equal(user, tokens.GetRefreshRateLimitIdentity(raw, IdentityType.Tenant));
        Assert.Null(tokens.GetRefreshRateLimitIdentity(raw, IdentityType.Platform));
        Assert.Null(tokens.GetRefreshRateLimitIdentity(raw + "x", IdentityType.Tenant));
        Assert.Null(tokens.GetRefreshRateLimitIdentity(tokens.CreateRefreshToken(), IdentityType.Tenant));
        var parts = raw.Split('.');
        parts[2] = Guid.NewGuid().ToString("N");
        Assert.Null(tokens.GetRefreshRateLimitIdentity(string.Join('.', parts), IdentityType.Tenant));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", raw);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed class StopBeforeDatabase : IActionFilter
    {
        public void OnActionExecuting(ActionExecutingContext context) => context.Result = new NoContentResult();
        public void OnActionExecuted(ActionExecutedContext context) { }
    }

    private static WebApplicationFactory<Program> CreateFactory(QueueFlowApiFactory root, int globalLimit = 300) =>
        root.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("RateLimiting:AuthPermitLimit", "10");
            builder.UseSetting("RateLimiting:GlobalPermitLimit", globalLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        });

    private static async Task<int> Send(WebApplicationFactory<Program> factory, string path, string body,
        string peer = "198.51.100.1", string forwarded = "", Encoding? encoding = null, string? expectedPolicy = null)
    {
        encoding ??= Encoding.UTF8;
        var result = await factory.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
            context.Request.Scheme = "https";
            context.Request.Method = "POST";
            context.Request.Path = path;
            context.Request.Host = new HostString("localhost");
            context.Request.Headers["X-Forwarded-For"] = forwarded;
            context.Request.ContentType = $"application/json; charset={encoding.WebName}";
            var bytes = encoding.GetBytes(body);
            context.Request.Body = new MemoryStream(bytes);
            context.Request.ContentLength = bytes.Length;
        }, TestContext.Current.CancellationToken);
        if (result.Response.StatusCode == 429)
        {
            if (expectedPolicy is not null) Assert.Equal(expectedPolicy, result.Response.Headers["X-RateLimit-Policy"].ToString());
            Assert.True(result.Response.Headers["X-RateLimit-Policy"].ToString() is "global" or "auth" or "refresh");
            Assert.True(int.TryParse(result.Response.Headers.RetryAfter, out var seconds) && seconds > 0);
        }
        return result.Response.StatusCode;
    }
}
