using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QueueFlow.Application.Abstractions.Clock;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;
using QueueFlow.Infrastructure.Persistence;
using QueueFlow.Application.Observability;

namespace QueueFlow.Infrastructure.Jobs;

internal sealed partial class QueueMetricsJob(IServiceScopeFactory scopes, ILogger<QueueMetricsJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken)) try
        {
            await using var scope = scopes.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            var queues = await db.Queues.IgnoreQueryFilters().AsNoTracking().Where(x => x.IsActive).Select(x => new { x.Id, x.OrganizationId }).ToListAsync(stoppingToken);
            long totalWaiting = 0;
            foreach (var queue in queues)
            {
                var waiting = await db.QueueTickets.IgnoreQueryFilters().CountAsync(x => x.OrganizationId == queue.OrganizationId && x.QueueId == queue.Id && x.Status == TicketStatus.Waiting, stoppingToken);
                totalWaiting += waiting;
                var attendants = await db.QueueTickets.IgnoreQueryFilters().Where(x => x.OrganizationId == queue.OrganizationId && x.QueueId == queue.Id && x.AttendantUserId != null && (x.Status == TicketStatus.Called || x.Status == TicketStatus.InService)).Select(x => x.AttendantUserId).Distinct().CountAsync(stoppingToken);
                db.QueueMetricSnapshots.Add(new QueueMetricSnapshot(Guid.NewGuid(), queue.OrganizationId, queue.Id, waiting, attendants, clock.UtcNow));
            }
            QueueFlowTelemetry.ObserveQueues(queues.Count, totalWaiting);
            await db.SaveChangesAsync(stoppingToken);
            QueueFlowTelemetry.RecordJobSuccess("queue_metrics");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception exception) { Failed(logger, exception); }
    }
    [LoggerMessage(LogLevel.Error, "Queue metrics job failed and will retry on the next cycle.")]
    private static partial void Failed(ILogger logger, Exception exception);
}
