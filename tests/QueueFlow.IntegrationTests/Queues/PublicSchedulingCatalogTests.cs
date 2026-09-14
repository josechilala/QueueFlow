using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;
using QueueFlow.Infrastructure.Persistence;
using QueueFlow.IntegrationTests.Health;

namespace QueueFlow.IntegrationTests.Queues;

public sealed class PublicSchedulingCatalogTests(QueueFlowApiFactory factory) : IClassFixture<QueueFlowApiFactory>
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task SchedulingCatalogAndBookingStayWithinActiveTenantCatalog(int count)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (!await db.Database.CanConnectAsync(ct)) Assert.Skip("PostgreSQL is required.");
        if ((await db.Database.GetPendingMigrationsAsync(ct)).Any()) Assert.Skip("Apply test migrations first.");
        var now = DateTimeOffset.UtcNow;
        var org = new Organization(Guid.NewGuid(), "Scheduling", Guid.NewGuid().ToString("N"), "UTC", now);
        var foreign = new Organization(Guid.NewGuid(), "Foreign", Guid.NewGuid().ToString("N"), "UTC", now);
        var branches = Enumerable.Range(0, count).Select(i => new Branch(Guid.NewGuid(), org.Id, $"Branch {i}", "UTC", now)).ToArray();
        var branch = branches[0];
        var inactiveBranch = new Branch(Guid.NewGuid(), org.Id, "Inactive", "UTC", now); inactiveBranch.SetActive(false, now);
        Service Make(string name, ServiceAttendanceMode mode) { var s = new Service(Guid.NewGuid(), org.Id, branch.Id, name, null, "A", 30, now); s.SetAttendanceMode(mode, now); return s; }
        var queue = Make("Queue", ServiceAttendanceMode.QueueOnly); var appointment = Make("Appointment", ServiceAttendanceMode.AppointmentOnly); var hybrid = Make("Hybrid", ServiceAttendanceMode.Hybrid); var inactive = Make("Inactive", ServiceAttendanceMode.AppointmentOnly);
        var foreignService = new Service(Guid.NewGuid(), foreign.Id, branch.Id, "Foreign", null, "F", 30, now); foreignService.SetAttendanceMode(ServiceAttendanceMode.Hybrid, now);
        var start = new DateTimeOffset(now.UtcDateTime.Date.AddDays(2).AddHours(10), TimeSpan.Zero);
        var settings = new ServiceSchedulingSettings(Guid.NewGuid(), org.Id, branch.Id, appointment.Id, now);
        settings.Configure(30, 1, 0, 30, 10, 60, 30, true, true, false, true, now);
        var hybridSettings = new ServiceSchedulingSettings(Guid.NewGuid(), org.Id, branch.Id, hybrid.Id, now);
        var schedule = new ServiceSchedule(Guid.NewGuid(), org.Id, branch.Id, appointment.Id, start.DayOfWeek, new(10, 0), new(10, 30), now);
        db.AddRange(org, foreign, inactiveBranch, queue, appointment, hybrid, inactive, foreignService, settings, hybridSettings, schedule); db.AddRange(branches);
        db.Entry(inactive).Property(x => x.IsActive).CurrentValue = false;
        await db.SaveChangesAsync(ct);
        try
        {
            using var client = factory.CreateClient();
            using var response = await client.GetAsync($"/api/v1/public/organizations/{org.Slug}/scheduling", ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var json = await response.Content.ReadAsStringAsync(ct);
            Assert.DoesNotContain("queuePublicId", json); Assert.DoesNotContain("canJoinQueue", json); Assert.DoesNotContain("QueueOnly", json);
            using var doc = JsonDocument.Parse(json);
            var resultBranches = doc.RootElement.GetProperty("branches").EnumerateArray().ToArray(); Assert.Equal(count, resultBranches.Length);
            var services = resultBranches.Single(x => x.GetProperty("publicId").GetString() == branch.PublicId).GetProperty("services").EnumerateArray().ToArray();
            Assert.Equal(2, services.Length); Assert.All(services, x => Assert.True(x.GetProperty("canSchedule").GetBoolean()));
            var date = start.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            using var availability = await client.GetAsync($"/api/v1/public/branches/{branch.PublicId}/services/{appointment.PublicId}/availability?date={date}", ct);
            Assert.Equal(HttpStatusCode.OK, availability.StatusCode);
            using var available = JsonDocument.Parse(await availability.Content.ReadAsStringAsync(ct)); Assert.Single(available.RootElement.GetProperty("slots").EnumerateArray());
            foreach (var invalidService in new[] { queue, inactive, foreignService })
            {
                using var invalid = await client.GetAsync($"/api/v1/public/branches/{branch.PublicId}/services/{invalidService.PublicId}/availability?date={date}", ct); Assert.Equal(HttpStatusCode.NotFound, invalid.StatusCode);
                using var rejected = await client.PostAsJsonAsync("/api/v1/public/appointments", new { branchPublicId = branch.PublicId, servicePublicId = invalidService.PublicId, scheduledStart = start, customerName = "Customer" }, ct); Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
            }
            using var wrongBranch = await client.GetAsync($"/api/v1/public/branches/{inactiveBranch.PublicId}/services/{appointment.PublicId}/availability?date={date}", ct); Assert.Equal(HttpStatusCode.NotFound, wrongBranch.StatusCode);
            var body = new { branchPublicId = branch.PublicId, servicePublicId = appointment.PublicId, scheduledStart = start, customerName = "Customer", customerEmail = "customer@test.local" };
            using var booking = await client.PostAsJsonAsync("/api/v1/public/appointments", body, ct); Assert.Equal(HttpStatusCode.Created, booking.StatusCode);
            var saved = await db.Appointments.IgnoreQueryFilters().SingleAsync(x => x.OrganizationId == org.Id, ct); Assert.Equal(branch.Id, saved.BranchId); Assert.Equal(appointment.Id, saved.ServiceId);
            using var full = await client.PostAsJsonAsync("/api/v1/public/appointments", body, ct); Assert.Equal(HttpStatusCode.Conflict, full.StatusCode);
            using var outside = await client.PostAsJsonAsync("/api/v1/public/appointments", new { body.branchPublicId, body.servicePublicId, scheduledStart = start.AddHours(2), body.customerName }, ct); Assert.Equal(HttpStatusCode.Conflict, outside.StatusCode);
            using var operational = await client.GetAsync($"/api/v1/public/branches/{branch.PublicId}", ct); Assert.Equal(HttpStatusCode.OK, operational.StatusCode); Assert.Contains(queue.PublicId, await operational.Content.ReadAsStringAsync(ct));
        }
        finally
        {
            await db.OutboxMessages.IgnoreQueryFilters().Where(x => x.OrganizationId == org.Id).ExecuteDeleteAsync(ct);
            await db.Appointments.IgnoreQueryFilters().Where(x => x.OrganizationId == org.Id).ExecuteDeleteAsync(ct);
            await db.ServiceSchedules.IgnoreQueryFilters().Where(x => x.OrganizationId == org.Id).ExecuteDeleteAsync(ct);
            await db.ServiceSchedulingSettings.IgnoreQueryFilters().Where(x => x.OrganizationId == org.Id).ExecuteDeleteAsync(ct);
            await db.Services.IgnoreQueryFilters().Where(x => x.OrganizationId == org.Id || x.OrganizationId == foreign.Id).ExecuteDeleteAsync(ct);
            await db.Branches.IgnoreQueryFilters().Where(x => x.OrganizationId == org.Id).ExecuteDeleteAsync(ct);
            await db.Organizations.Where(x => x.Id == org.Id || x.Id == foreign.Id).ExecuteDeleteAsync(ct);
        }
    }
}
