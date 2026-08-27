using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QueueFlow.Application.Abstractions.Clock;
using QueueFlow.Application.Abstractions.Notifications;
using QueueFlow.Application.Abstractions.Realtime;
using QueueFlow.Infrastructure.Persistence;
using QueueFlow.Application.Observability;

namespace QueueFlow.Infrastructure.Jobs;

internal sealed partial class OutboxProcessor(IServiceScopeFactory scopes, ILogger<OutboxProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await ProcessBatchAsync(stoppingToken); QueueFlowTelemetry.RecordJobSuccess("outbox"); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception) { BatchFailed(logger, exception); }
        }
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        for (var index = 0; index < 50; index++)
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var message = await db.OutboxMessages.FromSqlInterpolated($"SELECT * FROM \"OutboxMessages\" WHERE \"ProcessedAt\" IS NULL AND \"NextAttemptAt\" <= {clock.UtcNow} ORDER BY \"CreatedAt\" FOR UPDATE SKIP LOCKED LIMIT 1").IgnoreQueryFilters().SingleOrDefaultAsync(ct);
            if (message is null) { await transaction.RollbackAsync(ct); break; }
            try
            {
                if (message.Type == "notification.dispatch")
                {
                    var notificationId = JsonDocument.Parse(message.Payload).RootElement.GetProperty("notificationId").GetGuid();
                    var notification = await db.Notifications.IgnoreQueryFilters().SingleAsync(x => x.Id == notificationId && x.OrganizationId == message.OrganizationId, ct);
                    await scope.ServiceProvider.GetRequiredService<INotificationSender>().SendAsync(notification, ct);
                    notification.MarkSent(clock.UtcNow);
                }
                else if (message.Type == "ticket.realtime")
                {
                    using var document = JsonDocument.Parse(message.Payload); var root = document.RootElement;
                    var queueId = root.GetProperty("QueueId").GetGuid(); var token = root.GetProperty("ticketToken").GetString()!; var eventName = root.GetProperty("eventName").GetString()!;
                    var queuePublicId = await db.Queues.IgnoreQueryFilters().Where(x => x.OrganizationId == message.OrganizationId && x.Id == queueId).Select(x => x.PublicId).SingleAsync(ct);
                    var payload = JsonSerializer.Deserialize<object>(message.Payload)!; var realtime = scope.ServiceProvider.GetRequiredService<IQueueRealtimeNotifier>();
                    await realtime.TicketEventAsync(token, eventName, payload, ct); await realtime.QueueEventAsync(queuePublicId, eventName, payload, ct);
                }
                else if (message.Type == "appointment.realtime")
                {
                    using var document = JsonDocument.Parse(message.Payload); var root = document.RootElement;
                    var serviceId = root.GetProperty("ServiceId").GetGuid(); var token = root.GetProperty("appointmentToken").GetString()!; var eventName = root.GetProperty("eventName").GetString()!;
                    var servicePublicId = await db.Services.IgnoreQueryFilters().Where(x => x.OrganizationId == message.OrganizationId && x.Id == serviceId).Select(x => x.PublicId).SingleAsync(ct);
                    var payload = JsonSerializer.Deserialize<object>(message.Payload)!; var realtime = scope.ServiceProvider.GetRequiredService<IQueueRealtimeNotifier>();
                    await realtime.TicketEventAsync(token, eventName, payload, ct); await realtime.QueueEventAsync(servicePublicId, eventName, payload, ct);
                }
                else throw new InvalidOperationException($"Unsupported outbox type '{message.Type}'.");
                message.MarkProcessed(clock.UtcNow);
            }
            catch (Exception exception)
            {
                if (message.Type == "notification.dispatch") QueueFlowTelemetry.NotificationFailures.Add(1);
                message.MarkFailed(exception.GetType().Name, clock.UtcNow);
                var notificationId = TryGetNotificationId(message.Payload);
                if (notificationId is Guid id)
                {
                    var notification = await db.Notifications.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == id && x.OrganizationId == message.OrganizationId, ct);
                    notification?.MarkFailed(clock.UtcNow);
                }
            }
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        }
    }

    private static Guid? TryGetNotificationId(string payload) { try { return JsonDocument.Parse(payload).RootElement.GetProperty("notificationId").GetGuid(); } catch (Exception) { return null; } }
    [LoggerMessage(LogLevel.Error, "Outbox batch failed and will be retried.")]
    private static partial void BatchFailed(ILogger logger, Exception exception);
}
