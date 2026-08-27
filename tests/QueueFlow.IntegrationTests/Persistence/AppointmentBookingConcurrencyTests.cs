using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QueueFlow.Application.Abstractions.Persistence;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;
using QueueFlow.Infrastructure.Persistence;
using QueueFlow.IntegrationTests.Health;

namespace QueueFlow.IntegrationTests.Persistence;

public sealed class AppointmentBookingConcurrencyTests(QueueFlowApiFactory factory) : IClassFixture<QueueFlowApiFactory>
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task ConcurrentClientsCannotExceedSlotCapacity(int capacity)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var setupScope = factory.Services.CreateAsyncScope();
        var db = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (!await db.Database.CanConnectAsync(cancellationToken)) Assert.Skip("A configured QueueFlow PostgreSQL database is required for the concurrency test.");
        if ((await db.Database.GetPendingMigrationsAsync(cancellationToken)).Any()) Assert.Skip("Apply the pending QueueFlow migrations to the test database before running the concurrency test.");
        var now = DateTimeOffset.UtcNow; var start = now.UtcDateTime.Date.AddDays(2).AddHours(10); var startAt = new DateTimeOffset(start, TimeSpan.Zero);
        var organization = new Organization(Guid.NewGuid(), "Appointment Concurrency", $"appointment-{Guid.NewGuid():N}", "UTC", now);
        var branch = new Branch(Guid.NewGuid(), organization.Id, "Appointment Branch", "UTC", now);
        var service = new Service(Guid.NewGuid(), organization.Id, branch.Id, "Appointment Service", null, "A", 30, now); service.SetAttendanceMode(ServiceAttendanceMode.AppointmentOnly, now);
        var settings = new ServiceSchedulingSettings(Guid.NewGuid(), organization.Id, branch.Id, service.Id, now); settings.Configure(30, capacity, 0, 30, 10, 60, 30, true, true, false, true, now);
        var schedule = new ServiceSchedule(Guid.NewGuid(), organization.Id, branch.Id, service.Id, startAt.DayOfWeek, new(10, 0), new(10, 30), now);
        db.AddRange(organization, branch, service, settings, schedule); await db.SaveChangesAsync(cancellationToken);
        try
        {
            var attempts = Enumerable.Range(1, 20).Select(async number => { await using var scope = factory.Services.CreateAsyncScope(); var operations = scope.ServiceProvider.GetRequiredService<IAppointmentOperations>(); return await operations.CreateAsync(new(branch.PublicId, service.PublicId, startAt, $"Customer {number}", null, $"customer{number}@example.com"), cancellationToken); });
            var results = await Task.WhenAll(attempts);
            Assert.Equal(capacity, results.Count(result => result is not null));
            Assert.Equal(capacity, await db.Appointments.IgnoreQueryFilters().CountAsync(x => x.OrganizationId == organization.Id, cancellationToken));
        }
        finally
        {
            await db.Appointments.IgnoreQueryFilters().Where(x => x.OrganizationId == organization.Id).ExecuteDeleteAsync(cancellationToken);
            await db.ServiceSchedules.IgnoreQueryFilters().Where(x => x.OrganizationId == organization.Id).ExecuteDeleteAsync(cancellationToken);
            await db.ServiceSchedulingSettings.IgnoreQueryFilters().Where(x => x.OrganizationId == organization.Id).ExecuteDeleteAsync(cancellationToken);
            await db.Services.IgnoreQueryFilters().Where(x => x.OrganizationId == organization.Id).ExecuteDeleteAsync(cancellationToken);
            await db.Branches.IgnoreQueryFilters().Where(x => x.OrganizationId == organization.Id).ExecuteDeleteAsync(cancellationToken);
            await db.Organizations.Where(x => x.Id == organization.Id).ExecuteDeleteAsync(cancellationToken);
        }
    }
}
