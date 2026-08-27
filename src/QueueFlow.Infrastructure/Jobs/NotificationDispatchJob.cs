using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QueueFlow.Application.Abstractions.Clock;
using QueueFlow.Application.Abstractions.Notifications;
using QueueFlow.Infrastructure.Persistence;
using QueueFlow.Domain.Enums;
using QueueFlow.Application.Observability;

namespace QueueFlow.Infrastructure.Jobs;

internal sealed class NotificationDispatchJob(IServiceScopeFactory scopes) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await using var scope = scopes.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); var sender = scope.ServiceProvider.GetRequiredService<INotificationSender>(); var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            var pending = await db.Notifications.IgnoreQueryFilters().Where(x => x.Status != NotificationStatus.Sent && x.NextAttemptAt <= clock.UtcNow).Take(50).ToListAsync(stoppingToken);
            foreach (var item in pending) { try { await sender.SendAsync(item, stoppingToken); item.MarkSent(clock.UtcNow); } catch (Exception) { QueueFlowTelemetry.NotificationFailures.Add(1); item.MarkFailed(clock.UtcNow); } }
            await db.SaveChangesAsync(stoppingToken);
        }
    }
}
