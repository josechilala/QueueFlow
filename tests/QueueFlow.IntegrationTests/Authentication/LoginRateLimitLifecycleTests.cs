using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;
using QueueFlow.IntegrationTests.Platform;

namespace QueueFlow.IntegrationTests.Authentication;

public sealed class LoginRateLimitLifecycleTests(PlatformTestFactory factory) : IClassFixture<PlatformTestFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ValidLoginWrongPasswordsBlockedValidPasswordDifferentEmailAndWindowExpiry()
    {
        await using var db = await PlatformMigrationTests.CreateDatabaseAsync();
        await db.Database.MigrateAsync(Ct);
        using var isolated = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:QueueFlowDatabase", db.Database.GetConnectionString());
            builder.UseSetting("RateLimiting:AuthPermitLimit", "10");
            builder.UseSetting("RateLimiting:GlobalPermitLimit", "300");
        });
        const string password = "Correct-test-password-123!";
        var hash = isolated.Services.GetRequiredService<IPasswordService>().Hash(password);
        var now = DateTimeOffset.UtcNow;
        var org = new Organization(Guid.NewGuid(), "Login test", $"login-{Guid.NewGuid():N}", "UTC", now);
        db.AddRange(org,
            new AppUser(Guid.NewGuid(), org.Id, "First", "first@example.test", hash, UserRole.Owner, now),
            new AppUser(Guid.NewGuid(), org.Id, "Second", "second@example.test", hash, UserRole.Owner, now));
        await db.SaveChangesAsync(Ct);
        using var client = isolated.CreateClient();
        using var valid = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "first@example.test", password }, Ct);
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        for (var attempt = 0; attempt < 9; attempt++)
        {
            using var wrong = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "first@example.test", password = "wrong-password" }, Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }
        using var blocked = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "first@example.test", password }, Ct);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Equal("auth", blocked.Headers.GetValues("X-RateLimit-Policy").Single());
        var retry = blocked.Headers.RetryAfter!.Delta!.Value;
        Assert.InRange(retry.TotalSeconds, 1, 60);
        using var other = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "second@example.test", password }, Ct);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        // Exercise the real production window, without changing limiter options or its clock.
        await Task.Delay(retry + TimeSpan.FromSeconds(1), Ct);
        using var recovered = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "first@example.test", password }, Ct);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
    }
}
