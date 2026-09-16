using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Features.Platform;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;
using QueueFlow.Infrastructure.Persistence;

namespace QueueFlow.IntegrationTests.Platform;

public sealed class PlatformSecurityTests : IClassFixture<PlatformTestFactory>
{
    private readonly PlatformTestFactory _factory;
    public PlatformSecurityTests(PlatformTestFactory factory) => _factory = factory;

    [Theory]
    [InlineData("TenantIdentity")]
    [InlineData("AdminPanel")]
    [InlineData("UserManagement")]
    [InlineData("AttendantPanel")]
    [InlineData("OrganizationManagement")]
    [InlineData("SubscriptionManagement")]
    [InlineData("ReportRead")]
    [InlineData("AuditRead")]
    public async Task TenantPoliciesRejectPlatformEvenWithTenantRoleAndOrganization(string policy)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var claims = new[] { new Claim("identity_type", "platform"), new Claim("organization_id", Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, "Owner") };
        Assert.False((await authorization.AuthorizeAsync(new ClaimsPrincipal(new ClaimsIdentity(claims, "test")), null, policy)).Succeeded);
        claims[0] = new Claim("identity_type", "tenant");
        Assert.True((await authorization.AuthorizeAsync(new ClaimsPrincipal(new ClaimsIdentity(claims, "test")), null, policy)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(new ClaimsPrincipal(new ClaimsIdentity(claims.Where(c => c.Type != "organization_id"), "test")), null, policy)).Succeeded);
        claims[1] = new Claim("organization_id", "forged-invalid-organization");
        Assert.False((await authorization.AuthorizeAsync(new ClaimsPrincipal(new ClaimsIdentity(claims, "test")), null, policy)).Succeeded);
    }

    [Theory]
    [InlineData(UserRole.Owner)]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Manager)]
    [InlineData(UserRole.Attendant)]
    [InlineData(UserRole.Viewer)]
    public async Task TenantJwtCannotAccessPlatform(UserRole role)
    {
        var token = _factory.Services.GetRequiredService<ITokenService>().CreateAccessToken(Guid.NewGuid(), Guid.NewGuid(), role, "tenant@example.test");
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.GetAsync("/api/v1/platform/dashboard", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/auth/me")]
    [InlineData("/api/v1/branches")]
    [InlineData("/api/v1/users")]
    [InlineData("/api/v1/reports")]
    [InlineData("/api/v1/audit")]
    public async Task PlatformJwtCannotAccessTenant(string path)
    {
        var token = _factory.Services.GetRequiredService<ITokenService>().CreatePlatformAccessToken(Guid.NewGuid(), "platform@example.test");
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ProductionDisablesRegisterAndCodeExposureEvenWhenFlagsAreEnabled()
    {
        using var factory = _factory.WithWebHostBuilder(builder => {
            builder.UseEnvironment("Production");
            builder.UseSetting("Onboarding:AllowPublicRegistration", "true");
            builder.UseSetting("Onboarding:ExposeVerificationCodeForDevelopment", "true");
        });
        using var client = factory.CreateClient();
        Assert.False(factory.Services.GetRequiredService<IOptions<OnboardingOptions>>().Value.ExposeVerificationCodeForDevelopment);
        using var response = await client.PostAsJsonAsync("/api/v1/auth/register", new { name = "Forbidden", slug = "forbidden", timeZone = "UTC", adminName = "Owner", adminEmail = "owner@example.test", password = "Test-password-123!" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(IdentityType.Platform, false)]
    [InlineData(IdentityType.Platform, true)]
    [InlineData(IdentityType.Tenant, false)]
    public async Task MissingTenantOrPlatformCannotWriteTenantEntities(IdentityType type, bool hasOrganization)
    {
        var identity = new TestIdentity(type, hasOrganization ? Guid.NewGuid() : null);
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=127.0.0.1;Port=1;Database=unused").Options, identity);
        db.Branches.Add(new Branch(Guid.NewGuid(), identity.OrganizationId ?? Guid.NewGuid(), "Test", "UTC", DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.Throws<UnauthorizedAccessException>(() => db.SaveChanges());
    }

    [Fact]
    public async Task TenantCannotForgeOrganizationInEntity()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=127.0.0.1;Port=1;Database=unused").Options, new TestIdentity(IdentityType.Tenant, Guid.NewGuid()));
        db.Branches.Add(new Branch(Guid.NewGuid(), Guid.NewGuid(), "Other tenant", "UTC", DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ChangingOrganizationClaimInvalidatesJwtSignature()
    {
        var token = _factory.Services.GetRequiredService<ITokenService>().CreateAccessToken(Guid.NewGuid(), Guid.NewGuid(), UserRole.Owner, "tenant@example.test");
        var parts = token.Split('.');
        var payload = System.Text.Json.Nodes.JsonNode.Parse(Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Decode(parts[1]))!;
        payload["organization_id"] = Guid.NewGuid().ToString();
        parts[1] = Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Encode(payload.ToJsonString());
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", string.Join('.', parts));
        using var response = await client.GetAsync("/api/v1/branches", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

public sealed class PlatformTestFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.UseSetting("Authentication:JwtKey", "platform-integration-test-key-at-least-thirty-two-characters");
        builder.UseSetting("Cors:Origins:0", "https://test.example");
        builder.UseSetting("ConnectionStrings:QueueFlowDatabase", Environment.GetEnvironmentVariable("QUEUEFLOW_PLATFORM_TEST_DATABASE") ?? "Host=127.0.0.1;Port=1;Database=unused");
        builder.UseSetting("Onboarding:PublicUrl", "https://customer.example.test");
        builder.UseSetting("Onboarding:VerificationCodePepper", "test-only-pepper-never-use-in-production");
        builder.UseSetting("Onboarding:ExposeVerificationCodeForDevelopment", "true");
        builder.UseSetting("RateLimiting:GlobalPermitLimit", "10000");
        builder.UseSetting("RateLimiting:PublicPermitLimit", "1000");
        builder.UseSetting("RateLimiting:AuthPermitLimit", "1000");
        builder.ConfigureServices(services => {
            foreach (var service in services.Where(service => service.ServiceType == typeof(IHostedService)).ToArray()) services.Remove(service);
        });
    }
}

internal sealed class TestIdentity(IdentityType? type, Guid? organizationId = null, Guid? platformId = null) : ICurrentUser
{
    public Guid? UserId => platformId;
    public Guid? PlatformUserId => type == QueueFlow.Domain.Enums.IdentityType.Platform ? platformId : null;
    public Guid? OrganizationId => organizationId;
    public UserRole? Role => type == QueueFlow.Domain.Enums.IdentityType.Tenant ? UserRole.Owner : null;
    public IdentityType? IdentityType => type;
    public bool IsAuthenticated => type is not null;
}
