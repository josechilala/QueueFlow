using Microsoft.EntityFrameworkCore;
using QueueFlow.Application.Abstractions.Persistence;

namespace QueueFlow.Application.Features.Auditing;

public sealed record AuditEntryDto(Guid Id, Guid? UserId, string? UserName, string Action, string ResourceType, Guid? ResourceId, string? Data, string CorrelationId, DateTimeOffset CreatedAt);
public sealed record AuditPage(IReadOnlyList<AuditEntryDto> Items, int Total, int Page, int PageSize);

public sealed class AuditQueryService(IApplicationDbContext db)
{
    public async Task<AuditPage> GetAsync(DateTimeOffset? from, DateTimeOffset? to, string? action, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.AuditLogs.AsNoTracking();
        if (from is not null) query = query.Where(x => x.CreatedAt >= from);
        if (to is not null) query = query.Where(x => x.CreatedAt < to);
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(x => x.Action == action.Trim());
        var total = await query.CountAsync(ct); var rows = await query.OrderByDescending(x => x.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var userIds = rows.Where(x => x.UserId != null).Select(x => x.UserId!.Value).Distinct().ToArray();
        var users = await db.Users.AsNoTracking().Where(x => userIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        return new AuditPage(rows.Select(x => new AuditEntryDto(x.Id, x.UserId, x.UserId is Guid id ? users.GetValueOrDefault(id, "Usuário removido") : null, x.Action, x.ResourceType, x.ResourceId, x.Data, x.CorrelationId, x.CreatedAt)).ToArray(), total, page, pageSize);
    }
}
