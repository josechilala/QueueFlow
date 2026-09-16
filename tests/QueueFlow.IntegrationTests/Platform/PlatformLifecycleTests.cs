using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Abstractions.Notifications;
using QueueFlow.Application.Features.Platform;
using QueueFlow.Domain.Entities;
using QueueFlow.Infrastructure.Persistence;
using Serilog.Core;
using Serilog.Events;

namespace QueueFlow.IntegrationTests.Platform;

public sealed class PlatformLifecycleTests(PlatformTestFactory factory) : IClassFixture<PlatformTestFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task BootstrapIsConcurrentSafeAndPlatformLoginDoesNotCreateTenantSession()
    {
        await using var database = await PlatformMigrationTests.CreateDatabaseAsync();
        await database.Database.MigrateAsync(Ct);
        using var isolated = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:QueueFlowDatabase", database.Database.GetConnectionString()));
        var emails = new[] { "first@example.test", "second@example.test" };
        var attempts = await Task.WhenAll(emails.Select(async email => {
            await using var scope = isolated.Services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<PlatformBootstrapService>().ProvisionFirstAdminAsync("Test platform", email, "Test-platform-password-123!", Ct);
            return (Email: email, result.IsSuccess);
        }));
        var winner = Assert.Single(attempts, attempt => attempt.IsSuccess);
        await using (var scope = isolated.Services.CreateAsyncScope())
        {
            Assert.True((await scope.ServiceProvider.GetRequiredService<PlatformBootstrapService>().ProvisionFirstAdminAsync("Same admin", winner.Email, "Test-platform-password-123!", Ct)).IsSuccess);
        }
        Assert.Equal(1, await database.PlatformUsers.CountAsync(Ct));
        Assert.Equal(0, await database.Users.IgnoreQueryFilters().CountAsync(Ct));
        Assert.Equal(0, await database.Organizations.CountAsync(Ct));
        using var client = isolated.CreateClient();
        using var login = await client.PostAsJsonAsync("/api/v1/platform/auth/login", new { email = winner.Email, password = "Test-platform-password-123!" }, Ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var pair = (await login.Content.ReadFromJsonAsync<QueueFlow.Application.Features.Auth.TokenPair>(Ct))!;
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(pair.AccessToken);
        Assert.DoesNotContain(jwt.Claims, claim => claim.Type == "organization_id");
        client.DefaultRequestHeaders.Authorization = new("Bearer", pair.AccessToken);
        using var me = await client.GetAsync("/api/v1/platform/auth/me", Ct);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        using var tenantLogin = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = winner.Email, password = "Test-platform-password-123!" }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, tenantLogin.StatusCode);
        var refreshes = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.PostAsJsonAsync("/api/v1/platform/auth/refresh", new { refreshToken = pair.RefreshToken }, Ct)));
        try { Assert.Single(refreshes, response => response.StatusCode == HttpStatusCode.OK); Assert.Single(refreshes, response => response.StatusCode == HttpStatusCode.Unauthorized); }
        finally { foreach (var response in refreshes) response.Dispose(); }
    }

    [Fact]
    public async Task ProductionSendsCodeOnlyThroughProviderAndNeverLogsOrReturnsIt()
    {
        await using var database = await PlatformMigrationTests.CreateDatabaseAsync(); await database.Database.MigrateAsync(Ct);
        var sender = new CapturingSender(); var logs = new CapturingSink();
        using var production = factory.WithWebHostBuilder(builder => {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:QueueFlowDatabase", database.Database.GetConnectionString());
            builder.UseSetting("Onboarding:ExposeVerificationCodeForDevelopment", "true");
            builder.ConfigureServices(services => { services.AddSingleton<IActivationEmailSender>(sender); services.AddSingleton<ILogEventSink>(logs); });
        });
        var rawToken = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var hash = production.Services.GetRequiredService<ITokenService>().HashToken(rawToken);
        var invitation = new OrganizationInvitation(Guid.NewGuid(), "production-test@example.test", null, "Test", "Trial", hash, DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow);
        database.OrganizationInvitations.Add(invitation);
        await database.SaveChangesAsync(Ct);
        using var client = production.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", production.Services.GetRequiredService<ITokenService>().CreatePlatformAccessToken(Guid.NewGuid(), "test-admin@example.test"));
        using var sent = await client.PostAsJsonAsync($"/api/v1/platform/invitations/{invitation.Id}/send", new { token = rawToken }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, sent.StatusCode);
        Assert.Equal($"https://customer.example.test/ativar#{rawToken}", sender.ActivationUrl);
        client.DefaultRequestHeaders.Authorization = null;
        using var response = await client.PostAsync($"/api/v1/public/activation/{rawToken}/verification-code", null, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Matches("^[0-9]{6}$", sender.Code!);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain(sender.Code!, body, StringComparison.Ordinal);
        Assert.Null(JsonDocument.Parse(body).RootElement.GetProperty("developmentCode").GetString());
        Assert.NotEmpty(logs.Events);
        Assert.DoesNotContain(logs.Events, entry => entry.Contains(rawToken, StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Events, entry => entry.Contains(sender.Code!, StringComparison.Ordinal));
        using var verified = await client.PostAsJsonAsync("/api/v1/public/activation/verify", new { invitationToken = rawToken, code = sender.Code }, Ct);
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        var grant = (await verified.Content.ReadFromJsonAsync<ActivationAuthorizationDto>(Ct))!;
        const string password = "Production-synthetic-password-123!";
        using var completed = await client.PostAsJsonAsync("/api/v1/public/activation/complete", new { invitationToken = rawToken, activation = new { activationAuthorization = grant.ActivationAuthorization, organizationName = "Prod test", slug = $"prod-{Guid.NewGuid():N}", responsibleName = "Owner", password, timeZone = "UTC" } }, Ct);
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        foreach (var secret in new[] { rawToken, sender.Code!, grant.ActivationAuthorization, password })
            Assert.DoesNotContain(logs.Events, entry => entry.Contains(secret, StringComparison.Ordinal));
        var stored = await database.OrganizationInvitations.AsNoTracking().SingleAsync(Ct);
        Assert.Null(stored.VerificationCodeHash);
        Assert.NotNull(stored.ActivationAuthorizationConsumedAt);
    }

    [Fact]
    public async Task DatabaseFailureRollsBackOrganizationOwnerSubscriptionAndInvitation()
    {
        await using var database = await PlatformMigrationTests.CreateDatabaseAsync(); await database.Database.MigrateAsync(Ct);
        using var isolated = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:QueueFlowDatabase", database.Database.GetConnectionString()));
        var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;
        var invitation = new OrganizationInvitation(Guid.NewGuid(), "rollback@example.test", null, "Rollback", "Trial", isolated.Services.GetRequiredService<ITokenService>().HashToken(token), now.AddHours(1), now);
        invitation.SetVerificationCode("test-hash", now.AddMinutes(10), now); invitation.AuthorizeActivation(isolated.Services.GetRequiredService<ITokenService>().HashToken(token), now.AddMinutes(10), now);
        database.OrganizationInvitations.Add(invitation); await database.SaveChangesAsync(Ct);
        await database.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION reject_test_activation() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Injected activation failure'; END; $$;
            CREATE TRIGGER reject_test_activation BEFORE UPDATE ON "OrganizationInvitations"
            FOR EACH ROW WHEN (NEW."UsedAt" IS NOT NULL) EXECUTE FUNCTION reject_test_activation();
            """, Ct);
        using var client = isolated.CreateClient();
        using var response = await client.PostAsJsonAsync($"/api/v1/public/activation/{token}/complete", new { activationAuthorization = token, organizationName = "Rollback", slug = "rollback-test", responsibleName = "Owner", password = "Test-password-123!", timeZone = "UTC" }, Ct);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(0, await database.Organizations.CountAsync(Ct));
        Assert.Equal(0, await database.Users.IgnoreQueryFilters().CountAsync(Ct));
        Assert.Equal(0, await database.Subscriptions.IgnoreQueryFilters().CountAsync(Ct));
        Assert.Null((await database.OrganizationInvitations.AsNoTracking().SingleAsync(Ct)).UsedAt);
    }

    private sealed class CapturingSender : IActivationEmailSender
    {
        public bool IsConfigured => true;
        public bool SupportsInvitationDelivery => true;
        public string? ActivationUrl { get; private set; }
        public string? Code { get; private set; }
        public Task SendVerificationCodeAsync(string email, string code, CancellationToken cancellationToken) { Code = code; return Task.CompletedTask; }
        public Task SendInvitationAsync(string email, string activationUrl, CancellationToken cancellationToken) { ActivationUrl = activationUrl; return Task.CompletedTask; }
    }
    private sealed class CapturingSink : ILogEventSink
    {
        public ConcurrentBag<string> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent.RenderMessage(System.Globalization.CultureInfo.InvariantCulture) + JsonSerializer.Serialize(logEvent.Properties) + logEvent.Exception?.ToString());
    }
}
