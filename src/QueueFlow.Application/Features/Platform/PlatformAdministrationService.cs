using Microsoft.EntityFrameworkCore;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Abstractions.Persistence;
using QueueFlow.Domain.Enums;

namespace QueueFlow.Application.Features.Platform;

public sealed record PlatformDashboardDto(int TotalOrganizations, int ActiveOrganizations, int Trials, int ActiveSubscriptions, int PendingInvitations, int ExpiredInvitations);
public sealed record PlatformOrganizationDto(Guid Id, string Name, string Slug, string? OwnerName, string? OwnerEmail, DateTimeOffset CreatedAt, bool IsActive, string? Plan, SubscriptionStatus? SubscriptionStatus, DateTimeOffset? TrialEndsAt, int BranchCount, int UserCount);
public sealed record PlatformSubscriptionDto(Guid OrganizationId, string OrganizationName, string Plan, SubscriptionStatus Status, DateTimeOffset TrialEndsAt);
public sealed record PlatformAuditDto(Guid Id, Guid? PlatformUserId, string Action, string ResourceType, Guid? ResourceId, DateTimeOffset CreatedAt);

public sealed class PlatformAdministrationService(IApplicationDbContext db, ICurrentUser currentUser, QueueFlow.Application.Abstractions.Clock.IClock clock)
{
    private bool Allowed => currentUser.IsAuthenticated && currentUser.IdentityType == IdentityType.Platform && currentUser.PlatformUserId is not null && currentUser.OrganizationId is null;
    public async Task<PlatformDashboardDto> GetDashboardAsync(CancellationToken ct)
    {
        Ensure();
        var now = clock.UtcNow;
        var organizations = await db.Organizations.AsNoTracking().ToListAsync(ct);
        var subscriptions = await db.Subscriptions.IgnoreQueryFilters().AsNoTracking().ToListAsync(ct);
        var invitations = await db.OrganizationInvitations.AsNoTracking().ToListAsync(ct);
        return new(organizations.Count, organizations.Count(x => x.IsActive), subscriptions.Count(x => x.Status == SubscriptionStatus.Trial), subscriptions.Count(x => x.Status == SubscriptionStatus.Active), invitations.Count(x => x.UsedAt is null && x.RevokedAt is null && x.ExpiresAt > now), invitations.Count(x => x.UsedAt is null && x.RevokedAt is null && x.ExpiresAt <= now));
    }
    public async Task<IReadOnlyList<PlatformOrganizationDto>> GetOrganizationsAsync(CancellationToken ct)
    {
        Ensure();
        var organizations = await db.Organizations.AsNoTracking().OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
        var owners = await db.Users.IgnoreQueryFilters().AsNoTracking().Where(x => x.Role == UserRole.Owner).ToListAsync(ct);
        var subscriptions = await db.Subscriptions.IgnoreQueryFilters().AsNoTracking().ToListAsync(ct);
        var branches = await db.Branches.IgnoreQueryFilters().AsNoTracking().GroupBy(x => x.OrganizationId).Select(x => new { OrganizationId = x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.OrganizationId, x => x.Count, ct);
        var users = await db.Users.IgnoreQueryFilters().AsNoTracking().GroupBy(x => x.OrganizationId).Select(x => new { OrganizationId = x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.OrganizationId, x => x.Count, ct);
        return organizations.Select(org => { var owner = owners.FirstOrDefault(x => x.OrganizationId == org.Id && x.IsActive); var subscription = subscriptions.FirstOrDefault(x => x.OrganizationId == org.Id); return new PlatformOrganizationDto(org.Id, org.Name, org.Slug, owner?.Name, owner?.Email, org.CreatedAt, org.IsActive, subscription?.Plan, subscription?.Status, subscription?.TrialEndsAt, branches.GetValueOrDefault(org.Id), users.GetValueOrDefault(org.Id)); }).ToArray();
    }
    public async Task<IReadOnlyList<PlatformSubscriptionDto>> GetSubscriptionsAsync(CancellationToken ct)
    {
        Ensure();
        var organizations = await db.Organizations.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        return (await db.Subscriptions.IgnoreQueryFilters().AsNoTracking().OrderByDescending(x => x.CreatedAt).ToListAsync(ct)).Where(x => organizations.ContainsKey(x.OrganizationId)).Select(x => new PlatformSubscriptionDto(x.OrganizationId, organizations[x.OrganizationId], x.Plan, x.Status, x.TrialEndsAt)).ToArray();
    }
    public async Task<IReadOnlyList<PlatformAuditDto>> GetAuditAsync(CancellationToken ct)
    {
        Ensure();
        return await db.PlatformAuditLogs.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(200).Select(x => new PlatformAuditDto(x.Id, x.PlatformUserId, x.Action, x.ResourceType, x.ResourceId, x.CreatedAt)).ToListAsync(ct);
    }
    private void Ensure() { if (!Allowed) throw new UnauthorizedAccessException(); }
}
