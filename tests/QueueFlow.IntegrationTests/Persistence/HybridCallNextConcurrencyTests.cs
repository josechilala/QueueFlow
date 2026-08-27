using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QueueFlow.Application.Abstractions.Persistence;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;
using QueueFlow.Infrastructure.Persistence;
using QueueFlow.IntegrationTests.Health;

namespace QueueFlow.IntegrationTests.Persistence;

public sealed class HybridCallNextConcurrencyTests(QueueFlowApiFactory factory) : IClassFixture<QueueFlowApiFactory>
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ConcurrentCallNextDoesNotReturnSameTicket(int repetition)
    {
        var ct = TestContext.Current.CancellationToken; await using var setup = factory.Services.CreateAsyncScope(); var db = setup.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (!await db.Database.CanConnectAsync(ct)) Assert.Skip("PostgreSQL is required."); if ((await db.Database.GetPendingMigrationsAsync(ct)).Any()) Assert.Skip("Apply pending migrations to the test database.");
        var now = DateTimeOffset.UtcNow; var organization = new Organization(Guid.NewGuid(), $"Hybrid Concurrency {repetition}", $"hybrid-{Guid.NewGuid():N}", "UTC", now); var branch = new Branch(Guid.NewGuid(), organization.Id, "Branch", "UTC", now); var service = new Service(Guid.NewGuid(), organization.Id, branch.Id, "Hybrid", null, "H", 30, now); service.SetAttendanceMode(ServiceAttendanceMode.Hybrid, now); var settings = new ServiceSchedulingSettings(Guid.NewGuid(), organization.Id, branch.Id, service.Id, now); settings.Configure(30, 10, 0, 30, 10, 60, 30, true, true, false, true, now); var queue = new QueueFlow.Domain.Entities.Queue(Guid.NewGuid(), organization.Id, branch.Id, service.Id, "Hybrid", null, now); queue.Open(now);
        var counters = Enumerable.Range(1, 10).Select(index => new QueueCounter(Guid.NewGuid(), organization.Id, branch.Id, $"Counter {index}", now)).ToArray();
        var tickets = new List<QueueTicket>(); var appointments = new List<Appointment>();
        for (var index = 0; index < 10; index++)
        {
            var sequence = queue.ReserveSequence(); var ticket = new QueueTicket(Guid.NewGuid(), organization.Id, branch.Id, queue.Id, service.Id, $"H-{sequence:000}", sequence, TicketPriority.Normal, Guid.NewGuid().ToString("N"), now.AddMinutes(-20 + index)); tickets.Add(ticket);
            if (index < 4) { var appointment = new Appointment(Guid.NewGuid(), organization.Id, branch.Id, service.Id, $"Scheduled {index}", null, null, now.AddMinutes(-4 + index), now.AddMinutes(26 + index), "UTC", false, now.AddMinutes(-30)); appointment.CheckIn(ticket.Id, now.AddMinutes(-10 + index)); appointments.Add(appointment); }
        }
        db.AddRange(organization, branch, service, settings, queue); db.AddRange(counters); db.AddRange(tickets); db.AddRange(appointments); await db.SaveChangesAsync(ct);
        try
        {
            var calls = counters.Select(async counter => { await using var scope = factory.Services.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<ITicketOperations>().CallNextAsync(organization.Id, queue.Id, counter.Id, Guid.NewGuid(), ct); }); var results = await Task.WhenAll(calls);
            Assert.All(results, Assert.NotNull); Assert.Equal(10, results.Select(x => x!.Id).Distinct().Count()); Assert.Equal(tickets.Select(x => x.Id).Order().ToArray(), results.Select(x => x!.Id).Order().ToArray());
            var persisted = await db.QueueTickets.IgnoreQueryFilters().AsNoTracking().Where(x => x.OrganizationId == organization.Id && x.Status == TicketStatus.Called).ToListAsync(ct); Assert.Equal(10, persisted.Count); Assert.Equal(10, persisted.Select(x => x.CounterId).Distinct().Count()); Assert.Equal(10, persisted.Select(x => x.AttendantUserId).Distinct().Count());
        }
        finally
        {
            await db.AppointmentStatusHistory.IgnoreQueryFilters().Where(x => x.OrganizationId == organization.Id).ExecuteDeleteAsync(ct); await db.Appointments.IgnoreQueryFilters().Where(x => x.OrganizationId == organization.Id).ExecuteDeleteAsync(ct); await db.TicketEvents.IgnoreQueryFilters().Where(x => x.OrganizationId == organization.Id).ExecuteDeleteAsync(ct); await db.QueueTickets.IgnoreQueryFilters().Where(x => x.OrganizationId == organization.Id).ExecuteDeleteAsync(ct); await db.OutboxMessages.IgnoreQueryFilters().Where(x => x.OrganizationId == organization.Id).ExecuteDeleteAsync(ct); await db.QueueCounters.IgnoreQueryFilters().Where(x => x.OrganizationId == organization.Id).ExecuteDeleteAsync(ct); await db.Queues.IgnoreQueryFilters().Where(x => x.OrganizationId == organization.Id).ExecuteDeleteAsync(ct); await db.ServiceSchedulingSettings.IgnoreQueryFilters().Where(x => x.OrganizationId == organization.Id).ExecuteDeleteAsync(ct); await db.Services.IgnoreQueryFilters().Where(x => x.OrganizationId == organization.Id).ExecuteDeleteAsync(ct); await db.Branches.IgnoreQueryFilters().Where(x => x.OrganizationId == organization.Id).ExecuteDeleteAsync(ct); await db.Organizations.Where(x => x.Id == organization.Id).ExecuteDeleteAsync(ct);
        }
    }
}
