using QueueFlow.Domain.Common;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;

namespace QueueFlow.Domain.Tests.Entities;

public sealed class QueueTests
{
    [Fact]
    public void DraftQueueCanBeOpenedPausedAndResumed()
    {
        var queue = Create();
        var now = DateTimeOffset.UtcNow;

        queue.Open(now);
        queue.Pause(now.AddMinutes(1));
        queue.Open(now.AddMinutes(2));

        Assert.Equal(QueueStatus.Open, queue.Status);
    }

    [Fact]
    public void PausedOrClosedQueueDoesNotReserveSequence()
    {
        var queue = Create();
        var now = DateTimeOffset.UtcNow;
        queue.Open(now);
        queue.Pause(now);

        Assert.Throws<DomainException>(() => queue.ReserveSequence());

        queue.Close(now);
        Assert.Throws<DomainException>(() => queue.ReserveSequence());
    }

    [Fact]
    public void ClosedQueueCannotBeReopened()
    {
        var queue = Create();
        var now = DateTimeOffset.UtcNow;
        queue.Close(now);

        Assert.Throws<DomainException>(() => queue.Open(now));
    }

    [Fact]
    public void OpenQueueReservesIncreasingSequences()
    {
        var queue = Create();
        queue.Open(DateTimeOffset.UtcNow);

        Assert.Equal(1, queue.ReserveSequence());
        Assert.Equal(2, queue.ReserveSequence());
    }

    [Fact]
    public void PublicIdIsNonSequentialHexIdentifier()
    {
        var first = Create();
        var second = Create();

        Assert.Matches("^[a-f0-9]{32}$", first.PublicId);
        Assert.NotEqual(first.PublicId, second.PublicId);
        Assert.False(long.TryParse(first.PublicId, out _));
    }

    private static QueueFlow.Domain.Entities.Queue Create() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Main", 50, DateTimeOffset.UtcNow);
}
