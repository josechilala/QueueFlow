using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Features.Auth;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;
using QueueFlow.IntegrationTests.Platform;

namespace QueueFlow.IntegrationTests.Authentication;

public sealed class RefreshSessionTests(PlatformTestFactory factory) : IClassFixture<PlatformTestFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentRotationAcrossInstancesHasOneWinnerAndNoReplay(bool platform)
    {
        await using var db = await PlatformMigrationTests.CreateDatabaseAsync();
        await db.Database.MigrateAsync(Ct);
        using var first = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:QueueFlowDatabase", db.Database.GetConnectionString()));
        using var second = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:QueueFlowDatabase", db.Database.GetConnectionString()));
        var tokens = first.Services.GetRequiredService<ITokenService>();
        var raw = tokens.CreateRefreshToken();
        var now = DateTimeOffset.UtcNow;
        var userId = Guid.NewGuid();
        if (platform)
        {
            db.PlatformUsers.Add(new PlatformUser(userId, "Admin", "refresh@example.test", "unused", now));
            db.PlatformRefreshTokens.Add(new PlatformRefreshToken(Guid.NewGuid(), userId, tokens.HashToken(raw), now.AddDays(30), now));
        }
        else
        {
            var organizationId = Guid.NewGuid();
            db.Users.Add(new AppUser(userId, organizationId, "Owner", "refresh@example.test", "unused", UserRole.Owner, now));
            db.RefreshTokens.Add(new RefreshToken(Guid.NewGuid(), organizationId, userId, tokens.HashToken(raw), now.AddDays(30), now));
        }
        await db.SaveChangesAsync(Ct);
        using var client1 = first.CreateClient();
        using var client2 = second.CreateClient();
        var path = platform ? "/api/v1/platform/auth/refresh" : "/api/v1/auth/refresh";
        var responses = await Task.WhenAll(client1.PostAsJsonAsync(path, new { refreshToken = raw }, Ct), client2.PostAsJsonAsync(path, new { refreshToken = raw }, Ct));
        TokenPair winner;
        try
        {
            winner = (await Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK).Content.ReadFromJsonAsync<TokenPair>(Ct))!;
            var conflict = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            Assert.DoesNotContain("accessToken", await conflict.Content.ReadAsStringAsync(Ct));
            Assert.NotEqual(raw, winner.RefreshToken);
        }
        finally { foreach (var response in responses) response.Dispose(); }
        var active = platform ? await db.PlatformRefreshTokens.CountAsync(x => x.RevokedAt == null, Ct)
            : await db.RefreshTokens.IgnoreQueryFilters().CountAsync(x => x.RevokedAt == null, Ct);
        Assert.Equal(1, active);
        using var replay = await client1.PostAsJsonAsync(path, new { refreshToken = raw }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);

        // A fresh API process with the same persisted database/key can renew the winning token.
        using var restarted = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:QueueFlowDatabase", db.Database.GetConnectionString()));
        using var afterRestart = restarted.CreateClient();
        using var renewed = await afterRestart.PostAsJsonAsync(path, new { refreshToken = winner.RefreshToken }, Ct);
        Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);
        using var invalid = await afterRestart.PostAsJsonAsync(path, new { refreshToken = "unknown-token" }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        Assert.Contains(platform ? "platform.invalid_refresh" : "auth.invalid_refresh", await invalid.Content.ReadAsStringAsync(Ct));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpiredRefreshIsDefinitivelyRejected(bool platform)
    {
        await using var db = await PlatformMigrationTests.CreateDatabaseAsync();
        await db.Database.MigrateAsync(Ct);
        using var isolated = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:QueueFlowDatabase", db.Database.GetConnectionString()));
        var tokens = isolated.Services.GetRequiredService<ITokenService>();
        var raw = tokens.CreateRefreshToken();
        var now = DateTimeOffset.UtcNow;
        if (platform) db.PlatformRefreshTokens.Add(new PlatformRefreshToken(Guid.NewGuid(), Guid.NewGuid(), tokens.HashToken(raw), now.AddMinutes(-1), now.AddDays(-31)));
        else db.RefreshTokens.Add(new RefreshToken(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), tokens.HashToken(raw), now.AddMinutes(-1), now.AddDays(-31)));
        await db.SaveChangesAsync(Ct);
        using var client = isolated.CreateClient();
        using var response = await client.PostAsJsonAsync(platform ? "/api/v1/platform/auth/refresh" : "/api/v1/auth/refresh", new { refreshToken = raw }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(platform ? "platform.invalid_refresh" : "auth.invalid_refresh", await response.Content.ReadAsStringAsync(Ct));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedRotationRollsBackAndInactiveUsersCannotRefresh(bool platform)
    {
        await using var db = await PlatformMigrationTests.CreateDatabaseAsync();
        await db.Database.MigrateAsync(Ct);
        using var isolated = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:QueueFlowDatabase", db.Database.GetConnectionString()));
        var tokens = isolated.Services.GetRequiredService<ITokenService>();
        var raw = tokens.CreateRefreshToken();
        var now = DateTimeOffset.UtcNow;
        var userId = Guid.NewGuid();
        if (platform)
        {
            db.PlatformUsers.Add(new PlatformUser(userId, "Admin", "failure@example.test", "unused", now));
            db.PlatformRefreshTokens.Add(new PlatformRefreshToken(Guid.NewGuid(), userId, tokens.HashToken(raw), now.AddDays(30), now));
        }
        else
        {
            var organizationId = Guid.NewGuid();
            db.Users.Add(new AppUser(userId, organizationId, "Owner", "failure@example.test", "unused", UserRole.Owner, now));
            db.RefreshTokens.Add(new RefreshToken(Guid.NewGuid(), organizationId, userId, tokens.HashToken(raw), now.AddDays(30), now));
        }
        await db.SaveChangesAsync(Ct);
        // Only this disposable database is modified; failure occurs between revocation and commit.
        var table = platform ? "PlatformRefreshTokens" : "RefreshTokens";
        var triggerSql = """
            CREATE FUNCTION reject_test_rotation() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Injected rotation failure'; END; $$;
            CREATE TRIGGER reject_test_rotation BEFORE INSERT ON "__REFRESH_TABLE__"
            FOR EACH ROW EXECUTE FUNCTION reject_test_rotation();
            """.Replace("__REFRESH_TABLE__", table, StringComparison.Ordinal);
        await db.Database.ExecuteSqlRawAsync(triggerSql, Ct);
        using var client = isolated.CreateClient();
        var path = platform ? "/api/v1/platform/auth/refresh" : "/api/v1/auth/refresh";
        using var failed = await client.PostAsJsonAsync(path, new { refreshToken = raw }, Ct);
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        var revokedAt = platform ? await db.PlatformRefreshTokens.AsNoTracking().Select(x => x.RevokedAt).SingleAsync(Ct)
            : await db.RefreshTokens.IgnoreQueryFilters().AsNoTracking().Select(x => x.RevokedAt).SingleAsync(Ct);
        Assert.Null(revokedAt);
        await db.Database.ExecuteSqlRawAsync(platform
            ? "DROP TRIGGER reject_test_rotation ON \"PlatformRefreshTokens\""
            : "DROP TRIGGER reject_test_rotation ON \"RefreshTokens\"", Ct);
        using var recovered = await client.PostAsJsonAsync(path, new { refreshToken = raw }, Ct);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        var pair = (await recovered.Content.ReadFromJsonAsync<TokenPair>(Ct))!;
        if (platform) (await db.PlatformUsers.SingleAsync(Ct)).SetActive(false, now);
        else (await db.Users.IgnoreQueryFilters().SingleAsync(Ct)).SetActive(false, now);
        await db.SaveChangesAsync(Ct);
        using var inactive = await client.PostAsJsonAsync(path, new { refreshToken = pair.RefreshToken }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, inactive.StatusCode);
        if (platform) (await db.PlatformUsers.SingleAsync(Ct)).SetActive(true, now);
        else (await db.Users.IgnoreQueryFilters().SingleAsync(Ct)).SetActive(true, now);
        await db.SaveChangesAsync(Ct);
        using var replay = await client.PostAsJsonAsync(path, new { refreshToken = pair.RefreshToken }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
    }
}
