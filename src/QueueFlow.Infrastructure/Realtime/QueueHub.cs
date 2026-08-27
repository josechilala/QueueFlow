using Microsoft.AspNetCore.SignalR;

namespace QueueFlow.Infrastructure.Realtime;

public sealed class QueueHub : Hub
{
    public Task JoinQueueGroup(string queuePublicId) => IsSecureId(queuePublicId) ? Groups.AddToGroupAsync(Context.ConnectionId, QueueGroup(queuePublicId)) : Task.CompletedTask;
    public Task LeaveQueueGroup(string queuePublicId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, QueueGroup(queuePublicId));
    public Task JoinTicketGroup(string publicToken) => IsSecureId(publicToken) ? Groups.AddToGroupAsync(Context.ConnectionId, TicketGroup(publicToken)) : Task.CompletedTask;
    public Task LeaveTicketGroup(string publicToken) => Groups.RemoveFromGroupAsync(Context.ConnectionId, TicketGroup(publicToken));
    internal static string QueueGroup(string publicId) => $"queue:{publicId}";
    internal static string TicketGroup(string token) => $"ticket:{token}";
    private static bool IsSecureId(string value) => value.Length == 32 && value.All(Uri.IsHexDigit);
}
