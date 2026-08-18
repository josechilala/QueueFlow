namespace QueueFlow.Application.Abstractions.Clock;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
