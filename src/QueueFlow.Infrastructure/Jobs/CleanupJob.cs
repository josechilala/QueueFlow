using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QueueFlow.Application.Abstractions.Clock;
using QueueFlow.Domain.Enums;
using QueueFlow.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using QueueFlow.Application.Observability;

namespace QueueFlow.Infrastructure.Jobs;

internal sealed partial class CleanupJob(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<CleanupJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        while (await timer.WaitForNextTickAsync(stoppingToken)) try
        {
            await using var scope = scopes.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;
            await db.OutboxMessages.IgnoreQueryFilters().Where(x => x.ProcessedAt != null && x.ProcessedAt < now.AddDays(-7)).ExecuteDeleteAsync(stoppingToken);
            await db.Notifications.IgnoreQueryFilters().Where(x => x.Status == NotificationStatus.Sent && x.ReadAt != null && x.ReadAt < now.AddDays(-30)).ExecuteDeleteAsync(stoppingToken);
            await db.Notifications.IgnoreQueryFilters().Where(x => x.CreatedAt < now.AddDays(-90)).ExecuteDeleteAsync(stoppingToken);
            await db.QueueMetricSnapshots.IgnoreQueryFilters().Where(x => x.CreatedAt < now.AddDays(-90)).ExecuteDeleteAsync(stoppingToken);
            await db.RefreshTokens.IgnoreQueryFilters().Where(x => x.ExpiresAt < now || x.RevokedAt < now.AddDays(-30)).ExecuteDeleteAsync(stoppingToken);
            var piiDays = Math.Max(1, configuration.GetValue("Privacy:TicketPiiRetentionDays", 90));
            var closed = await db.QueueTickets.IgnoreQueryFilters().Where(x => x.AnonymizedAt == null && (x.Status == TicketStatus.Completed || x.Status == TicketStatus.Cancelled || x.Status == TicketStatus.NoShow) && x.UpdatedAt < now.AddDays(-piiDays)).Take(500).ToListAsync(stoppingToken);
            foreach (var ticket in closed) ticket.Anonymize(Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(), now);
            var appointmentPiiDays = Math.Max(1, configuration.GetValue("Privacy:AppointmentPiiRetentionDays", 365));
            var closedAppointments = await db.Appointments.IgnoreQueryFilters().Where(x => x.AnonymizedAt == null && (x.Status == AppointmentStatus.Completed || x.Status == AppointmentStatus.Cancelled || x.Status == AppointmentStatus.NoShow || x.Status == AppointmentStatus.Rescheduled) && x.UpdatedAt < now.AddDays(-appointmentPiiDays)).Take(500).ToListAsync(stoppingToken);
            foreach (var appointment in closedAppointments) appointment.Anonymize(Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(), now);
            var auditDays = Math.Max(365, configuration.GetValue("Privacy:AuditRetentionDays", 1825));
            await db.AuditLogs.IgnoreQueryFilters().Where(x => x.CreatedAt < now.AddDays(-auditDays)).ExecuteDeleteAsync(stoppingToken);
            await db.SaveChangesAsync(stoppingToken);
            QueueFlowTelemetry.RecordJobSuccess("cleanup");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception exception) { Failed(logger, exception); }
    }
    [LoggerMessage(LogLevel.Error, "Cleanup job failed and will retry on the next cycle.")]
    private static partial void Failed(ILogger logger, Exception exception);
}
