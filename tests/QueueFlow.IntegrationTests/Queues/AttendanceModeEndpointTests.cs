using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;
using QueueFlow.Infrastructure.Persistence;
using QueueFlow.IntegrationTests.Health;

namespace QueueFlow.IntegrationTests.Queues;

public sealed class AttendanceModeEndpointTests(QueueFlowApiFactory factory) : IClassFixture<QueueFlowApiFactory>
{
    [Fact]
    public async Task AppointmentOnlyRejectsWalkInTicketIssuance()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (!await db.Database.CanConnectAsync(ct)) Assert.Skip("PostgreSQL is required.");
        if ((await db.Database.GetPendingMigrationsAsync(ct)).Any()) Assert.Skip("Apply pending migrations to the test database.");

        var now = DateTimeOffset.UtcNow;
        var organization = new Organization(Guid.NewGuid(), "Appointment Only Validation", $"appointment-only-{Guid.NewGuid():N}", "UTC", now);
        var branch = new Branch(Guid.NewGuid(), organization.Id, "Branch", "UTC", now);
        var service = new Service(Guid.NewGuid(), organization.Id, branch.Id, "Scheduled Service", null, "A", 30, now);
        service.SetAttendanceMode(ServiceAttendanceMode.AppointmentOnly, now);
        var queue = new QueueFlow.Domain.Entities.Queue(Guid.NewGuid(), organization.Id, branch.Id, service.Id, "Scheduled Queue", null, now);
        queue.Open(now);
        db.AddRange(organization, branch, service, queue);
        await db.SaveChangesAsync(ct);

        try
        {
            using var client = factory.CreateClient();
            using var response = await client.PostAsJsonAsync($"/api/v1/public/queues/{queue.PublicId}/tickets", new { priority = "Normal" }, ct);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False(await db.QueueTickets.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.OrganizationId == organization.Id, ct));
        }
        finally
        {
            await db.TicketEvents.IgnoreQueryFilters().Where(x => x.OrganizationId == organization.Id).ExecuteDeleteAsync(ct);
            await db.QueueTickets.IgnoreQueryFilters().Where(x => x.OrganizationId == organization.Id).ExecuteDeleteAsync(ct);
            await db.OutboxMessages.IgnoreQueryFilters().Where(x => x.OrganizationId == organization.Id).ExecuteDeleteAsync(ct);
            await db.Queues.IgnoreQueryFilters().Where(x => x.OrganizationId == organization.Id).ExecuteDeleteAsync(ct);
            await db.Services.IgnoreQueryFilters().Where(x => x.OrganizationId == organization.Id).ExecuteDeleteAsync(ct);
            await db.Branches.IgnoreQueryFilters().Where(x => x.OrganizationId == organization.Id).ExecuteDeleteAsync(ct);
            await db.Organizations.Where(x => x.Id == organization.Id).ExecuteDeleteAsync(ct);
        }
    }
}
