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

internal sealed partial class AppointmentNoShowJob(IServiceScopeFactory scopes, ILogger<AppointmentNoShowJob> logger) : BackgroundService
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
            SELECT appointment.*, appointment.xmin FROM "Appointments" appointment
            JOIN "ServiceSchedulingSettings" settings ON settings."OrganizationId" = appointment."OrganizationId" AND settings."ServiceId" = appointment."ServiceId"
            WHERE appointment."Status" = {{(int)AppointmentStatus.Confirmed}}
              AND appointment."ScheduledStart" + make_interval(mins => settings."LateToleranceMinutes") < {{now}}
            ORDER BY appointment."ScheduledStart" FOR UPDATE OF appointment SKIP LOCKED LIMIT 1
            """).IgnoreQueryFilters().SingleOrDefaultAsync(cancellationToken);
        if (appointment is null) { await transaction.RollbackAsync(cancellationToken); return false; }
        var previous = appointment.Status; appointment.MarkNoShow(now); db.AppointmentStatusHistory.Add(new(Guid.NewGuid(), appointment.OrganizationId, appointment.Id, previous, appointment.Status, null, "No-show automático após tolerância", now));
        var notification = new Notification(Guid.NewGuid(), appointment.OrganizationId, appointment.PublicToken, "Seu agendamento foi marcado como não comparecimento após o término da tolerância.", now); db.Notifications.Add(notification);
        db.OutboxMessages.Add(new OutboxMessage(Guid.NewGuid(), appointment.OrganizationId, "notification.dispatch", JsonSerializer.Serialize(new { notificationId = notification.Id }), now)); db.OutboxMessages.Add(new OutboxMessage(Guid.NewGuid(), appointment.OrganizationId, "appointment.realtime", JsonSerializer.Serialize(new { eventName = "appointment.no-show", appointmentToken = appointment.PublicToken, appointmentId = appointment.Id, appointment.ServiceId, status = appointment.Status.ToString(), appointment.ScheduledStart }), now));
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return true;
    }
    [LoggerMessage(LogLevel.Error, "Appointment no-show job failed.")] private static partial void Failed(ILogger logger, Exception exception);
}
