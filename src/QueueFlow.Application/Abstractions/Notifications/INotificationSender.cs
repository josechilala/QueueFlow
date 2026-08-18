using QueueFlow.Domain.Entities;

namespace QueueFlow.Application.Abstractions.Notifications;

public interface INotificationSender
{
    Task SendAsync(Notification notification, CancellationToken cancellationToken);
}
