using QueueFlow.Domain.Entities;

namespace QueueFlow.Domain.Tests.Entities;

public sealed class OutboxMessageTests
{
    [Fact]
    public void FailureIsScheduledAndSuccessfulProcessingIsRecorded()
    {
        var now = DateTimeOffset.UtcNow;
        var message = new OutboxMessage(Guid.NewGuid(), Guid.NewGuid(), "notification.dispatch", "{}", now);
        message.MarkFailed("Temporary error", now);
        Assert.Equal(1, message.Attempts);
        Assert.True(message.NextAttemptAt > now);
        Assert.Null(message.ProcessedAt);
        message.MarkProcessed(now.AddMinutes(1));
        Assert.Equal(now.AddMinutes(1), message.ProcessedAt);
        Assert.Null(message.LastError);
    }
}
