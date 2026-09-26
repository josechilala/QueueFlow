using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QueueFlow.Infrastructure.Persistence;
using QueueFlow.IntegrationTests.Health;

namespace QueueFlow.IntegrationTests.GoldenPath;

public sealed class GoldenQueueFlowTests : IClassFixture<QueueFlowApiFactory>
{
    private readonly QueueFlowApiFactory _factory;
    public GoldenQueueFlowTests(QueueFlowApiFactory factory) => _factory = factory;

    [Fact]
    public async Task MainProductFlowWorksEndToEnd()
    {
        await using var scope = _factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); var ct = TestContext.Current.CancellationToken;
        if (!await db.Database.CanConnectAsync(ct)) Assert.Skip("The golden E2E test requires the configured QueueFlow PostgreSQL database.");
        if ((await db.Database.GetPendingMigrationsAsync(ct)).Any()) Assert.Skip("Apply pending QueueFlow migrations before running the golden E2E test.");
        var stamp = Guid.NewGuid().ToString("N"); var email = $"golden-{stamp}@test.local"; var password = $"Qf!Golden-{stamp}"; Guid organizationId = Guid.Empty;
        var client = _factory.CreateClient();
        try
        {
            var register = await client.PostAsJsonAsync("/api/v1/auth/register", new { name = "Golden Company", slug = $"golden-{stamp}", timeZone = "America/Sao_Paulo", adminName = "Golden Owner", adminEmail = email, password }, ct);
            Assert.Equal(HttpStatusCode.Created, register.StatusCode); organizationId = (await register.Content.ReadFromJsonAsync<OrganizationCreated>(ct))!.OrganizationId;

            var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password }, ct); Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var tokens = await login.Content.ReadFromJsonAsync<TokenPair>(ct); Assert.NotNull(tokens); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

            var branchResponse = await client.PostAsJsonAsync("/api/v1/branches", new { name = "Unidade Ouro", address = "Rua E2E, 25", timeZone = "America/Sao_Paulo" }, ct); Assert.Equal(HttpStatusCode.Created, branchResponse.StatusCode);
            var branch = await branchResponse.Content.ReadFromJsonAsync<BranchResponse>(ct); Assert.NotNull(branch);

            var serviceResponse = await client.PostAsJsonAsync("/api/v1/services", new { branchId = branch.Id, name = "Consulta", description = "Fluxo principal", prefix = "A", averageDurationMinutes = 10 }, ct); Assert.Equal(HttpStatusCode.Created, serviceResponse.StatusCode);
            var service = await serviceResponse.Content.ReadFromJsonAsync<IdResponse>(ct); Assert.NotNull(service);

            var counterResponse = await client.PostAsJsonAsync("/api/v1/counters", new { branchId = branch.Id, name = "Guichê 01" }, ct); Assert.Equal(HttpStatusCode.Created, counterResponse.StatusCode);
            var counter = await counterResponse.Content.ReadFromJsonAsync<IdResponse>(ct); Assert.NotNull(counter);

            var queueResponse = await client.PostAsJsonAsync("/api/v1/queues", new { branchId = branch.Id, serviceId = service.Id, name = "Fila de Consulta", capacity = 50 }, ct); Assert.Equal(HttpStatusCode.Created, queueResponse.StatusCode);
            var queue = await queueResponse.Content.ReadFromJsonAsync<QueueResponse>(ct); Assert.NotNull(queue); Assert.Matches("^[a-f0-9]{32}$", queue.PublicId);
            var qrTarget = $"/q/{queue.PublicId}"; Assert.DoesNotContain(queue.Id.ToString(), qrTarget, StringComparison.OrdinalIgnoreCase);

            var open = await client.PostAsync($"/api/v1/queues/{queue.Id}/open", null, ct); Assert.Equal(HttpStatusCode.OK, open.StatusCode);
            var publicQueue = await client.GetAsync($"/api/v1/public/queues/{queue.PublicId}", ct); Assert.Equal(HttpStatusCode.OK, publicQueue.StatusCode);

            var first = await IssueAsync(client, queue.PublicId, ct); var second = await IssueAsync(client, queue.PublicId, ct);
            Assert.Equal("A-001", first.TicketNumber); Assert.Equal("A-002", second.TicketNumber); Assert.NotEqual(first.CustomerPublicToken, second.CustomerPublicToken);

            var calledResponse = await client.PostAsJsonAsync($"/api/v1/queues/{queue.Id}/call-next", new { counterId = counter.Id }, ct); Assert.Equal(HttpStatusCode.OK, calledResponse.StatusCode);
            var called = await calledResponse.Content.ReadFromJsonAsync<TicketOperationResponse>(ct); Assert.NotNull(called); Assert.Equal(first.Id, called.Id); Assert.Equal("Called", called.Status);
            var firstCalled = await PublicTicketAsync(client, first.CustomerPublicToken, ct); Assert.Equal("Called", firstCalled.Status); Assert.Equal("Guichê 01", firstCalled.CounterName);

            var started = await client.PostAsync($"/api/v1/tickets/{first.Id}/start", null, ct); Assert.Equal(HttpStatusCode.OK, started.StatusCode);
            var completed = await client.PostAsync($"/api/v1/tickets/{first.Id}/complete", null, ct); Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
            var firstCompleted = await PublicTicketAsync(client, first.CustomerPublicToken, ct); Assert.Equal("Completed", firstCompleted.Status);
            var secondNext = await PublicTicketAsync(client, second.CustomerPublicToken, ct); Assert.Equal("Waiting", secondNext.Status); Assert.Equal(1, secondNext.Position); Assert.Equal(0, secondNext.TicketsAhead);

            var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"); var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone); var localTarget = DateTime.SpecifyKind(localNow.DateTime.AddMinutes(5), DateTimeKind.Unspecified); localTarget = new DateTime(localTarget.Year, localTarget.Month, localTarget.Day, localTarget.Hour, localTarget.Minute, 0, DateTimeKind.Unspecified); var target = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localTarget, zone), TimeSpan.Zero);
            var settingsResponse = await client.PutAsJsonAsync($"/api/v1/services/{service.Id}/scheduling-settings", new { attendanceMode = "Hybrid", slotDurationMinutes = 30, capacityPerSlot = 1, minimumAdvanceMinutes = 0, maximumAdvanceDays = 30, lateToleranceMinutes = 10, cancellationDeadlineMinutes = 60, checkInAdvanceMinutes = 60, allowCustomerCancellation = true, allowCustomerReschedule = true, requireConfirmation = false, isActive = true }, ct); Assert.Equal(HttpStatusCode.OK, settingsResponse.StatusCode);
            var scheduleResponse = await client.PostAsJsonAsync($"/api/v1/services/{service.Id}/schedules", new { dayOfWeek = localTarget.DayOfWeek.ToString(), startTime = TimeOnly.FromDateTime(localTarget).ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture), endTime = TimeOnly.FromDateTime(localTarget.AddMinutes(30)).ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) }, ct); Assert.Equal(HttpStatusCode.OK, scheduleResponse.StatusCode);
            var availability = await client.GetAsync($"/api/v1/public/branches/{branch.PublicId}/services/{service.PublicId}/availability?date={localTarget:yyyy-MM-dd}", ct); Assert.Equal(HttpStatusCode.OK, availability.StatusCode);
            var appointmentResponse = await client.PostAsJsonAsync("/api/v1/public/appointments", new { branchPublicId = branch.PublicId, servicePublicId = service.PublicId, scheduledStart = target, customerName = "Golden Customer", customerPhone = "+5511999999999", customerEmail = "golden.customer@test.local" }, ct); Assert.Equal(HttpStatusCode.Created, appointmentResponse.StatusCode); var appointment = await appointmentResponse.Content.ReadFromJsonAsync<CreatedAppointment>(ct); Assert.NotNull(appointment); Assert.Matches("^[a-f0-9]{32}$", appointment.PublicToken);
            var appointmentId = await db.Appointments.IgnoreQueryFilters().Where(x => x.PublicToken == appointment.PublicToken).Select(x => x.Id).SingleAsync(ct);
            var checkIn = await client.PostAsJsonAsync($"/api/v1/operations/appointments/{appointmentId}/check-in", new { }, ct); Assert.Equal(HttpStatusCode.OK, checkIn.StatusCode);
            var checkedIn = await client.GetFromJsonAsync<PublicAppointment>($"/api/v1/public/appointments/{appointment.PublicToken}", ct); Assert.NotNull(checkedIn); Assert.Equal("CheckedIn", checkedIn.Status); Assert.NotNull(checkedIn.QueueTicketToken);
            var appointmentTicket = await PublicTicketAsync(client, checkedIn.QueueTicketToken!, ct); Assert.Equal("Waiting", appointmentTicket.Status);
        }
        finally
        {
            if (organizationId != Guid.Empty) await CleanupAsync(db, organizationId, ct);
        }
    }

    private static async Task<IssuedTicket> IssueAsync(HttpClient client, string publicId, CancellationToken ct) { var response = await client.PostAsJsonAsync($"/api/v1/public/queues/{publicId}/tickets", new { priority = "Normal" }, ct); Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await response.Content.ReadFromJsonAsync<IssuedTicket>(ct))!; }
    private static async Task<PublicTicket> PublicTicketAsync(HttpClient client, string token, CancellationToken ct) { var response = await client.GetAsync($"/api/v1/public/tickets/{token}", ct); Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await response.Content.ReadFromJsonAsync<PublicTicket>(ct))!; }
    private static async Task CleanupAsync(ApplicationDbContext db, Guid organizationId, CancellationToken ct)
    {
        await db.OutboxMessages.IgnoreQueryFilters().Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct); await db.Notifications.IgnoreQueryFilters().Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct); await db.AuditLogs.IgnoreQueryFilters().Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct); await db.QueueMetricSnapshots.IgnoreQueryFilters().Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
        await db.AppointmentStatusHistory.IgnoreQueryFilters().Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct); await db.Appointments.IgnoreQueryFilters().Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct); await db.ScheduleBlocks.IgnoreQueryFilters().Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct); await db.ServiceSchedules.IgnoreQueryFilters().Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct); await db.ServiceSchedulingSettings.IgnoreQueryFilters().Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
        await db.TicketEvents.IgnoreQueryFilters().Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct); await db.QueueTickets.IgnoreQueryFilters().Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct); await db.Queues.IgnoreQueryFilters().Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct); await db.QueueCounters.IgnoreQueryFilters().Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct); await db.Services.IgnoreQueryFilters().Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct); await db.Branches.IgnoreQueryFilters().Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
        await db.UserBranches.IgnoreQueryFilters().Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct); await db.RefreshTokens.IgnoreQueryFilters().Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct); await db.Subscriptions.IgnoreQueryFilters().Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct); await db.Users.IgnoreQueryFilters().Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct); await db.Organizations.Where(x => x.Id == organizationId).ExecuteDeleteAsync(ct);
    }

    private sealed record OrganizationCreated(Guid OrganizationId, Guid UserId);
    private sealed record TokenPair(string AccessToken, string RefreshToken);
    private sealed record BranchResponse(Guid Id, string PublicId);
    private sealed record IdResponse(Guid Id, string PublicId);
    private sealed record QueueResponse(Guid Id, string PublicId);
    private sealed record IssuedTicket(Guid Id, string TicketNumber, string CustomerPublicToken, string Status);
    private sealed record TicketOperationResponse(Guid Id, string TicketNumber, string Status);
    private sealed record PublicTicket(string TicketNumber, string Status, int Position, int TicketsAhead, string? CounterName);
    private sealed record CreatedAppointment(string PublicToken, string Status);
    private sealed record PublicAppointment(string PublicToken, string Status, string? QueueTicketToken);
}
