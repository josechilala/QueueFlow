namespace QueueFlow.Application.Abstractions.Realtime;

public interface IQueueRealtimeNotifier
{
    Task QueueChangedAsync(string queuePublicId, string eventName, object payload, CancellationToken cancellationToken);
}
