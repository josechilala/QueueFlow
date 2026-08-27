using Microsoft.Extensions.Logging;
using QueueFlow.Application.Abstractions.Notifications;
using QueueFlow.Domain.Entities;
using QueueFlow.Application.Abstractions.Realtime;

namespace QueueFlow.Infrastructure.Notifications;

internal sealed partial class InAppNotificationSender(ILogger<InAppNotificationSender> logger, IQueueRealtimeNotifier realtime) : INotificationSender
{
    public async Task SendAsync(Notification notification, CancellationToken cancellationToken)
    {
        if (notification.Channel != QueueFlow.Domain.Enums.NotificationChannel.InApp || string.IsNullOrWhiteSpace(notification.RecipientPublicToken)) throw new InvalidOperationException("The in-app sender only accepts addressed in-app notifications.");
        await realtime.TicketEventAsync(notification.RecipientPublicToken, "notification.created", new { notification.Id, notification.Message, notification.CreatedAt }, cancellationToken);
        LogDispatched(logger, notification.Id, notification.OrganizationId);
    }
    [LoggerMessage(LogLevel.Information, "Dispatched in-app notification {NotificationId} for tenant {OrganizationId}")]
    private static partial void LogDispatched(ILogger logger, Guid notificationId, Guid organizationId);
}
