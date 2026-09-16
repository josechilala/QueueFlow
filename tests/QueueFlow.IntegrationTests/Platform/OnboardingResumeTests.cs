using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Features.Tenants;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;

namespace QueueFlow.IntegrationTests.Platform;

public sealed class OnboardingResumeTests(PlatformTestFactory factory) : IClassFixture<PlatformTestFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ProgressSurvivesNewSessionsAndCannotCompleteBeforeOperationExists()
    {
        await using var db = await PlatformMigrationTests.CreateDatabaseAsync();
        await db.Database.MigrateAsync(Ct);
        using var isolated = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:QueueFlowDatabase", db.Database.GetConnectionString()));
        var now = DateTimeOffset.UtcNow;
        var org = new Organization(Guid.NewGuid(), "Resume", $"resume-{Guid.NewGuid():N}", "UTC", now);
        var user = new AppUser(Guid.NewGuid(), org.Id, "Owner", $"resume-{Guid.NewGuid():N}@example.test", "unused", UserRole.Owner, now);
        db.AddRange(org, user); await db.SaveChangesAsync(Ct);
        async Task<OnboardingProgress> ReadFromNewSession()
        {
            using var client = isolated.CreateClient();
            client.DefaultRequestHeaders.Authorization = new("Bearer", isolated.Services.GetRequiredService<ITokenService>().CreateAccessToken(user.Id, org.Id, UserRole.Owner, user.Email));
            return (await client.GetFromJsonAsync<OnboardingProgress>("/api/v1/onboarding", Ct))!;
        }
        Assert.Equal("branch", (await ReadFromNewSession()).NextStep);
        var branch = new Branch(Guid.NewGuid(), org.Id, "First", "UTC", now);
        db.Add(branch); await db.SaveChangesAsync(Ct);
        Assert.Equal("services", (await ReadFromNewSession()).NextStep);
        var service = new Service(Guid.NewGuid(), org.Id, branch.Id, "First", null, "RS", 10, now);
        db.Add(service); await db.SaveChangesAsync(Ct);
        Assert.Equal("operation", (await ReadFromNewSession()).NextStep);
        using var owner = isolated.CreateClient();
        owner.DefaultRequestHeaders.Authorization = new("Bearer", isolated.Services.GetRequiredService<ITokenService>().CreateAccessToken(user.Id, org.Id, UserRole.Owner, user.Email));
        using var premature = await owner.PostAsync("/api/v1/onboarding/complete", null, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, premature.StatusCode);
        db.Add(new QueueFlow.Domain.Entities.Queue(Guid.NewGuid(), org.Id, branch.Id, service.Id, "First", null, now));
        await db.SaveChangesAsync(Ct);
        var ready = await ReadFromNewSession();
        Assert.True(ready.OperationReady);
        Assert.False(ready.Completed);
        using var completed = await owner.PostAsync("/api/v1/onboarding/complete", null, Ct);
        Assert.Equal(HttpStatusCode.NoContent, completed.StatusCode);
        Assert.True((await ReadFromNewSession()).Completed);
        Assert.Equal("dashboard", (await ReadFromNewSession()).NextStep);
    }

    [Fact]
    public async Task CrossTenantEmailCollisionReturnsSafeBusinessError()
    {
        await using var db = await PlatformMigrationTests.CreateDatabaseAsync(); await db.Database.MigrateAsync(Ct);
        using var isolated = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:QueueFlowDatabase", db.Database.GetConnectionString()));
        var email = $"collision-{Guid.NewGuid():N}@example.test";
        db.Users.Add(new AppUser(Guid.NewGuid(), Guid.NewGuid(), "Private owner", email, "unused", UserRole.Owner, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(Ct);
        using var owner = isolated.CreateClient();
        owner.DefaultRequestHeaders.Authorization = new("Bearer", isolated.Services.GetRequiredService<ITokenService>().CreateAccessToken(Guid.NewGuid(), Guid.NewGuid(), UserRole.Owner, "other@example.test"));
        using var response = await owner.PostAsJsonAsync("/api/v1/users", new { name = "New user", email, password = "New-password-123!", role = "Viewer" }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.DoesNotContain("Private owner", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }
}
