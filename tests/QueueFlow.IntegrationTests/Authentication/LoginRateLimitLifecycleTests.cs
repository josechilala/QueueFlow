using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Features.Auth;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;
using QueueFlow.Infrastructure.Persistence;
using QueueFlow.IntegrationTests.Platform;

namespace QueueFlow.IntegrationTests.Authentication;

public sealed class LoginRateLimitLifecycleTests(PlatformTestFactory factory) : IClassFixture<PlatformTestFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfulLoginResetsFailuresAndRefreshRotationRemainsSingleUse(bool platform)
    {
        var connection = Environment.GetEnvironmentVariable("QUEUEFLOW_PLATFORM_TEST_DATABASE");
        var redis = Environment.GetEnvironmentVariable("QUEUEFLOW_AUTH_TEST_REDIS");
        if (string.IsNullOrWhiteSpace(connection) || string.IsNullOrWhiteSpace(redis))
            Assert.Skip("Set disposable local PostgreSQL and Redis test connections.");
        var parsed = new NpgsqlConnectionStringBuilder(connection);
        Assert.Equal("127.0.0.1", parsed.Host);
        Assert.StartsWith("queueflow_platform_test", parsed.Database);
        Assert.StartsWith("127.0.0.1:", redis);
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).Options, new TestIdentity(null));
        // Use the already provisioned test schema; never create or apply migrations here.
        Assert.Empty(await db.Database.GetPendingMigrationsAsync(Ct));
        void Configure(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:QueueFlowDatabase", connection);
            builder.UseSetting("RateLimiting:UseRedis", "true");
            builder.UseSetting("Redis:ConnectionString", redis);
            builder.UseSetting("RateLimiting:AuthPermitLimit", "10");
            builder.UseSetting("RateLimiting:GlobalPermitLimit", "2");
        }
        using var first = factory.WithWebHostBuilder(Configure);
        using var second = factory.WithWebHostBuilder(Configure);
        const string password = "Correct-test-password-123!";
        var hash = first.Services.GetRequiredService<IPasswordService>().Hash(password);
        var now = DateTimeOffset.UtcNow;
        var userId = Guid.NewGuid();
        var email = $"login-{userId:N}@example.test";
        var org = new Organization(Guid.NewGuid(), "Login test", $"login-{userId:N}", "UTC", now);
        if (platform) db.PlatformUsers.Add(new PlatformUser(userId, "Admin", email, hash, now));
        else db.AddRange(org, new AppUser(userId, org.Id, "Owner", email, hash, UserRole.Owner, now));
        await db.SaveChangesAsync(Ct);
        using var client1 = first.CreateClient();
        using var client2 = second.CreateClient();
        var path = platform ? "/api/v1/platform/auth" : "/api/v1/auth";
        try
        {
            TokenPair? pair = null;
            for (var i = 0; i < 12; i++)
            {
                using var valid = await (i % 2 == 0 ? client1 : client2).PostAsJsonAsync(path + "/login", new { email, password }, Ct);
                Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
                pair = await valid.Content.ReadFromJsonAsync<TokenPair>(Ct);
            }
            // Neither the shared BFF IP nor successful authentication consumes the failure budget.
            for (var cycle = 0; cycle < 2; cycle++)
            {
                for (var i = 0; i < 9; i++)
                {
                    using var wrong = await client1.PostAsJsonAsync(path + "/login", new { email, password = "wrong-password" }, Ct);
                    Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
                }
                using var reset = await client2.PostAsJsonAsync(path + "/login", new { email, password }, Ct);
                Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
            }
            var refreshes = await Task.WhenAll(client1.PostAsJsonAsync(path + "/refresh", new { pair!.RefreshToken }, Ct),
                client2.PostAsJsonAsync(path + "/refresh", new { pair!.RefreshToken }, Ct));
            TokenPair winner;
            try
            {
                winner = (await Assert.Single(refreshes, x => x.StatusCode == HttpStatusCode.OK).Content.ReadFromJsonAsync<TokenPair>(Ct))!;
                var conflict = Assert.Single(refreshes, x => x.StatusCode == HttpStatusCode.Conflict);
                Assert.DoesNotContain("accessToken", await conflict.Content.ReadAsStringAsync(Ct));
            }
            finally { foreach (var response in refreshes) response.Dispose(); }
            using var next = await client2.PostAsJsonAsync(path + "/refresh", new { winner.RefreshToken }, Ct);
            Assert.Equal(HttpStatusCode.OK, next.StatusCode);
            var unknown = $"missing-{userId:N}@example.test";
            for (var i = 0; i < 10; i++)
            {
                using var wrong = await client1.PostAsJsonAsync(path + "/login", new { email, password = "wrong-password" }, Ct);
                using var missing = await client2.PostAsJsonAsync(path + "/login", new { email = unknown, password }, Ct);
                Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
                Assert.Equal(wrong.StatusCode, missing.StatusCode);
                var wrongBody = await wrong.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(Ct);
                var missingBody = await missing.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(Ct);
                Assert.Equal(wrongBody.GetProperty("detail").GetString(), missingBody.GetProperty("detail").GetString());
            }
            foreach (var identity in new[] { email, unknown })
            {
                using var blocked = await client2.PostAsJsonAsync(path + "/login", new { email = identity, password }, Ct);
                Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
                Assert.Equal("login", blocked.Headers.GetValues("X-RateLimit-Policy").Single());
                Assert.InRange(blocked.Headers.RetryAfter!.Delta!.Value.TotalSeconds, 1, 60);
            }
        }
        finally
        {
            if (platform)
            {
                await db.PlatformRefreshTokens.Where(x => x.PlatformUserId == userId).ExecuteDeleteAsync(CancellationToken.None);
                await db.PlatformUsers.Where(x => x.Id == userId).ExecuteDeleteAsync(CancellationToken.None);
            }
            else
            {
                await db.RefreshTokens.IgnoreQueryFilters().Where(x => x.UserId == userId).ExecuteDeleteAsync(CancellationToken.None);
                await db.Users.IgnoreQueryFilters().Where(x => x.Id == userId).ExecuteDeleteAsync(CancellationToken.None);
                await db.Organizations.Where(x => x.Id == org.Id).ExecuteDeleteAsync(CancellationToken.None);
            }
        }
    }
}
