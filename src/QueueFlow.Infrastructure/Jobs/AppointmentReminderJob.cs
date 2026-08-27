using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QueueFlow.Application.Abstractions.Clock;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;
using QueueFlow.Infrastructure.Persistence;
using System.Text.Json;

namespace QueueFlow.Infrastructure.Jobs;

internal sealed partial class AppointmentReminderJob(IServiceScopeFactory scopes, ILogger<AppointmentReminderJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { for (var index = 0; index < 100 && await ProcessOneAsync(stoppingToken); index++) { } }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception) { Failed(logger, exception); }
        }
    }

    private async Task<bool> ProcessOneAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); var clock = scope.ServiceProvider.GetRequiredService<IClock>(); var now = clock.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var appointment = await db.Appointments.FromSqlInterpolated($$"""
            SELECT *, xmin FROM "Appointments"
            WHERE "Status" IN ({{(int)AppointmentStatus.Scheduled}}, {{(int)AppointmentStatus.Confirmed}})
              AND "ReminderStage" < 3
              AND "ScheduledStart" <= {{now}} + make_interval(mins => CASE "ReminderStage" WHEN 0 THEN 1440 WHEN 1 THEN 120 ELSE 30 END)
              AND "ScheduledStart" > {{now}}
            ORDER BY "ScheduledStart" FOR UPDATE SKIP LOCKED LIMIT 1
            """).IgnoreQueryFilters().SingleOrDefaultAsync(cancellationToken);
        if (appointment is null) { await transaction.RollbackAsync(cancellationToken); return false; }
        var offset = appointment.ReminderStage switch { 0 => 1440, 1 => 120, _ => 30 }; var stage = appointment.ReminderStage;
        var message = new Notification(Guid.NewGuid(), appointment.OrganizationId, appointment.PublicToken, $"Lembrete: seu agendamento começa em aproximadamente {FormatOffset(offset)}.", now);
        db.Notifications.Add(message); db.OutboxMessages.Add(new OutboxMessage(Guid.NewGuid(), appointment.OrganizationId, "notification.dispatch", JsonSerializer.Serialize(new { notificationId = message.Id }), now)); appointment.MarkReminderSent(stage, now);
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return true;
    }

    private static string FormatOffset(int minutes) => minutes >= 1440 ? "24 horas" : minutes >= 60 ? $"{minutes / 60} horas" : $"{minutes} minutos";
    [LoggerMessage(LogLevel.Error, "Appointment reminder job failed.")] private static partial void Failed(ILogger logger, Exception exception);
}
