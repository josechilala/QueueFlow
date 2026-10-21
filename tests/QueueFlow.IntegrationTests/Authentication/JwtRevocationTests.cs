using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Features.Auth;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;
using QueueFlow.Infrastructure.Persistence;

namespace QueueFlow.IntegrationTests.Authentication;

public sealed partial class JwtRevocationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("disabled")]
    [InlineData("demoted")]
    [InlineData("password")]
    [InlineData("reactivated")]
    [InlineData("role_restored")]
    public async Task CommittedUserChangeRejectsOldJwtWithoutWritesAndRevokesRefresh(string change)
    {
        await using var test = await LocalTest.CreateAsync();
        using var actor = test.Client(test.Target);
        using var owner = test.Client(test.Owner);
        using (var before = await actor.GetAsync("/api/v1/users", Ct)) Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        var pair = await test.LoginAsync();
        actor.DefaultRequestHeaders.Authorization = new("Bearer", pair.AccessToken);
        if (change is "disabled" or "reactivated")
        {
            using var disabled = await owner.PatchAsJsonAsync($"/api/v1/users/{test.Target.Id}/status", new { isActive = false }, Ct);
            Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
            if (change == "reactivated")
            {
                using var enabled = await owner.PatchAsJsonAsync($"/api/v1/users/{test.Target.Id}/status", new { isActive = true }, Ct);
                Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
            }
        }
        else if (change is "demoted" or "role_restored")
        {
            using var demoted = await owner.PutAsJsonAsync($"/api/v1/users/{test.Target.Id}", new { name = "Target", role = "Attendant", branchIds = new[] { test.Branch.Id } }, Ct);
            Assert.Equal(HttpStatusCode.OK, demoted.StatusCode);
            if (change == "role_restored")
            {
                using var restored = await owner.PutAsJsonAsync($"/api/v1/users/{test.Target.Id}", new { name = "Target", role = "Admin", branchIds = new[] { test.Branch.Id } }, Ct);
                Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
            }
        }
        else
        {
            using var reset = await owner.PostAsJsonAsync($"/api/v1/users/{test.Target.Id}/reset-password", new { password = "Replacement-password-123!" }, Ct);
            Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        }
        var snapshot = await test.SnapshotAsync();
        for (var i = 0; i < 2; i++)
        {
            using var denied = await actor.GetAsync("/api/v1/users", Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        }
        Assert.Equal(snapshot, await test.SnapshotAsync());
        using var anonymous = test.Factory.CreateClient();
        using var refresh = await anonymous.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = pair.RefreshToken }, Ct);
        Assert.NotEqual(HttpStatusCode.OK, refresh.StatusCode);
        if (change != "disabled")
        {
            var renewed = await test.LoginAsync(change == "password" ? "Replacement-password-123!" : LocalTest.Password);
            actor.DefaultRequestHeaders.Authorization = new("Bearer", renewed.AccessToken);
            using var me = await actor.GetAsync("/api/v1/auth/me", Ct);
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
            using var users = await actor.GetAsync("/api/v1/users", Ct);
            Assert.Equal(change == "demoted" ? HttpStatusCode.Forbidden : HttpStatusCode.OK, users.StatusCode);
        }
    }

    [Fact]
    public async Task ValidJwtAndRotatedRefreshWorkWithoutCreatingSessionsPerGet()
    {
        await using var test = await LocalTest.CreateAsync();
        var pair = await test.LoginAsync();
        using var client = test.Client(test.Target);
        client.DefaultRequestHeaders.Authorization = new("Bearer", pair.AccessToken);
        var snapshot = await test.SnapshotAsync();
        for (var i = 0; i < 3; i++)
        {
            using var response = await client.GetAsync("/api/v1/users", Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        Assert.Equal(snapshot, await test.SnapshotAsync());
        using var refresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = pair.RefreshToken }, Ct);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var rotated = (await refresh.Content.ReadFromJsonAsync<TokenPair>(Ct))!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", rotated.AccessToken);
        using var accepted = await client.GetAsync("/api/v1/users", Ct);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        using var replay = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = pair.RefreshToken }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
    }

    [Theory]
    [InlineData("foreign_tenant")]
    [InlineData("missing_user")]
    [InlineData("wrong_role")]
    [InlineData("missing_version")]
    [InlineData("old_snapshot")]
    public async Task InvalidSignedIdentityCannotReadOrMutateAnotherTenant(string scenario)
    {
        await using var test = await LocalTest.CreateAsync();
        using var client = test.Factory.CreateClient();
        var tokens = test.Factory.Services.GetRequiredService<ITokenService>();
        var token = tokens.CreateAccessToken(scenario == "missing_user" ? Guid.NewGuid() : test.Target.Id,
            scenario == "foreign_tenant" ? Guid.NewGuid() : test.Owner.OrganizationId,
            scenario == "wrong_role" ? UserRole.Owner : test.Target.Role, test.Target.Email,
            scenario == "missing_version" ? null : scenario == "old_snapshot" ? test.Target.CreatedAt.AddSeconds(-1) : test.Target.CreatedAt);
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var snapshot = await test.SnapshotAsync();
        using var get = await client.GetAsync("/api/v1/users", Ct);
        using var mutation = await client.PatchAsJsonAsync($"/api/v1/users/{test.Owner.Id}/status", new { isActive = false }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, mutation.StatusCode);
        Assert.Equal(snapshot, await test.SnapshotAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SecurityDependencyFailureFailsClosed(bool databaseUnavailable)
    {
        await using var test = await LocalTest.CreateAsync();
        await using var failing = test.Factory.WithWebHostBuilder(builder =>
        {
            if (databaseUnavailable)
                builder.UseSetting("ConnectionStrings:QueueFlowDatabase", "Host=127.0.0.1;Port=1;Database=queueflow_q04_test;Username=queueflow_q04_test;Password=unused;Timeout=1;Pooling=false");
            else builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAccessTokenValidator>();
                services.AddScoped<IAccessTokenValidator, UnavailableValidator>();
            });
        });
        using var client = failing.CreateClient();
        using var original = test.Client(test.Target);
        client.DefaultRequestHeaders.Authorization = original.DefaultRequestHeaders.Authorization;
        using var response = await client.GetAsync("/api/v1/users", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("sensitive-provider-error", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlatformIdentityIsValidatedSeparatelyAndCannotAccessTenantOperations()
    {
        await using var test = await LocalTest.CreateAsync();
        var platform = new PlatformUser(Guid.NewGuid(), "Platform", "platform@example.test", "unused", DateTimeOffset.UtcNow);
        await using (var db = test.Context()) { db.Add(platform); await db.SaveChangesAsync(Ct); }
        using var client = test.Factory.CreateClient();
        var token = test.Factory.Services.GetRequiredService<ITokenService>().CreatePlatformAccessToken(platform.Id, platform.Email, platform.CreatedAt);
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        using var status = await client.GetAsync("/api/v1/platform/billing/inbox/worker-status", Ct);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        using var tenant = await client.GetAsync("/api/v1/users", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, tenant.StatusCode);
        await using (var db = test.Context())
        {
            var user = await db.PlatformUsers.SingleAsync(Ct);
            user.SetActive(false, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(Ct);
        }
        using var denied = await client.GetAsync("/api/v1/platform/billing/inbox/worker-status", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    private sealed class UnavailableValidator : IAccessTokenValidator
    {
        public Task<bool> ValidateAsync(AccessTokenIdentity identity, CancellationToken cancellationToken) => throw new InvalidOperationException("sensitive-provider-error");
    }

    private sealed class LocalTest(string connection, string schema) : IAsyncDisposable
    {
        public const string Password = "Local-test-password-123!";
        public WebApplicationFactory<Program> Factory { get; private set; } = null!;
        public AppUser Owner { get; private set; } = null!;
        public AppUser Target { get; private set; } = null!;
        public Branch Branch { get; private set; } = null!;
        public ApplicationDbContext Context(ICurrentUser? identity = null, params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors) =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).AddInterceptors(interceptors).Options, identity ?? new SetupIdentity());

        public static async Task<LocalTest> CreateAsync()
        {
            var configured = Environment.GetEnvironmentVariable("QUEUEFLOW_Q04_TEST_DATABASE");
            Assert.False(string.IsNullOrWhiteSpace(configured), "An explicit disposable local Q04 database is required.");
            var settings = new NpgsqlConnectionStringBuilder(configured);
            Assert.Equal("127.0.0.1", settings.Host);
            Assert.Equal("queueflow_q04_test", settings.Database);
            Assert.Equal("queueflow_q04_test", settings.Username);
            var schema = $"q04_{Guid.NewGuid():N}";
            settings.SearchPath = schema;
            settings.Pooling = false;
            var test = new LocalTest(settings.ConnectionString, schema);
            try
            {
                await using var setup = new NpgsqlConnection(settings.ConnectionString);
                await setup.OpenAsync(Ct);
                await using (var check = new NpgsqlCommand("SELECT current_database()", setup))
                    Assert.Equal("queueflow_q04_test", await check.ExecuteScalarAsync(Ct));
                await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", setup)) await create.ExecuteNonQueryAsync(Ct);
                await using (var normalize = new NpgsqlCommand("CREATE FUNCTION queueflow_normalize_email(text) RETURNS text LANGUAGE sql IMMUTABLE AS 'SELECT lower(btrim($1))'", setup)) await normalize.ExecuteNonQueryAsync(Ct);
                await using var db = test.Context();
                await using (var create = new NpgsqlCommand(db.Database.GenerateCreateScript(), setup)) await create.ExecuteNonQueryAsync(Ct);
                test.Factory = new LocalFactory(settings.ConnectionString);
                var now = DateTimeOffset.UtcNow;
                var org = new Organization(Guid.NewGuid(), "Local Q04", $"q04-{Guid.NewGuid():N}", "UTC", now);
                var hash = test.Factory.Services.GetRequiredService<IPasswordService>().Hash(Password);
                test.Owner = new AppUser(Guid.NewGuid(), org.Id, "Owner", "owner@example.test", hash, UserRole.Owner, now);
                test.Target = new AppUser(Guid.NewGuid(), org.Id, "Target", "target@example.test", hash, UserRole.Admin, now);
                test.Branch = new Branch(Guid.NewGuid(), org.Id, "Main", "UTC", now);
                db.AddRange(org, test.Owner, test.Target, test.Branch);
                await db.SaveChangesAsync(Ct);
                return test;
            }
            catch { await test.DisposeAsync(); throw; }
        }

        public HttpClient Client(AppUser user)
        {
            var client = Factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                Factory.Services.GetRequiredService<ITokenService>().CreateAccessToken(user.Id, user.OrganizationId, user.Role, user.Email, user.UpdatedAt ?? user.CreatedAt));
            return client;
        }

        public async Task<TokenPair> LoginAsync(string password = Password)
        {
            using var client = Factory.CreateClient();
            using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = Target.Email, password }, Ct);
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            return (await login.Content.ReadFromJsonAsync<TokenPair>(Ct))!;
        }

        public async Task<(int Refresh, int Audits, int Tickets)> SnapshotAsync()
        {
            await using var db = Context();
            return (await db.RefreshTokens.IgnoreQueryFilters().CountAsync(Ct), await db.AuditLogs.IgnoreQueryFilters().CountAsync(Ct), await db.QueueTickets.IgnoreQueryFilters().CountAsync(Ct));
        }

        public async ValueTask DisposeAsync()
        {
            if (Factory is not null) await Factory.DisposeAsync();
            await using var cleanup = new NpgsqlConnection(connection);
            await cleanup.OpenAsync(CancellationToken.None);
            await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", cleanup);
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private sealed class SetupIdentity : ICurrentUser
    {
        public Guid? UserId => null;
        public Guid? OrganizationId => null;
        public UserRole? Role => null;
        public bool IsAuthenticated => false;
    }

    private sealed class LocalFactory(string connection) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Test");
            builder.UseSetting("ConnectionStrings:QueueFlowDatabase", connection);
            builder.UseSetting("Authentication:JwtKey", "q04-local-key-with-at-least-thirty-two-characters");
            builder.UseSetting("Authentication:Issuer", "q04-tests");
            builder.UseSetting("Authentication:Audience", "q04-tests");
            builder.UseSetting("Asaas:Environment", "");
            builder.UseSetting("Asaas:CheckoutEnabled", "false");
            builder.UseSetting("OTEL_EXPORTER_OTLP_ENDPOINT", "");
            builder.UseSetting("Redis:UseBackplane", "false");
            builder.UseSetting("RateLimiting:UseRedis", "false");
            for (var i = 0; i < 4; i++) builder.UseSetting($"Cors:Origins:{i}", "https://q04.example.test");
            builder.ConfigureServices(services => services.RemoveAll<IHostedService>());
        }
    }
}
