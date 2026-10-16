using Microsoft.EntityFrameworkCore;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Abstractions.Persistence;

namespace QueueFlow.Infrastructure.Authentication;

internal sealed class AccessTokenValidator(IApplicationDbContext db) : IAccessTokenValidator
{
    // Npgsql/PostgreSQL persist timestamps with microsecond precision (10 .NET ticks).
    public async Task<bool> ValidateAsync(AccessTokenIdentity identity, CancellationToken cancellationToken)
    {
        if (identity.UserId == Guid.Empty || identity.AccessVersion <= 0 || identity.AccessVersion > DateTimeOffset.MaxValue.UtcTicks / 10)
            return false;

        if (identity.IdentityType == "tenant" && identity.OrganizationId is Guid organizationId && organizationId != Guid.Empty)
        {
            // Authentication precedes tenant resolution: never rely on the current tenant filter here.
            var user = await db.Users.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.Id == identity.UserId && x.OrganizationId == organizationId)
                .Select(x => new { x.IsActive, x.Role, x.UpdatedAt, x.CreatedAt }).SingleOrDefaultAsync(cancellationToken);
            return user is not null && user.IsActive && user.Role.ToString() == identity.Role &&
                (user.UpdatedAt ?? user.CreatedAt).UtcTicks / 10 == identity.AccessVersion;
        }

        if (identity.IdentityType == "platform" && identity.OrganizationId is null && identity.Role == "PlatformAdmin")
        {
            var user = await db.PlatformUsers.AsNoTracking().Where(x => x.Id == identity.UserId)
                .Select(x => new { x.IsActive, x.UpdatedAt, x.CreatedAt }).SingleOrDefaultAsync(cancellationToken);
            return user is not null && user.IsActive &&
                (user.UpdatedAt ?? user.CreatedAt).UtcTicks / 10 == identity.AccessVersion;
        }
        return false;
    }
}
