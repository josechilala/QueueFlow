using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;

namespace QueueFlow.IntegrationTests.Platform;

public sealed class UserManagementRoleTests(PlatformTestFactory factory) : IClassFixture<PlatformTestFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ApiEnforcesAssignableRolesActorHierarchyLegacyCompatibilityAndTenantIsolation()
    {
        await using var db = await PlatformMigrationTests.CreateDatabaseAsync();
        await db.Database.MigrateAsync(Ct);
        using var isolated = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:QueueFlowDatabase", db.Database.GetConnectionString()));
        var now = DateTimeOffset.UtcNow;
        var org = new Organization(Guid.NewGuid(), "Users", $"users-{Guid.NewGuid():N}", "UTC", now);
        var otherOrg = new Organization(Guid.NewGuid(), "Other", $"other-{Guid.NewGuid():N}", "UTC", now);
        var branch = new Branch(Guid.NewGuid(), org.Id, "Main", "UTC", now);
        var otherBranch = new Branch(Guid.NewGuid(), otherOrg.Id, "Private", "UTC", now);
        var actors = Enum.GetValues<UserRole>().Select(role => new AppUser(Guid.NewGuid(), org.Id, role.ToString(), $"{role}-{Guid.NewGuid():N}@example.test", "unused", role, now)).ToArray();
        var target = new AppUser(Guid.NewGuid(), org.Id, "Target", $"target-{Guid.NewGuid():N}@example.test", "unused", UserRole.Attendant, now);
        var foreign = new AppUser(Guid.NewGuid(), otherOrg.Id, "Foreign", $"foreign-{Guid.NewGuid():N}@example.test", "unused", UserRole.Admin, now);
        db.AddRange(org, otherOrg, branch, otherBranch, target, foreign);
        db.AddRange(actors);
        await db.SaveChangesAsync(Ct);
        var tokens = isolated.Services.GetRequiredService<ITokenService>();

        foreach (var actor in actors)
        {
            using var client = isolated.CreateClient();
            client.DefaultRequestHeaders.Authorization = new("Bearer", tokens.CreateAccessToken(actor.Id, org.Id, actor.Role, actor.Email));
            foreach (var assigned in Enum.GetValues<UserRole>())
            {
                using var created = await client.PostAsJsonAsync("/api/v1/users", new { name = "New user", email = $"new-{Guid.NewGuid():N}@example.test", password = "Test-password-123!", role = assigned.ToString(), branchIds = new[] { branch.Id } }, Ct);
                var allowed = (actor.Role is UserRole.Owner or UserRole.Admin && assigned is UserRole.Admin or UserRole.Manager or UserRole.Attendant)
                    || (actor.Role == UserRole.Manager && assigned == UserRole.Attendant);
                Assert.Equal(allowed ? HttpStatusCode.Created : HttpStatusCode.Forbidden, created.StatusCode);
            }
            foreach (var forbidden in new[] { UserRole.Owner, UserRole.Viewer })
            {
                using var changed = await client.PutAsJsonAsync($"/api/v1/users/{target.Id}", new { name = "Forbidden change", role = forbidden.ToString(), branchIds = new[] { branch.Id } }, Ct);
                Assert.Equal(HttpStatusCode.Forbidden, changed.StatusCode);
            }
            if (actor.Role is UserRole.Attendant or UserRole.Viewer)
            {
                using var status = await client.PatchAsJsonAsync($"/api/v1/users/{target.Id}/status", new { isActive = false }, Ct);
                Assert.Equal(HttpStatusCode.Forbidden, status.StatusCode);
                using var reset = await client.PostAsJsonAsync($"/api/v1/users/{target.Id}/reset-password", new { password = "Test-password-123!" }, Ct);
                Assert.Equal(HttpStatusCode.Forbidden, reset.StatusCode);
                continue;
            }
            using var list = await client.GetAsync("/api/v1/users", Ct);
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            var listed = await list.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Contains(listed.EnumerateArray(), user => user.GetProperty("role").GetString() == "Viewer");
            Assert.DoesNotContain(listed.EnumerateArray(), user => user.GetProperty("id").GetGuid() == foreign.Id);
            var legacy = actors.Single(x => x.Role == UserRole.Viewer);
            using var detail = await client.GetAsync($"/api/v1/users/{legacy.Id}", Ct);
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            using var deactivate = await client.PatchAsJsonAsync($"/api/v1/users/{legacy.Id}/status", new { isActive = false }, Ct);
            Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
            using var reactivate = await client.PatchAsJsonAsync($"/api/v1/users/{legacy.Id}/status", new { isActive = true, role = "Owner" }, Ct);
            Assert.Equal(HttpStatusCode.OK, reactivate.StatusCode);
            Assert.Equal("Viewer", (await reactivate.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("role").GetString());

            var owner = actors.Single(x => x.Role == UserRole.Owner);
            foreach (var active in new[] { false, true })
            {
                using var ownerStatus = await client.PatchAsJsonAsync($"/api/v1/users/{owner.Id}/status", new { isActive = active }, Ct);
                Assert.Equal(HttpStatusCode.Forbidden, ownerStatus.StatusCode);
            }
            using var demote = await client.PutAsJsonAsync($"/api/v1/users/{owner.Id}", new { name = "Demoted", role = "Admin" }, Ct);
            Assert.Equal(HttpStatusCode.Forbidden, demote.StatusCode);
            using var ownerPassword = await client.PostAsJsonAsync($"/api/v1/users/{owner.Id}/reset-password", new { password = "Test-password-123!" }, Ct);
            Assert.Equal(HttpStatusCode.Forbidden, ownerPassword.StatusCode);

            using var foreignRead = await client.GetAsync($"/api/v1/users/{foreign.Id}", Ct);
            using var foreignEdit = await client.PutAsJsonAsync($"/api/v1/users/{foreign.Id}", new { name = "Other tenant", role = "Admin" }, Ct);
            using var foreignStatus = await client.PatchAsJsonAsync($"/api/v1/users/{foreign.Id}/status", new { isActive = false }, Ct);
            using var foreignReset = await client.PostAsJsonAsync($"/api/v1/users/{foreign.Id}/reset-password", new { password = "Test-password-123!" }, Ct);
            foreach (var response in new[] { foreignRead, foreignEdit, foreignStatus, foreignReset }) Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            using var foreignAssignment = await client.PostAsJsonAsync("/api/v1/users", new { name = "Foreign branch", email = $"bad-{Guid.NewGuid():N}@example.test", password = "Test-password-123!", role = "Attendant", branchIds = new[] { otherBranch.Id } }, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, foreignAssignment.StatusCode);
            if (actor.Role == UserRole.Manager)
            {
                var admin = actors.Single(x => x.Role == UserRole.Admin);
                using var editAdmin = await client.PutAsJsonAsync($"/api/v1/users/{admin.Id}", new { name = "Demote admin", role = "Attendant", branchIds = new[] { branch.Id } }, Ct);
                using var disableAdmin = await client.PatchAsJsonAsync($"/api/v1/users/{admin.Id}/status", new { isActive = false }, Ct);
                using var resetAdmin = await client.PostAsJsonAsync($"/api/v1/users/{admin.Id}/reset-password", new { password = "Test-password-123!" }, Ct);
                foreach (var response in new[] { editAdmin, disableAdmin, resetAdmin }) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            }
        }
        await db.Entry(target).ReloadAsync(Ct);
        Assert.Equal(UserRole.Attendant, target.Role);
        Assert.Equal(1, await db.Users.IgnoreQueryFilters().CountAsync(x => x.OrganizationId == org.Id && x.Role == UserRole.Owner, Ct));
    }
}
