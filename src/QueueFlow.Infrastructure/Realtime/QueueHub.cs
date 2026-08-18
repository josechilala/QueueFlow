using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace QueueFlow.Infrastructure.Realtime;

[Authorize]
public sealed class QueueHub : Hub
{
    public Task JoinQueueGroup(string queuePublicId) => Groups.AddToGroupAsync(Context.ConnectionId, Group(queuePublicId));
    public Task LeaveQueueGroup(string queuePublicId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(queuePublicId));
    internal static string Group(string publicId) => $"queue:{publicId}";
}
