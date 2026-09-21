using Microsoft.EntityFrameworkCore;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Abstractions.Clock;
using QueueFlow.Application.Features.Reports;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;
using QueueFlow.Infrastructure.Persistence;
using QueueFlow.IntegrationTests.Platform;

namespace QueueFlow.IntegrationTests.Reports;

public sealed class OwnerDashboardTests
{
    [Fact]
    public async Task CountsOperationalDataInSavedTimeZoneWithoutListLimitAndIsolatesTenant()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = await PlatformMigrationTests.CreateDatabaseAsync();
        await setup.Database.MigrateAsync(ct);
        var now = new DateTimeOffset(2026, 9, 22, 1, 0, 0, TimeSpan.Zero); // September 21 in São Paulo.
        var organization = new Organization(Guid.NewGuid(), "Owner company", $"dashboard-{Guid.NewGuid():N}", "UTC", now);
        var foreign = new Organization(Guid.NewGuid(), "Other company", $"other-{Guid.NewGuid():N}", "UTC", now);
        var branch = new Branch(Guid.NewGuid(), organization.Id, "Main branch", "UTC", now);
        var inactive = new Branch(Guid.NewGuid(), organization.Id, "Inactive", "UTC", now);
        inactive.SetActive(false, now);
        var service = new Service(Guid.NewGuid(), organization.Id, branch.Id, "Consultation", null, "C", 30, now);
        var open = new Queue(Guid.NewGuid(), organization.Id, branch.Id, service.Id, "Open queue", null, now);
        open.Open(now);
        var closed = new Queue(Guid.NewGuid(), organization.Id, branch.Id, service.Id, "Closed queue", null, now);
        closed.Close(now);
        var foreignBranch = new Branch(Guid.NewGuid(), foreign.Id, "Foreign branch", "UTC", now);
        var foreignService = new Service(Guid.NewGuid(), foreign.Id, foreignBranch.Id, "Foreign service", null, "F", 30, now);
        var foreignQueue = new Queue(Guid.NewGuid(), foreign.Id, foreignBranch.Id, foreignService.Id, "Foreign queue", null, now);
        foreignQueue.Open(now);
        setup.AddRange(organization, foreign, branch, inactive, service, open, closed, foreignBranch, foreignService, foreignQueue);
        QueueTicket Ticket(Queue queue, long sequence) => new(Guid.NewGuid(), queue.OrganizationId, queue.BranchId, queue.Id,
            queue.ServiceId, $"T{sequence}", sequence, TicketPriority.Normal, Guid.NewGuid().ToString("N"), now.AddDays(-2));
        var serving = Ticket(open, 2);
        serving.Call(Guid.NewGuid(), Guid.NewGuid(), now);
        serving.Start(now);
        var called = Ticket(open, 3);
        called.Call(Guid.NewGuid(), Guid.NewGuid(), now);
        setup.AddRange(Ticket(open, 1), serving, called, Ticket(foreignQueue, 1));
        Appointment Booking(DateTimeOffset start, string zone = "America/Sao_Paulo") => new(Guid.NewGuid(), organization.Id,
            branch.Id, service.Id, "Customer", null, null, start, start.AddMinutes(30), zone, false, now);
        // More than the administrative list limit; all are today in the saved timezone.
        for (var i = 0; i < 501; i++) setup.Add(Booking(now.AddMinutes(30)));
        setup.Add(Booking(now.AddHours(-2)));
        setup.Add(Booking(now.AddHours(2))); // Local midnight: tomorrow, still upcoming.
        setup.Add(Booking(now.AddHours(2), "UTC")); // Today in UTC.
        var cancelled = Booking(now.AddMinutes(15));
        cancelled.Cancel(now);
        setup.Add(cancelled);
        setup.Add(new Appointment(Guid.NewGuid(), foreign.Id, foreignBranch.Id, foreignService.Id, "Foreign customer", null,
            null, now.AddMinutes(30), now.AddHours(1), "UTC", false, now));
        await setup.SaveChangesAsync(ct);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(setup.Database.GetConnectionString()).Options;
        var identity = new Identity(organization.Id);
        await using var db = new ApplicationDbContext(options, identity);
        var summary = await new ReportingService(db, new Clock(now), identity).GetDashboardAsync(ct);
        Assert.Equal(organization.Slug, summary.OrganizationSlug);
        Assert.Equal(1, summary.ActiveQueues);
        Assert.Equal(1, summary.Waiting);
        Assert.Equal(1, summary.InService); // Called is not InService; old tickets still count.
        Assert.Equal(1, summary.ActiveBranches);
        Assert.Equal(503, summary.AppointmentsToday);
        Assert.Equal(503, summary.UpcomingAppointments);
        Assert.Equal(2, summary.Queues.Count);
        Assert.Single(summary.QueuesInProgress);
        var row = Assert.Single(summary.Queues, x => x.Id == open.Id);
        Assert.Equal(branch.Name, row.BranchName);
        Assert.Equal(service.Name, row.ServiceName);
        Assert.Equal(1, row.Waiting);
        Assert.Contains(summary.Queues, x => x.Status == QueueStatus.Closed && x.Waiting == 0);

        var empty = new Organization(Guid.NewGuid(), "Empty", $"empty-{Guid.NewGuid():N}", "UTC", now);
        setup.Add(empty);
        await setup.SaveChangesAsync(ct);
        var emptyIdentity = new Identity(empty.Id);
        await using var emptyDb = new ApplicationDbContext(options, emptyIdentity);
        var emptySummary = await new ReportingService(emptyDb, new Clock(now), emptyIdentity).GetDashboardAsync(ct);
        Assert.Equal(0, emptySummary.ActiveQueues + emptySummary.Waiting + emptySummary.InService +
            emptySummary.ActiveBranches + emptySummary.AppointmentsToday + emptySummary.UpcomingAppointments);
        Assert.Empty(emptySummary.Queues);
    }

    private sealed record Clock(DateTimeOffset UtcNow) : IClock;
    private sealed record Identity(Guid TenantId) : ICurrentUser
    {
        public Guid? UserId => null;
        public Guid? OrganizationId => TenantId;
        public UserRole? Role => UserRole.Owner;
        public IdentityType? IdentityType => QueueFlow.Domain.Enums.IdentityType.Tenant;
        public bool IsAuthenticated => true;
    }
}
