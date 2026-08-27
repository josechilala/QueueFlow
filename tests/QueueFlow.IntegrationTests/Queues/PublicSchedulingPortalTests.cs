using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;
using QueueFlow.Infrastructure.Persistence;
using QueueFlow.IntegrationTests.Health;

namespace QueueFlow.IntegrationTests.Queues;

public sealed class PublicSchedulingPortalTests(QueueFlowApiFactory factory) : IClassFixture<QueueFlowApiFactory>
{
    [Fact]
    public async Task PublicOrganizationExposesOnlyItsActiveCatalogAndRealAttendanceModes()
    {
        var ct = TestContext.Current.CancellationToken; await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (!await db.Database.CanConnectAsync(ct)) Assert.Skip("PostgreSQL is required."); if ((await db.Database.GetPendingMigrationsAsync(ct)).Any()) Assert.Skip("Apply pending migrations to the test database.");
        var now = DateTimeOffset.UtcNow; var stamp = Guid.NewGuid().ToString("N");
        var companyA = new Organization(Guid.NewGuid(), "Public Company A", $"public-a-{stamp}", "UTC", now); var companyB = new Organization(Guid.NewGuid(), "Public Company B", $"public-b-{stamp}", "UTC", now);
        var branchA = new Branch(Guid.NewGuid(), companyA.Id, "Active Branch", "Public address", "UTC", now); var inactiveBranch = new Branch(Guid.NewGuid(), companyA.Id, "Inactive Branch", "UTC", now); inactiveBranch.SetActive(false, now); var branchB = new Branch(Guid.NewGuid(), companyB.Id, "Foreign Branch", "UTC", now);
        var queueOnly = new Service(Guid.NewGuid(), companyA.Id, branchA.Id, "Walk-in", "Queue service", "Q", 10, now);
        var appointmentOnly = new Service(Guid.NewGuid(), companyA.Id, branchA.Id, "Scheduled", "Appointment service", "A", 20, now); appointmentOnly.SetAttendanceMode(ServiceAttendanceMode.AppointmentOnly, now);
        var hybrid = new Service(Guid.NewGuid(), companyA.Id, branchA.Id, "Hybrid", "Hybrid service", "H", 30, now); hybrid.SetAttendanceMode(ServiceAttendanceMode.Hybrid, now);
        var foreign = new Service(Guid.NewGuid(), companyB.Id, branchB.Id, "Foreign", null, "F", 30, now); foreign.SetAttendanceMode(ServiceAttendanceMode.Hybrid, now);
        var appointmentSettings = new ServiceSchedulingSettings(Guid.NewGuid(), companyA.Id, branchA.Id, appointmentOnly.Id, now); var hybridSettings = new ServiceSchedulingSettings(Guid.NewGuid(), companyA.Id, branchA.Id, hybrid.Id, now); appointmentSettings.Configure(20, 1, 0, 30, 10, 60, 30, true, true, false, true, now); hybridSettings.Configure(30, 1, 0, 30, 10, 60, 30, true, true, false, true, now);
        var queueQueue = new QueueFlow.Domain.Entities.Queue(Guid.NewGuid(), companyA.Id, branchA.Id, queueOnly.Id, "Walk-in Queue", null, now); var appointmentQueue = new QueueFlow.Domain.Entities.Queue(Guid.NewGuid(), companyA.Id, branchA.Id, appointmentOnly.Id, "Scheduled Queue", null, now); var hybridQueue = new QueueFlow.Domain.Entities.Queue(Guid.NewGuid(), companyA.Id, branchA.Id, hybrid.Id, "Hybrid Queue", null, now); queueQueue.Open(now); appointmentQueue.Open(now); hybridQueue.Open(now);
        db.AddRange(companyA, companyB, branchA, inactiveBranch, branchB, queueOnly, appointmentOnly, hybrid, foreign, appointmentSettings, hybridSettings, queueQueue, appointmentQueue, hybridQueue); await db.SaveChangesAsync(ct);
        try
        {
            using var client = factory.CreateClient(); using var response = await client.GetAsync($"/api/v1/public/organizations/{companyA.Slug}", ct); Assert.Equal(HttpStatusCode.OK, response.StatusCode); var json = await response.Content.ReadAsStringAsync(ct);
            Assert.DoesNotContain("organizationId", json, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain(companyB.Name, json); Assert.DoesNotContain(inactiveBranch.Name, json);
            using var document = JsonDocument.Parse(json); var branches = document.RootElement.GetProperty("branches"); var branch = Assert.Single(branches.EnumerateArray()); var services = branch.GetProperty("services").EnumerateArray().ToDictionary(x => x.GetProperty("name").GetString()!);
            Assert.True(services["Walk-in"].GetProperty("canJoinQueue").GetBoolean()); Assert.False(services["Walk-in"].GetProperty("canSchedule").GetBoolean());
            Assert.False(services["Scheduled"].GetProperty("canJoinQueue").GetBoolean()); Assert.True(services["Scheduled"].GetProperty("canSchedule").GetBoolean());
            Assert.True(services["Hybrid"].GetProperty("canJoinQueue").GetBoolean()); Assert.True(services["Hybrid"].GetProperty("canSchedule").GetBoolean());
            using var direct = await client.GetAsync($"/api/v1/public/services/{appointmentOnly.PublicId}", ct); Assert.Equal(HttpStatusCode.OK, direct.StatusCode); var directJson = await direct.Content.ReadAsStringAsync(ct); Assert.Contains(companyA.Slug, directJson); Assert.DoesNotContain("serviceId", directJson, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await db.Queues.IgnoreQueryFilters().Where(x => x.OrganizationId == companyA.Id || x.OrganizationId == companyB.Id).ExecuteDeleteAsync(ct); await db.ServiceSchedulingSettings.IgnoreQueryFilters().Where(x => x.OrganizationId == companyA.Id || x.OrganizationId == companyB.Id).ExecuteDeleteAsync(ct); await db.Services.IgnoreQueryFilters().Where(x => x.OrganizationId == companyA.Id || x.OrganizationId == companyB.Id).ExecuteDeleteAsync(ct); await db.Branches.IgnoreQueryFilters().Where(x => x.OrganizationId == companyA.Id || x.OrganizationId == companyB.Id).ExecuteDeleteAsync(ct); await db.Organizations.Where(x => x.Id == companyA.Id || x.Id == companyB.Id).ExecuteDeleteAsync(ct);
        }
    }
}
