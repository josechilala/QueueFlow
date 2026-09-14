using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QueueFlow.Domain.Common;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;
using QueueFlow.Infrastructure.Persistence;
using QueueFlow.IntegrationTests.Health;
using QueueEntity = QueueFlow.Domain.Entities.Queue;

namespace QueueFlow.IntegrationTests.Queues;

public sealed class PublicBranchCatalogTests(QueueFlowApiFactory factory) : IClassFixture<QueueFlowApiFactory>
{
    [Fact]
    public async Task CatalogPreservesModesOwnershipHistoryAndTicketRouting()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (!await db.Database.CanConnectAsync(ct)) Assert.Skip("PostgreSQL is required.");
        if ((await db.Database.GetPendingMigrationsAsync(ct)).Any()) Assert.Skip("Apply migrations first.");
        var now = DateTimeOffset.UtcNow;
        var org = new Organization(Guid.NewGuid(), "Catalog", Guid.NewGuid().ToString("N"), "UTC", now);
        var foreignOrg = new Organization(Guid.NewGuid(), "Foreign", Guid.NewGuid().ToString("N"), "UTC", now);
        var branch = new Branch(Guid.NewGuid(), org.Id, "Salon", "UTC", now);
        var otherBranch = new Branch(Guid.NewGuid(), org.Id, "Other", "UTC", now);
        Service Make(string name) => new(Guid.NewGuid(), org.Id, branch.Id, name, null, "T", 10, now);
        var a = Make("A"); var b = Make("B"); var c = Make("C"); c.SetAttendanceMode(ServiceAttendanceMode.AppointmentOnly, now);
        var hybrid = Make("Hybrid"); hybrid.SetAttendanceMode(ServiceAttendanceMode.Hybrid, now);
        var missing = Make("Missing"); var inactive = Make("Inactive");
        var other = new Service(Guid.NewGuid(), org.Id, otherBranch.Id, "Other branch", null, "O", 10, now);
        var foreign = new Service(Guid.NewGuid(), foreignOrg.Id, branch.Id, "Foreign tenant", null, "F", 10, now);
        QueueEntity MakeQueue(Service service) { var q = new QueueEntity(Guid.NewGuid(), org.Id, branch.Id, service.Id, "Same name", null, now); q.Open(now); return q; }
        var qa = MakeQueue(a); var qb = MakeQueue(b); var qh = MakeQueue(hybrid);
        var settings = new ServiceSchedulingSettings(Guid.NewGuid(), org.Id, branch.Id, c.Id, now);
        var hybridSettings = new ServiceSchedulingSettings(Guid.NewGuid(), org.Id, branch.Id, hybrid.Id, now);
        db.AddRange(org, foreignOrg, branch, otherBranch, a, b, c, hybrid, missing, inactive, other, foreign, qa, qb, qh, settings, hybridSettings);
        db.Entry(inactive).Property(x => x.IsActive).CurrentValue = false;
        await db.SaveChangesAsync(ct);
        try
        {
        using var client = factory.CreateClient();
        async Task<Dictionary<string, JsonElement>> Catalog()
        {
            using var response = await client.GetAsync($"/api/v1/public/branches/{branch.PublicId}", ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return json.RootElement.GetProperty("services").EnumerateArray().ToDictionary(x => x.GetProperty("name").GetString()!, x => x.Clone());
        }
        var catalog = await Catalog();
        Assert.Equal(5, catalog.Count);
        Assert.True(catalog["A"].GetProperty("canJoinQueue").GetBoolean());
        Assert.True(catalog["B"].GetProperty("canJoinQueue").GetBoolean());
        Assert.Equal(qa.PublicId, catalog["A"].GetProperty("queue").GetProperty("publicId").GetString());
        Assert.Equal(qb.PublicId, catalog["B"].GetProperty("queue").GetProperty("publicId").GetString());
        Assert.False(catalog["C"].GetProperty("canJoinQueue").GetBoolean());
        Assert.True(catalog["C"].GetProperty("canSchedule").GetBoolean());
        Assert.True(catalog["Hybrid"].GetProperty("canJoinQueue").GetBoolean());
        Assert.True(catalog["Hybrid"].GetProperty("canSchedule").GetBoolean());
        Assert.False(catalog["Missing"].GetProperty("canJoinQueue").GetBoolean());
        using var issue = await client.PostAsJsonAsync($"/api/v1/public/queues/{qb.PublicId}/tickets", new { priority = "Normal" }, ct);
        Assert.Equal(HttpStatusCode.OK, issue.StatusCode);
        var ticket = await db.QueueTickets.IgnoreQueryFilters().SingleAsync(x => x.QueueId == qb.Id, ct);
        Assert.Equal(b.Id, ticket.ServiceId); Assert.Equal(org.Id, ticket.OrganizationId); Assert.Equal(branch.Id, ticket.BranchId);
        var duplicate = MakeQueue(a); db.Add(duplicate);
        await Assert.ThrowsAsync<DomainException>(() => db.SaveChangesAsync(ct));
        db.Entry(duplicate).State = EntityState.Detached;
        qa.Close(now); await db.SaveChangesAsync(ct);
        Assert.False((await Catalog())["A"].GetProperty("canJoinQueue").GetBoolean());
        using var closed = await client.PostAsJsonAsync($"/api/v1/public/queues/{qa.PublicId}/tickets", new { priority = "Normal" }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, closed.StatusCode);
        var replacement = MakeQueue(a); db.Add(replacement); await db.SaveChangesAsync(ct);
        Assert.Equal(2, await db.Queues.IgnoreQueryFilters().CountAsync(x => x.ServiceId == a.Id, ct));
        qb.Pause(now); await db.SaveChangesAsync(ct);
        Assert.False((await Catalog())["B"].GetProperty("canJoinQueue").GetBoolean());
        using var paused = await client.PostAsJsonAsync($"/api/v1/public/queues/{qb.PublicId}/tickets", new { priority = "Normal" }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, paused.StatusCode);
        // A malformed legacy relationship must not leak into the catalog or issue tickets.
        var malformed = new QueueEntity(Guid.NewGuid(), foreignOrg.Id, branch.Id, missing.Id, "Wrong tenant", null, now); malformed.Open(now); db.Add(malformed); await db.SaveChangesAsync(ct);
        Assert.False((await Catalog())["Missing"].GetProperty("canJoinQueue").GetBoolean());
        using var invalid = await client.PostAsJsonAsync($"/api/v1/public/queues/{malformed.PublicId}/tickets", new { priority = "Normal" }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        finally
        {
            await db.TicketEvents.IgnoreQueryFilters().Where(x => x.OrganizationId == org.Id || x.OrganizationId == foreignOrg.Id).ExecuteDeleteAsync(ct);
            await db.QueueTickets.IgnoreQueryFilters().Where(x => x.OrganizationId == org.Id || x.OrganizationId == foreignOrg.Id).ExecuteDeleteAsync(ct);
            await db.Queues.IgnoreQueryFilters().Where(x => x.OrganizationId == org.Id || x.OrganizationId == foreignOrg.Id).ExecuteDeleteAsync(ct);
            await db.ServiceSchedulingSettings.IgnoreQueryFilters().Where(x => x.OrganizationId == org.Id).ExecuteDeleteAsync(ct);
            await db.Services.IgnoreQueryFilters().Where(x => x.OrganizationId == org.Id || x.OrganizationId == foreignOrg.Id).ExecuteDeleteAsync(ct);
            await db.Branches.IgnoreQueryFilters().Where(x => x.OrganizationId == org.Id).ExecuteDeleteAsync(ct);
            await db.Organizations.Where(x => x.Id == org.Id || x.Id == foreignOrg.Id).ExecuteDeleteAsync(ct);
        }
    }
}
