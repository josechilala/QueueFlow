using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using QueueFlow.Application.Abstractions.Realtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QueueFlow.Domain.Enums;
using QueueFlow.Infrastructure.Persistence;
using System.Text.Json;

namespace QueueFlow.Infrastructure.Realtime;

internal sealed class SignalRQueueRealtimeNotifier(IHubContext<QueueHub> hub, ILogger<SignalRQueueRealtimeNotifier> logger, IServiceScopeFactory scopes) : IQueueRealtimeNotifier
{
    private static readonly Action<ILogger, string, Exception?> QueueDeliveryFailed = LoggerMessage.Define<string>(LogLevel.Warning, new EventId(2001, nameof(QueueDeliveryFailed)), "SignalR queue event {EventName} could not be delivered.");
    private static readonly Action<ILogger, string, Exception?> TicketDeliveryFailed = LoggerMessage.Define<string>(LogLevel.Warning, new EventId(2002, nameof(TicketDeliveryFailed)), "SignalR ticket event {EventName} could not be delivered.");
    public async Task QueueEventAsync(string queuePublicId, string eventName, object payload, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var queue = await db.Queues.IgnoreQueryFilters().AsNoTracking().Where(x => x.PublicId == queuePublicId).Select(x => new { x.Id, x.OrganizationId }).SingleOrDefaultAsync(cancellationToken);
            var group = hub.Clients.Group(QueueHub.QueueGroup(queue?.Id.ToString() ?? queuePublicId));
            if (queue is not null)
            {
                var waitingCount = await db.QueueTickets.IgnoreQueryFilters().CountAsync(x => x.OrganizationId == queue.OrganizationId && x.QueueId == queue.Id && x.Status == TicketStatus.Waiting, cancellationToken);
                await group.SendAsync("QueueUpdated", new { queueId = queue.Id, waitingCount, timestamp = DateTimeOffset.UtcNow }, cancellationToken);
                if (eventName is "ticket.called" or "ticket.recalled")
                {
                    var json = JsonSerializer.SerializeToElement(payload);
                    var idProperty = json.EnumerateObject().FirstOrDefault(x => x.Name.Equals("ticketId", StringComparison.OrdinalIgnoreCase) || x.Name.Equals("id", StringComparison.OrdinalIgnoreCase));
                    if (idProperty.Value.ValueKind == JsonValueKind.String && idProperty.Value.TryGetGuid(out var ticketId))
                    {
                        var ticket = await db.QueueTickets.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Id == ticketId && x.QueueId == queue.Id, cancellationToken);
                        if (ticket?.Status is TicketStatus.Called or TicketStatus.InService)
                        {
                            var counterName = await db.QueueCounters.IgnoreQueryFilters().Where(x => x.Id == ticket.CounterId && x.OrganizationId == queue.OrganizationId).Select(x => x.Name).SingleOrDefaultAsync(cancellationToken);
                            var call = new { queueId = queue.Id, ticketId = ticket.Id, id = ticket.Id, status = ticket.Status.ToString(), ticket.TicketNumber, ticket.CounterId, counterName, calledAt = ticket.CalledAt };
                            await group.SendAsync("TicketCalled", call, cancellationToken);
                            // Whitelist the public payload; never broadcast the private ticket token.
                            payload = call;
                        }
                        else return; // A delayed outbox call must not resurrect an old call.
                    }
                }
                else
                {
                    payload = JsonSerializer.SerializeToElement(payload).EnumerateObject()
                        .Where(x => !x.Name.Contains("token", StringComparison.OrdinalIgnoreCase))
                        .ToDictionary(x => JsonNamingPolicy.CamelCase.ConvertName(x.Name), x => x.Value.Clone());
                }
            }
            await group.SendAsync(eventName, payload, cancellationToken);
            await hub.Clients.All.SendAsync("queue.updated", new { queuePublicId, sourceEvent = eventName }, cancellationToken);
        }
        catch (Exception exception) { QueueDeliveryFailed(logger, eventName, exception); }
    }

    public async Task TicketEventAsync(string ticketPublicToken, string eventName, object payload, CancellationToken cancellationToken)
    {
        try { await hub.Clients.Group(QueueHub.TicketGroup(ticketPublicToken)).SendAsync(eventName, payload, cancellationToken); }
        catch (Exception exception) { TicketDeliveryFailed(logger, eventName, exception); }
    }
}
