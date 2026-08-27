using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using QueueFlow.Application.Abstractions.Realtime;

namespace QueueFlow.Infrastructure.Realtime;

internal sealed class SignalRQueueRealtimeNotifier(IHubContext<QueueHub> hub, Microsoft.Extensions.Logging.ILogger<SignalRQueueRealtimeNotifier> logger) : IQueueRealtimeNotifier
{
    private static readonly Action<ILogger, string, Exception?> QueueDeliveryFailed = LoggerMessage.Define<string>(LogLevel.Warning, new EventId(2001, nameof(QueueDeliveryFailed)), "SignalR queue event {EventName} could not be delivered.");
    private static readonly Action<ILogger, string, Exception?> TicketDeliveryFailed = LoggerMessage.Define<string>(LogLevel.Warning, new EventId(2002, nameof(TicketDeliveryFailed)), "SignalR ticket event {EventName} could not be delivered.");
    public async Task QueueEventAsync(string queuePublicId, string eventName, object payload, CancellationToken cancellationToken)
    {
        try
        {
            await hub.Clients.Group(QueueHub.QueueGroup(queuePublicId)).SendAsync(eventName, payload, cancellationToken);
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
