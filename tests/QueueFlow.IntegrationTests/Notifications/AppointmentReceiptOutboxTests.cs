using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using QueueFlow.Application.Abstractions.Auditing;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Abstractions.Clock;
using QueueFlow.Application.Abstractions.Persistence;
using QueueFlow.Application.Features.Appointments;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;
using QueueFlow.Infrastructure;
using QueueFlow.Infrastructure.Jobs;
using QueueFlow.Infrastructure.Notifications;
using QueueFlow.Infrastructure.Persistence;
using QueueFlow.IntegrationTests.Platform;

namespace QueueFlow.IntegrationTests.Notifications;

public sealed class AppointmentReceiptOutboxTests
{
    [Fact]
    public async Task ConfirmedPublicBookingSurvivesDeliveryFailureAndConcurrentProcessorsSendOneReceipt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = await PlatformMigrationTests.CreateDatabaseAsync();
        await setup.Database.MigrateAsync(ct);
        var clock = new TestClock();
        var tenant = new Identity();
        using var handler = new AppointmentReceiptSenderTests.Handler { Status = HttpStatusCode.ServiceUnavailable };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:QueueFlowDatabase"] = setup.Database.GetConnectionString() }).Build());
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<ICurrentUser>(tenant);
        services.AddScoped<AppointmentReceiptSender>(_ => AppointmentReceiptSenderTests.Sender(handler));
        await using var provider = services.BuildServiceProvider();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        var now = clock.UtcNow;
        var org = new Organization(Guid.NewGuid(), "Empresa correta", $"receipt-{Guid.NewGuid():N}", "UTC", now);
        var foreign = new Organization(Guid.NewGuid(), "Outra empresa", $"foreign-{Guid.NewGuid():N}", "UTC", now);
        var branch = new Branch(Guid.NewGuid(), org.Id, "Unidade", "UTC", now);
        var service = new Service(Guid.NewGuid(), org.Id, branch.Id, "Consulta", null, "R", 30, now);
        service.SetAttendanceMode(ServiceAttendanceMode.AppointmentOnly, now);
        var settings = new ServiceSchedulingSettings(Guid.NewGuid(), org.Id, branch.Id, service.Id, now);
        settings.Configure(30, 1, 0, 30, 10, 60, 30, true, true, false, true, now);
        var start = new DateTimeOffset(now.UtcDateTime.Date.AddDays(2).AddHours(10), TimeSpan.Zero);
        var schedule = new ServiceSchedule(Guid.NewGuid(), org.Id, branch.Id, service.Id, start.DayOfWeek, new(10, 0), new(12, 0), now);
        setup.AddRange(org, foreign, branch, service, settings, schedule); await setup.SaveChangesAsync(ct);

        Guid appointmentId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var operations = scope.ServiceProvider.GetRequiredService<IAppointmentOperations>();
            var booking = new AppointmentBookingService(operations);
            var result = await booking.CreateAsync(new(branch.PublicId, service.PublicId, start, "Cliente", null, "cliente@example.test"), ct);
            Assert.True(result.IsSuccess); Assert.Equal(AppointmentStatus.Confirmed, result.Value.Status);
            var appointment = await setup.Appointments.IgnoreQueryFilters().SingleAsync(ct);
            appointmentId = appointment.Id;
            var receipt = await setup.OutboxMessages.IgnoreQueryFilters().SingleAsync(x => x.Type == AppointmentReceipt.OutboxType, ct);
            Assert.Equal(org.Id, receipt.OrganizationId);
            Assert.Contains("Empresa correta", receipt.Payload); Assert.DoesNotContain("Outra empresa", receipt.Payload);
            var duplicate = await booking.CreateAsync(new(branch.PublicId, service.PublicId, start, "Other", null, "other@example.test"), ct);
            Assert.False(duplicate.IsSuccess);
        }

        using var first = new OutboxProcessor(scopes, NullLogger<OutboxProcessor>.Instance, receiptsOnly: true);
        using var second = new OutboxProcessor(scopes, NullLogger<OutboxProcessor>.Instance, receiptsOnly: true);
        await first.ProcessBatchAsync(ct);
        setup.ChangeTracker.Clear();
        var failed = await setup.OutboxMessages.IgnoreQueryFilters().SingleAsync(x => x.Id == appointmentId, ct);
        Assert.Equal(1, failed.Attempts); Assert.Null(failed.ProcessedAt);
        Assert.Equal(AppointmentStatus.Confirmed, (await setup.Appointments.IgnoreQueryFilters().SingleAsync(ct)).Status);
        Assert.Single(handler.Bodies);
        handler.Status = HttpStatusCode.OK; clock.UtcNow = now.AddMinutes(1);
        await Task.WhenAll(first.ProcessBatchAsync(ct), second.ProcessBatchAsync(ct));
        setup.ChangeTracker.Clear();
        Assert.NotNull((await setup.OutboxMessages.IgnoreQueryFilters().SingleAsync(x => x.Id == appointmentId, ct)).ProcessedAt);
        Assert.Equal(2, handler.Bodies.Count); Assert.Equal(handler.Keys[0], handler.Keys[1]); Assert.Equal(handler.Bodies[0], handler.Bodies[1]);
        await first.ProcessBatchAsync(ct); Assert.Equal(2, handler.Bodies.Count);

        // Pending public bookings only get a receipt after the existing Admin confirmation.
        var pending = new Appointment(Guid.NewGuid(), org.Id, branch.Id, service.Id, "Pendente", null, "pending@example.test", start.AddHours(1), start.AddMinutes(90), "UTC", true, now);
        setup.Add(pending); await AppointmentReceipt.EnqueueAsync(setup, pending, now, ct); await setup.SaveChangesAsync(ct);
        Assert.False(await setup.OutboxMessages.IgnoreQueryFilters().AnyAsync(x => x.Id == pending.Id, ct));
        tenant.OrganizationId = org.Id;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var management = new AppointmentManagementService(db, tenant, clock, new Audit());
            var confirmed = await management.ConfirmAsync(pending.Id, ct);
            Assert.True(confirmed.IsSuccess); Assert.Equal(AppointmentStatus.Confirmed, confirmed.Value.Status);
            await AppointmentReceipt.EnqueueAsync(db, await db.Appointments.SingleAsync(x => x.Id == pending.Id, ct), now, ct);
            await db.SaveChangesAsync(ct);
        }
        Assert.Equal(1, await setup.OutboxMessages.IgnoreQueryFilters().CountAsync(x => x.Id == pending.Id, ct));
        var adminBooking = new Appointment(Guid.NewGuid(), org.Id, branch.Id, service.Id, "Admin", null, "admin@example.test", start, start.AddMinutes(30), "UTC", false, now, Guid.NewGuid());
        await AppointmentReceipt.EnqueueAsync(setup, adminBooking, now, ct);
        Assert.DoesNotContain(setup.OutboxMessages.Local, x => x.Id == adminBooking.Id);
    }

    private sealed class TestClock : IClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow; }
    private sealed class Identity : ICurrentUser
    {
        public Guid? UserId => null;
        public Guid? OrganizationId { get; set; }
        public UserRole? Role => UserRole.Owner;
        public IdentityType? IdentityType => OrganizationId.HasValue ? QueueFlow.Domain.Enums.IdentityType.Tenant : null;
        public bool IsAuthenticated => OrganizationId.HasValue;
    }
    private sealed class Audit : IAuditWriter { public void Write(string action, string type, Guid? id, object? data = null) { } }
}
