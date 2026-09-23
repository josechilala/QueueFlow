using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;
using System.Globalization;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;
using QueueFlow.IntegrationTests.Platform;

namespace QueueFlow.IntegrationTests.Appointments;

public sealed class PublicAppointmentDiagnosticsTests(PlatformTestFactory factory) : IClassFixture<PlatformTestFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreateRescheduleAndPersistenceFailureRetainResponsesAndLogOnlySafeMetadata()
    {
        await using var db = await PlatformMigrationTests.CreateDatabaseAsync();
        await db.Database.MigrateAsync(Ct);
        var logs = new DiagnosticLogs();
        using var isolated = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:QueueFlowDatabase", db.Database.GetConnectionString());
            builder.ConfigureServices(services => services.AddSingleton<ILogEventSink>(logs));
        });
        var now = DateTimeOffset.UtcNow;
        var org = new Organization(Guid.NewGuid(), "Diagnostic organization", $"diagnostic-{Guid.NewGuid():N}", "America/Sao_Paulo", now);
        var branch = new Branch(Guid.NewGuid(), org.Id, "Branch", "America/Sao_Paulo", now);
        var service = new Service(Guid.NewGuid(), org.Id, branch.Id, "Consultation", null, "A", 30, now);
        service.SetAttendanceMode(ServiceAttendanceMode.AppointmentOnly, now);
        var settings = new ServiceSchedulingSettings(Guid.NewGuid(), org.Id, branch.Id, service.Id, now);
        settings.Configure(30, 1, 0, 30, 10, 60, 30, true, true, false, true, now);
        var start = new DateTimeOffset(now.UtcDateTime.Date.AddDays(2).AddHours(13), TimeSpan.Zero);
        var schedule = new ServiceSchedule(Guid.NewGuid(), org.Id, branch.Id, service.Id, start.DayOfWeek, new(10, 0), new(14, 0), now);
        db.AddRange(org, branch, service, settings, schedule);
        await db.SaveChangesAsync(Ct);
        using var client = isolated.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "appointment-diagnostic-test");
        object Payload(DateTimeOffset value) => new { branchPublicId = branch.PublicId, servicePublicId = service.PublicId, scheduledStart = value, customerName = "Private customer", customerEmail = "private@example.test", customerPhone = (string?)null };

        using var created = await client.PostAsJsonAsync("/api/v1/public/appointments", Payload(start), Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var token = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("publicToken").GetString()!;
        Assert.Equal(1, await db.Appointments.IgnoreQueryFilters().CountAsync(Ct));
        using var occupied = await client.PostAsJsonAsync("/api/v1/public/appointments", Payload(start), Ct);
        Assert.Equal(HttpStatusCode.Conflict, occupied.StatusCode);
        using var invalid = await client.PostAsJsonAsync("/api/v1/public/appointments", new { branchPublicId = branch.PublicId, servicePublicId = service.PublicId, scheduledStart = "invalid", customerName = "Private customer" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var missingFields = await client.PostAsJsonAsync("/api/v1/public/appointments", new { scheduledStart = start }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, missingFields.StatusCode);
        foreach (var invalidToken in new[] { "invalid-token", new string('a', 32) })
        {
            using var response = await client.PostAsJsonAsync($"/api/v1/public/appointments/{invalidToken}/reschedule", new { scheduledStart = start.AddMinutes(30) }, Ct);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }
        using var rescheduled = await client.PostAsJsonAsync($"/api/v1/public/appointments/{token}/reschedule", new { scheduledStart = start.AddMinutes(30) }, Ct);
        Assert.Equal(HttpStatusCode.OK, rescheduled.StatusCode);
        var replacementToken = (await rescheduled.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("publicToken").GetString()!;
        var replacement = await db.Appointments.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.PublicToken == replacementToken, Ct);
        Assert.Equal(start.AddMinutes(30), replacement.ScheduledStart);
        Assert.Equal(org.Id, replacement.OrganizationId);
        Assert.Equal(AppointmentStatus.Rescheduled, (await db.Appointments.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.PublicToken == token, Ct)).Status);
        Assert.Equal(2, await db.OutboxMessages.IgnoreQueryFilters().CountAsync(x => x.Type == "appointment.receipt", Ct));

        // Fail the actual PostgreSQL INSERT, only in the disposable test database.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION fail_test_appointment() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'private@example.test secret-token'; END; $$;
            CREATE TRIGGER fail_test_appointment BEFORE INSERT ON "Appointments"
            FOR EACH ROW EXECUTE FUNCTION fail_test_appointment();
            """, Ct);
        var outboxCount = await db.OutboxMessages.IgnoreQueryFilters().CountAsync(Ct);
        using var failed = await client.PostAsJsonAsync("/api/v1/public/appointments", Payload(start.AddHours(1)), Ct);
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.Equal("appointment-diagnostic-test", Assert.Single(failed.Headers.GetValues("X-Correlation-ID")));
        Assert.DoesNotContain("private@example.test", await failed.Content.ReadAsStringAsync(Ct));
        Assert.Equal(2, await db.Appointments.IgnoreQueryFilters().CountAsync(Ct));
        Assert.Equal(outboxCount, await db.OutboxMessages.IgnoreQueryFilters().CountAsync(Ct));
        using var failedReschedule = await client.PostAsJsonAsync($"/api/v1/public/appointments/{replacementToken}/reschedule", new { scheduledStart = start.AddHours(1) }, Ct);
        Assert.Equal(HttpStatusCode.InternalServerError, failedReschedule.StatusCode);
        Assert.Equal(2, await db.Appointments.IgnoreQueryFilters().CountAsync(Ct));
        Assert.Equal(AppointmentStatus.Confirmed, (await db.Appointments.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.PublicToken == replacementToken, Ct)).Status);
        Assert.Equal(outboxCount, await db.OutboxMessages.IgnoreQueryFilters().CountAsync(Ct));
        var failure = Assert.Single(logs.Entries, x => x.Contains("Appointment operation failed. Operation: create", StringComparison.Ordinal));
        Assert.Contains("Operation: create Stage: save_changes", failure);
        Assert.Contains("SqlState: P0001", failure);
        Assert.Contains(org.Id.ToString(), failure);
        Assert.Contains(service.Id.ToString(), failure);
        Assert.Contains(logs.Entries, x => x.Contains("Endpoint: api/v1/public/appointments", StringComparison.Ordinal));
        Assert.Contains(logs.Entries, x => x.Contains("Endpoint: api/v1/public/appointments/{publicToken}/reschedule", StringComparison.Ordinal));
        Assert.Contains(logs.Entries, x => x.Contains("Operation: reschedule Stage: save_changes", StringComparison.Ordinal));
        Assert.Contains(logs.Entries, x => x.Contains("Appointment transaction committed. Operation: reschedule", StringComparison.Ordinal));
        var diagnosticText = string.Join('\n', logs.Entries);
        Assert.DoesNotContain("private@example.test", diagnosticText);
        Assert.DoesNotContain("Private customer", diagnosticText);
        Assert.DoesNotContain("secret-token", diagnosticText);
        Assert.DoesNotContain(token, diagnosticText);
        Assert.DoesNotContain(replacementToken, diagnosticText);

        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_test_appointment ON \"Appointments\"; DROP FUNCTION fail_test_appointment();", Ct);
        using var recovered = await client.PostAsJsonAsync("/api/v1/public/appointments", Payload(start.AddHours(1)), Ct);
        Assert.Equal(HttpStatusCode.Created, recovered.StatusCode);
        Assert.Equal(3, await db.Appointments.IgnoreQueryFilters().CountAsync(Ct));
        var concurrent = await Task.WhenAll(
            client.PostAsJsonAsync("/api/v1/public/appointments", Payload(start.AddMinutes(90)), Ct),
            client.PostAsJsonAsync("/api/v1/public/appointments", Payload(start.AddMinutes(90)), Ct));
        try
        {
            Assert.Single(concurrent, x => x.StatusCode == HttpStatusCode.Created);
            Assert.Single(concurrent, x => x.StatusCode == HttpStatusCode.Conflict);
            Assert.Equal(4, await db.Appointments.IgnoreQueryFilters().CountAsync(Ct));
        }
        finally { foreach (var response in concurrent) response.Dispose(); }
    }

    private sealed class DiagnosticLogs : ILogEventSink
    {
        public ConcurrentQueue<string> Entries { get; } = new();
        public void Emit(LogEvent logEvent)
        {
            if (logEvent.Properties.TryGetValue("SourceContext", out var source) && source is ScalarValue { Value: string category } &&
                (category.EndsWith("PostgresAppointmentOperations", StringComparison.Ordinal) || category.EndsWith("ExceptionHandlingMiddleware", StringComparison.Ordinal)))
            {
                Assert.Null(logEvent.Exception);
                Assert.Equal("appointment-diagnostic-test", ((ScalarValue)logEvent.Properties["CorrelationId"]).Value);
                Entries.Enqueue(logEvent.RenderMessage(CultureInfo.InvariantCulture).Replace("\"", "", StringComparison.Ordinal));
            }
        }
    }
}
