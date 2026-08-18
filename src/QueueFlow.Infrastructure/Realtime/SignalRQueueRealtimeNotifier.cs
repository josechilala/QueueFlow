using Microsoft.AspNetCore.SignalR;
using QueueFlow.Application.Abstractions.Realtime;

namespace QueueFlow.Infrastructure.Realtime;

internal sealed class SignalRQueueRealtimeNotifier(IHubContext<QueueHub> hub) : IQueueRealtimeNotifier
{
    public Task QueueChangedAsync(string queuePublicId, string eventName, object payload, CancellationToken cancellationToken) => hub.Clients.Group(QueueHub.Group(queuePublicId)).SendAsync(eventName, payload, cancellationToken);
}
