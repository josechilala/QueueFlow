using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Abstractions.Clock;
using QueueFlow.Application.Abstractions.Persistence;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;
using QueueFlow.Infrastructure.Persistence;
using QueueFlow.IntegrationTests.Health;

namespace QueueFlow.IntegrationTests.Appointments;

public sealed class OperationalAppointmentEndpointTests(QueueFlowApiFactory factory) : IClassFixture<QueueFlowApiFactory>
{
    [Fact]
    public async Task CheckInIsOnlyExposedThroughAuthenticatedOperations()
    {
        var ct = TestContext.Current.CancellationToken;
        using var publicClient = factory.CreateClient();
        var appointmentId = Guid.NewGuid();
        using var publicCheckIn = await publicClient.PostAsJsonAsync($"/api/v1/public/appointments/{Guid.NewGuid():N}/check-in", new { }, ct);
        Assert.Equal(HttpStatusCode.NotFound, publicCheckIn.StatusCode);
        using var scope = factory.Services.CreateScope();
        var adminToken = scope.ServiceProvider.GetRequiredService<ITokenService>().CreateAccessToken(Guid.NewGuid(), Guid.NewGuid(), UserRole.Admin, "admin@test.local");
        using var adminClient = AuthenticatedClient(adminToken);
        using var adminCheckIn = await adminClient.PostAsJsonAsync($"/api/v1/appointments/{appointmentId}/check-in", new { }, ct);
        Assert.Equal(HttpStatusCode.NotFound, adminCheckIn.StatusCode);
        using var anonymousCheckIn = await publicClient.PostAsJsonAsync($"/api/v1/operations/appointments/{appointmentId}/check-in", new { }, ct);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousCheckIn.StatusCode);
    }

    [Fact]
    public async Task AttendantOnlySeesAssignedBranchAndConcurrentArrivalConfirmationIsIdempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = factory.Services.CreateAsyncScope();
        var db = setup.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (!await db.Database.CanConnectAsync(ct)) Assert.Skip("PostgreSQL is required.");
        if ((await db.Database.GetPendingMigrationsAsync(ct)).Any()) Assert.Skip("Apply pending migrations to the test database.");

        var now = DateTimeOffset.UtcNow;
        var organization = new Organization(Guid.NewGuid(), "Operational appointments", $"operational-{Guid.NewGuid():N}", "UTC", now);
        var assignedBranch = new Branch(Guid.NewGuid(), organization.Id, "Assigned branch", "UTC", now);
        var foreignBranch = new Branch(Guid.NewGuid(), organization.Id, "Foreign branch", "UTC", now);
        var attendant = new AppUser(Guid.NewGuid(), organization.Id, "Reception", $"reception-{Guid.NewGuid():N}@test.local", "test-hash", UserRole.Attendant, now);
        var assignment = new UserBranch(Guid.NewGuid(), organization.Id, attendant.Id, assignedBranch.Id, UserRole.Attendant);
        var assignedService = ServiceFor(organization.Id, assignedBranch.Id, "Assigned service", "A", now);
        var foreignService = ServiceFor(organization.Id, foreignBranch.Id, "Foreign service", "B", now);
        var unavailableService = ServiceFor(organization.Id, assignedBranch.Id, "No queue service", "N", now);
        var assignedSettings = SettingsFor(organization.Id, assignedBranch.Id, assignedService.Id, now);
        var foreignSettings = SettingsFor(organization.Id, foreignBranch.Id, foreignService.Id, now);
        var unavailableSettings = SettingsFor(organization.Id, assignedBranch.Id, unavailableService.Id, now);
        var assignedQueue = QueueFor(organization.Id, assignedBranch.Id, assignedService.Id, "Assigned queue", now);
        var counter = new QueueCounter(Guid.NewGuid(), organization.Id, assignedBranch.Id, "Guichê 01", now);
        var foreignQueue = QueueFor(organization.Id, foreignBranch.Id, foreignService.Id, "Foreign queue", now);
        var appointment = AppointmentFor(organization.Id, assignedBranch.Id, assignedService.Id, "Assigned customer", now);
        var foreignAppointment = AppointmentFor(organization.Id, foreignBranch.Id, foreignService.Id, "Foreign customer", now);
        var unavailableAppointment = AppointmentFor(organization.Id, assignedBranch.Id, unavailableService.Id, "No queue customer", now);
        db.AddRange(organization, assignedBranch, foreignBranch, attendant, assignment, assignedService, foreignService, unavailableService,
            assignedSettings, foreignSettings, unavailableSettings, assignedQueue, foreignQueue, counter, appointment, foreignAppointment, unavailableAppointment);
        await db.SaveChangesAsync(ct);

        try
        {
            var token = setup.ServiceProvider.GetRequiredService<ITokenService>().CreateAccessToken(attendant.Id, organization.Id, UserRole.Attendant, attendant.Email);
            using var firstClient = AuthenticatedClient(token);
            using var secondClient = AuthenticatedClient(token);

            var list = await firstClient.GetFromJsonAsync<OperationalAppointmentResponse[]>("/api/v1/operations/appointments/today", ct);
            Assert.NotNull(list);
            Assert.Equal(2, list.Length);
            Assert.All(list, item => Assert.Equal(assignedBranch.Id, item.BranchId));
            Assert.DoesNotContain(list, item => item.Id == foreignAppointment.Id);

            using var forbidden = await firstClient.PostAsJsonAsync($"/api/v1/operations/appointments/{foreignAppointment.Id}/check-in", new { }, ct);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

            using var unavailable = await firstClient.PostAsJsonAsync($"/api/v1/operations/appointments/{unavailableAppointment.Id}/check-in", new { }, ct);
            Assert.Equal(HttpStatusCode.Conflict, unavailable.StatusCode);
            Assert.Contains("Não há fila ativa para este serviço", await unavailable.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);

            using var publicClient = factory.CreateClient();
            var confirmations = await Task.WhenAll(
                firstClient.PostAsJsonAsync($"/api/v1/operations/appointments/{appointment.Id}/check-in", new { }, ct),
                secondClient.PostAsJsonAsync($"/api/v1/operations/appointments/{appointment.Id}/check-in", new { }, ct));
            Assert.All(confirmations, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            foreach (var response in confirmations) response.Dispose();

            using var repeated = await firstClient.PostAsJsonAsync($"/api/v1/operations/appointments/{appointment.Id}/check-in", new { }, ct);
            Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);

            db.ChangeTracker.Clear();
            var persistedAppointment = await db.Appointments.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == appointment.Id, ct);
            Assert.Equal(AppointmentStatus.CheckedIn, persistedAppointment.Status);
            Assert.NotNull(persistedAppointment.QueueTicketId);
            var tickets = await db.QueueTickets.IgnoreQueryFilters().AsNoTracking().Where(x => x.OrganizationId == organization.Id && x.QueueId == assignedQueue.Id).ToListAsync(ct);
            var ticket = Assert.Single(tickets);
            Assert.Equal(organization.Id, ticket.OrganizationId);
            Assert.Equal(assignedBranch.Id, ticket.BranchId);
            Assert.Equal(assignedService.Id, ticket.ServiceId);
            Assert.Equal(assignedQueue.Id, ticket.QueueId);
            Assert.Equal(persistedAppointment.QueueTicketId, ticket.Id);

            var receipt = await publicClient.GetFromJsonAsync<PublicAppointmentResponse>($"/api/v1/public/appointments/{appointment.PublicToken}", ct);
            Assert.NotNull(receipt);
            Assert.Equal("CheckedIn", receipt.Status);
            Assert.Equal(ticket.CustomerPublicToken, receipt.QueueTicketToken);
            Assert.Equal(ticket.TicketNumber, receipt.QueueTicketNumber);
            var waiting = await publicClient.GetFromJsonAsync<PublicTicketResponse>($"/api/v1/public/tickets/{ticket.CustomerPublicToken}", ct);
            Assert.NotNull(waiting);
            Assert.Equal("Waiting", waiting.Status);
            Assert.Equal(1, waiting.Position);
            Assert.Equal(0, waiting.TicketsAhead);

            using var called = await firstClient.PostAsJsonAsync($"/api/v1/queues/{assignedQueue.Id}/call-next", new { counterId = counter.Id }, ct);
            Assert.Equal(HttpStatusCode.OK, called.StatusCode);
            var publicCall = await publicClient.GetFromJsonAsync<PublicTicketResponse>($"/api/v1/public/tickets/{ticket.CustomerPublicToken}", ct);
            Assert.NotNull(publicCall);
            Assert.Equal("Called", publicCall.Status);
            Assert.Equal(counter.Name, publicCall.CounterName);
            using var started = await firstClient.PostAsync($"/api/v1/tickets/{ticket.Id}/start", null, ct);
            Assert.Equal(HttpStatusCode.OK, started.StatusCode);
            var inService = await publicClient.GetFromJsonAsync<PublicTicketResponse>($"/api/v1/public/tickets/{ticket.CustomerPublicToken}", ct);
            Assert.Equal("InService", inService!.Status);
            using var completed = await firstClient.PostAsync($"/api/v1/tickets/{ticket.Id}/complete", null, ct);
            Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
            var publicCompleted = await publicClient.GetFromJsonAsync<PublicTicketResponse>($"/api/v1/public/tickets/{ticket.CustomerPublicToken}", ct);
            Assert.Equal("Completed", publicCompleted!.Status);
            var completedReceipt = await publicClient.GetFromJsonAsync<PublicAppointmentResponse>($"/api/v1/public/appointments/{appointment.PublicToken}", ct);
            Assert.Equal("Completed", completedReceipt!.Status);
            Assert.Equal(receipt.QueueTicketToken, completedReceipt.QueueTicketToken);

            var finishedAppointment = await db.Appointments.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == appointment.Id, ct);
            Assert.NotNull(finishedAppointment.CompletedAt);
            var history = await db.AppointmentStatusHistory.IgnoreQueryFilters().AsNoTracking().Where(x => x.AppointmentId == appointment.Id).OrderBy(x => x.CreatedAt).ToListAsync(ct);
            Assert.Equal(new[] { AppointmentStatus.CheckedIn, AppointmentStatus.Completed }, history.Select(x => x.NewStatus));
            Assert.Equal(AppointmentStatus.CheckedIn, history[1].PreviousStatus);
            Assert.Equal(attendant.Id, history[1].ChangedByUserId);
            Assert.False(string.IsNullOrWhiteSpace(history[1].Reason));
            var audit = await db.AuditLogs.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.ResourceId == appointment.Id && x.Action == "appointment.completed", ct);
            Assert.Equal(attendant.Id, audit.UserId);
            Assert.Contains(ticket.Id.ToString(), audit.Data!);
            Assert.True(await db.OutboxMessages.IgnoreQueryFilters().AnyAsync(x => x.OrganizationId == organization.Id && x.Type == "appointment.realtime" && x.Payload.Contains("appointment.completed"), ct));

        }
        finally
        {
            await CleanupAsync(db, organization.Id, ct);
        }
    }

    [Fact]
    public async Task EarlyArrivalDoesNotMakeScheduledTicketEligibleBeforeScheduledStart()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (!await db.Database.CanConnectAsync(ct)) Assert.Skip("PostgreSQL is required.");
        if ((await db.Database.GetPendingMigrationsAsync(ct)).Any()) Assert.Skip("Apply pending migrations to the test database.");

        var now = DateTimeOffset.UtcNow;
        var organization = new Organization(Guid.NewGuid(), "Early arrival", $"early-{Guid.NewGuid():N}", "UTC", now);
        var branch = new Branch(Guid.NewGuid(), organization.Id, "Branch", "UTC", now);
        var service = ServiceFor(organization.Id, branch.Id, "Hybrid service", "H", now);
        var settings = SettingsFor(organization.Id, branch.Id, service.Id, now);
        var queue = QueueFor(organization.Id, branch.Id, service.Id, "Hybrid queue", now);
        var counter = new QueueCounter(Guid.NewGuid(), organization.Id, branch.Id, "Counter", now);
        var appointment = new Appointment(Guid.NewGuid(), organization.Id, branch.Id, service.Id, "Early customer", null, null, now.AddMinutes(10), now.AddMinutes(40), "UTC", false, now);
        db.AddRange(organization, branch, service, settings, queue, counter, appointment);
        await db.SaveChangesAsync(ct);

        try
        {
            var appointmentOperations = scope.ServiceProvider.GetRequiredService<IAppointmentOperations>();
            var checkIn = await appointmentOperations.CheckInAsync(appointment.Id, ct);
            Assert.NotNull(checkIn);

            var ticketOperations = scope.ServiceProvider.GetRequiredService<ITicketOperations>();
            var walkIn = await ticketOperations.IssueAsync(queue.PublicId, TicketPriority.Normal, ct);
            Assert.NotNull(walkIn);
            Assert.Equal(0, await ticketOperations.GetTicketsAheadAsync(organization.Id, queue.Id, walkIn.Id, ct));
            Assert.Equal(1, await ticketOperations.GetTicketsAheadAsync(organization.Id, queue.Id, checkIn.Ticket.Id, ct));
            var called = await ticketOperations.CallNextAsync(organization.Id, queue.Id, counter.Id, Guid.NewGuid(), ct);
            Assert.NotNull(called);
            Assert.Equal(walkIn.Id, called.Id);
            Assert.NotEqual(checkIn.Ticket.Id, called.Id);
            Assert.Null(await ticketOperations.CallNextAsync(organization.Id, queue.Id, counter.Id, Guid.NewGuid(), ct));
        }
        finally
        {
            await CleanupAsync(db, organization.Id, ct);
        }
    }

    [Theory]
    [InlineData("priority", true)]
    [InlineData("scheduled", true)]
    [InlineData("future", false)]
    [InlineData("late", false)]
    [InlineData("capacity", false)]
    [InlineData("starvation", false)]
    [InlineData("check-in-order", true)]
    public async Task PublicPositionUsesTheActualCallOrder(string scenario, bool secondIsNext)
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        using var isolated = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton<IClock>(clock)));
        await using var scope = isolated.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (!await db.Database.CanConnectAsync(ct)) Assert.Skip("PostgreSQL is required.");
        if ((await db.Database.GetPendingMigrationsAsync(ct)).Any()) Assert.Skip("The test database must already have the current schema.");

        var now = clock.UtcNow;
        var organization = new Organization(Guid.NewGuid(), "Queue order", $"order-{Guid.NewGuid():N}", "UTC", now);
        var branch = new Branch(Guid.NewGuid(), organization.Id, "Branch", "UTC", now);
        var service = ServiceFor(organization.Id, branch.Id, "Hybrid", "H", now);
        var settings = new ServiceSchedulingSettings(Guid.NewGuid(), organization.Id, branch.Id, service.Id, now);
        settings.Configure(30, 1, 0, 30, 10, 60, 30, true, true, false, true, now);
        var queue = QueueFor(organization.Id, branch.Id, service.Id, "Queue", now);
        var counter = new QueueCounter(Guid.NewGuid(), organization.Id, branch.Id, "Counter", now);
        QueueTicket Ticket(TicketPriority priority, DateTimeOffset issuedAt)
        {
            var sequence = queue.ReserveSequence();
            return new QueueTicket(Guid.NewGuid(), organization.Id, branch.Id, queue.Id, service.Id, $"H-{sequence:000}",
                sequence, priority, Guid.NewGuid().ToString("N"), issuedAt);
        }
        Appointment Scheduled(QueueTicket ticket, DateTimeOffset start, DateTimeOffset checkIn)
        {
            var appointment = new Appointment(Guid.NewGuid(), organization.Id, branch.Id, service.Id, "Customer", null, null,
                start, start.AddMinutes(30), "UTC", false, now.AddHours(-1));
            appointment.CheckIn(ticket.Id, checkIn);
            return appointment;
        }
        var first = Ticket(TicketPriority.Normal, now.AddMinutes(scenario == "starvation" ? -40 : -5));
        var second = Ticket(scenario == "priority" ? TicketPriority.Priority : TicketPriority.Normal, now.AddMinutes(-1));
        db.AddRange(organization, branch, service, settings, queue, counter, first, second);
        if (scenario != "priority")
        {
            var start = scenario switch { "future" => now.AddSeconds(1), "late" => now.AddMinutes(-20), _ => now };
            db.Add(Scheduled(second, start, now.AddMinutes(-10)));
        }
        if (scenario == "check-in-order") db.Add(Scheduled(first, now, now.AddMinutes(-5)));
        if (scenario == "capacity")
        {
            var active = Ticket(TicketPriority.Normal, now.AddMinutes(-10));
            active.Call(counter.Id, Guid.NewGuid(), now);
            db.AddRange(active, Scheduled(active, now, now.AddMinutes(-10)));
        }
        await db.SaveChangesAsync(ct);
        try
        {
            using var client = isolated.CreateClient();
            var firstPublic = await client.GetFromJsonAsync<PublicTicketResponse>($"/api/v1/public/tickets/{first.CustomerPublicToken}", ct);
            var secondPublic = await client.GetFromJsonAsync<PublicTicketResponse>($"/api/v1/public/tickets/{second.CustomerPublicToken}", ct);
            Assert.Equal(secondIsNext ? 2 : 1, firstPublic!.Position);
            Assert.Equal(secondIsNext ? 1 : 0, firstPublic.TicketsAhead);
            Assert.Equal(secondIsNext ? 1 : 2, secondPublic!.Position);
            Assert.Equal(secondIsNext ? 0 : 1, secondPublic.TicketsAhead);
            var operations = scope.ServiceProvider.GetRequiredService<ITicketOperations>();
            var called = await operations.CallNextAsync(organization.Id, queue.Id, counter.Id, Guid.NewGuid(), ct);
            Assert.Equal(secondIsNext ? second.Id : first.Id, called!.Id);
            if (scenario == "future")
            {
                Assert.Null(await operations.CallNextAsync(organization.Id, queue.Id, counter.Id, Guid.NewGuid(), ct));
                clock.UtcNow = now.AddSeconds(1);
                var atScheduledStart = await operations.CallNextAsync(organization.Id, queue.Id, counter.Id, Guid.NewGuid(), ct);
                Assert.Equal(second.Id, atScheduledStart!.Id);
            }
        }
        finally { await CleanupAsync(db, organization.Id, ct); }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = now;
    }

    private HttpClient AuthenticatedClient(string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static Service ServiceFor(Guid organizationId, Guid branchId, string name, string prefix, DateTimeOffset now)
    {
        var service = new Service(Guid.NewGuid(), organizationId, branchId, name, null, prefix, 30, now);
        service.SetAttendanceMode(ServiceAttendanceMode.Hybrid, now);
        return service;
    }

    private static ServiceSchedulingSettings SettingsFor(Guid organizationId, Guid branchId, Guid serviceId, DateTimeOffset now)
    {
        var settings = new ServiceSchedulingSettings(Guid.NewGuid(), organizationId, branchId, serviceId, now);
        settings.Configure(30, 10, 0, 30, 1440, 60, 1440, true, true, false, true, now);
        return settings;
    }

    private static QueueFlow.Domain.Entities.Queue QueueFor(Guid organizationId, Guid branchId, Guid serviceId, string name, DateTimeOffset now)
    {
        var queue = new QueueFlow.Domain.Entities.Queue(Guid.NewGuid(), organizationId, branchId, serviceId, name, null, now);
        queue.Open(now);
        return queue;
    }

    private static Appointment AppointmentFor(Guid organizationId, Guid branchId, Guid serviceId, string customerName, DateTimeOffset now) =>
        new(Guid.NewGuid(), organizationId, branchId, serviceId, customerName, null, null, now, now.AddMinutes(30), "UTC", false, now);

    private static async Task CleanupAsync(ApplicationDbContext db, Guid organizationId, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await db.Notifications.IgnoreQueryFilters().Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
        await db.AuditLogs.IgnoreQueryFilters().Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
        await db.OutboxMessages.IgnoreQueryFilters().Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
        await db.AppointmentStatusHistory.IgnoreQueryFilters().Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
        await db.Appointments.IgnoreQueryFilters().Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
        await db.TicketEvents.IgnoreQueryFilters().Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
        await db.QueueTickets.IgnoreQueryFilters().Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
        await db.QueueCounters.IgnoreQueryFilters().Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
        await db.Queues.IgnoreQueryFilters().Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
        await db.ServiceSchedulingSettings.IgnoreQueryFilters().Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
        await db.Services.IgnoreQueryFilters().Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
        await db.UserBranches.IgnoreQueryFilters().Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
        await db.RefreshTokens.IgnoreQueryFilters().Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
        await db.Users.IgnoreQueryFilters().Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
        await db.Branches.IgnoreQueryFilters().Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
        await db.Organizations.Where(x => x.Id == organizationId).ExecuteDeleteAsync(ct);
    }

    private sealed record PublicAppointmentResponse(string Status, string? QueueTicketToken, string? QueueTicketNumber);
    private sealed record PublicTicketResponse(string Status, int Position, int TicketsAhead, string? CounterName);
    private sealed record OperationalAppointmentResponse(Guid Id, Guid BranchId);
}
