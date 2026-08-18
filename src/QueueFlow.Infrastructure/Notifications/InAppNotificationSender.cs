using Microsoft.Extensions.Logging;
using QueueFlow.Application.Abstractions.Notifications;
using QueueFlow.Domain.Entities;

namespace QueueFlow.Infrastructure.Notifications;

internal sealed partial class InAppNotificationSender(ILogger<InAppNotificationSender> logger) : INotificationSender
{
    public Task SendAsync(Notification notification, CancellationToken cancellationToken) { LogDispatched(logger, notification.Id, notification.OrganizationId); return Task.CompletedTask; }
    [LoggerMessage(LogLevel.Information, "Dispatched in-app notification {NotificationId} for tenant {OrganizationId}")]
    private static partial void LogDispatched(ILogger logger, Guid notificationId, Guid organizationId);
}
