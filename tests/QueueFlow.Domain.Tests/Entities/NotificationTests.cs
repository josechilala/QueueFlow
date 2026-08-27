using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;

namespace QueueFlow.Domain.Tests.Entities;

public sealed class NotificationTests
{
    [Fact]
    public void InAppNotificationKeepsRecipientAndReadState()
    {
        var now = DateTimeOffset.UtcNow;
        var notification = new Notification(Guid.NewGuid(), Guid.NewGuid(), "public-ticket-token", "Falta 1 pessoa para sua vez", now);
        Assert.Equal(NotificationChannel.InApp, notification.Channel);
        Assert.Equal("public-ticket-token", notification.RecipientPublicToken);
        Assert.False(notification.ReadAt.HasValue);
        notification.MarkRead(now.AddMinutes(1));
        Assert.Equal(now.AddMinutes(1), notification.ReadAt);
    }

    [Fact]
    public void FailedNotificationSchedulesRetry()
    {
        var now = DateTimeOffset.UtcNow;
        var notification = new Notification(Guid.NewGuid(), Guid.NewGuid(), "public-ticket-token", "Mensagem", now);
        notification.MarkFailed(now);
        Assert.Equal(NotificationStatus.Failed, notification.Status);
        Assert.Equal(1, notification.Attempts);
        Assert.True(notification.NextAttemptAt > now);
    }
}
