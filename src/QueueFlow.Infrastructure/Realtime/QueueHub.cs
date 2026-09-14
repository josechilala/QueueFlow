using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using QueueFlow.Infrastructure.Persistence;

namespace QueueFlow.Infrastructure.Realtime;

public sealed class QueueHub(ApplicationDbContext db) : Hub
{
    public async Task JoinQueueGroup(string queuePublicId)
    {
        var group = await ResolveQueueGroupAsync(queuePublicId);
        if (group is not null) await Groups.AddToGroupAsync(Context.ConnectionId, group);
    }
    public async Task LeaveQueueGroup(string queuePublicId)
    {
        var group = await ResolveQueueGroupAsync(queuePublicId);
        if (group is not null) await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
    }
    public async Task JoinTicketGroup(string publicToken)
    {
        if (!IsSecureId(publicToken)) return;
        await Groups.AddToGroupAsync(Context.ConnectionId, TicketGroup(publicToken));
        var queueId = await db.QueueTickets.IgnoreQueryFilters().Where(x => x.CustomerPublicToken == publicToken).Select(x => (Guid?)x.QueueId).SingleOrDefaultAsync(Context.ConnectionAborted);
        if (queueId is Guid id) await Groups.AddToGroupAsync(Context.ConnectionId, QueueGroup(id.ToString()));
    }
    public async Task LeaveTicketGroup(string publicToken)
    {
        if (!IsSecureId(publicToken)) return;
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, TicketGroup(publicToken));
        var queueId = await db.QueueTickets.IgnoreQueryFilters().Where(x => x.CustomerPublicToken == publicToken).Select(x => (Guid?)x.QueueId).SingleOrDefaultAsync(Context.ConnectionAborted);
        if (queueId is Guid id) await Groups.RemoveFromGroupAsync(Context.ConnectionId, QueueGroup(id.ToString()));
    }
    private async Task<string?> ResolveQueueGroupAsync(string publicId)
    {
        if (!IsSecureId(publicId)) return null;
        var id = await db.Queues.IgnoreQueryFilters().Where(x => x.PublicId == publicId).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(Context.ConnectionAborted);
        // Service subscriptions used by existing appointment clients retain their public group.
        return QueueGroup(id?.ToString() ?? publicId);
    }
    internal static string QueueGroup(string publicId) => $"queue:{publicId}";
    internal static string TicketGroup(string token) => $"ticket:{token}";
    private static bool IsSecureId(string? value) => value is { Length: 32 } && value.All(Uri.IsHexDigit);
}
