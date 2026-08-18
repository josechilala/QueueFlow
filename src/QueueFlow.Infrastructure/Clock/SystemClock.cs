using QueueFlow.Application.Abstractions.Clock;

namespace QueueFlow.Infrastructure.Clock;

internal sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
