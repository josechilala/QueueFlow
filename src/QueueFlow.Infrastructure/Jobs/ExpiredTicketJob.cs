using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QueueFlow.Application.Abstractions.Clock;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;
using QueueFlow.Infrastructure.Persistence;
using QueueFlow.Application.Observability;

namespace QueueFlow.Infrastructure.Jobs;

internal sealed partial class ExpiredTicketJob(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<ExpiredTicketJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (await timer.WaitForNextTickAsync(stoppingToken)) try
        {
            await using var scope = scopes.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            var hours = Math.Max(1, configuration.GetValue("Jobs:ExpiredTicketHours", 8)); var cutoff = clock.UtcNow.AddHours(-hours);
            var expired = await db.QueueTickets.IgnoreQueryFilters().Where(x => x.Status == TicketStatus.Waiting && x.IssuedAt < cutoff).Take(200).ToListAsync(stoppingToken);
            foreach (var ticket in expired) { ticket.Cancel(clock.UtcNow); db.TicketEvents.Add(new TicketEvent(Guid.NewGuid(), ticket.OrganizationId, ticket.Id, TicketStatus.Cancelled, "Expired", clock.UtcNow)); }
            await db.SaveChangesAsync(stoppingToken);
            QueueFlowTelemetry.RecordJobSuccess("expired_tickets");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception exception) { Failed(logger, exception); }
    }
    [LoggerMessage(LogLevel.Error, "Expired ticket job failed and will retry on the next cycle.")]
    private static partial void Failed(ILogger logger, Exception exception);
}
