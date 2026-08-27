namespace QueueFlow.Application.Abstractions.Realtime;

public interface IQueueRealtimeNotifier
{
    Task QueueEventAsync(string queuePublicId, string eventName, object payload, CancellationToken cancellationToken);
    Task TicketEventAsync(string ticketPublicToken, string eventName, object payload, CancellationToken cancellationToken);
}
