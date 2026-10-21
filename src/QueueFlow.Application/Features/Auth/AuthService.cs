using Microsoft.EntityFrameworkCore;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Abstractions.Clock;
using QueueFlow.Application.Abstractions.Persistence;
using QueueFlow.Application.Common;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;

namespace QueueFlow.Application.Features.Auth;

public sealed record TokenPair(string AccessToken, string RefreshToken);
public sealed record AuthenticatedUser(Guid UserId, Guid OrganizationId, string Name, string Email, UserRole Role, string? OrganizationName = null);

public sealed class AuthService(IApplicationDbContext db, ITokenService tokens, IPasswordService passwords, IClock clock, ICurrentUser currentUser)
{
    public async Task<Result<TokenPair>> LoginAsync(string email, string password, CancellationToken ct)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        await using var transaction = await db.BeginTransactionAsync(ct);
        // User first, then refresh tokens: security changes use the same lock order.
        var matches = await db.Users.FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE \"Email\" = {normalizedEmail} FOR UPDATE")
            .IgnoreQueryFilters().Take(2).ToListAsync(ct);
        var user = matches.Count == 1 ? matches[0] : null;
        var verified = passwords.Verify(user?.PasswordHash, password);
        if (user is null || !user.IsActive || !verified) return Result.Failure<TokenPair>(new("auth.invalid_credentials", "Invalid credentials."));
        var result = await CreatePairAsync(user, ct);
        await transaction.CommitAsync(ct);
        return result;
    }
    public async Task<Result<TokenPair>> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        await using var transaction = await db.BeginTransactionAsync(ct);
        var hash = tokens.HashToken(refreshToken);
        // This lookup only identifies the lock owner; token validity is rechecked under the locks.
        var owner = await db.RefreshTokens.IgnoreQueryFilters().AsNoTracking().Where(x => x.TokenHash == hash)
            .Select(x => new { x.UserId, x.OrganizationId }).SingleOrDefaultAsync(ct);
        if (owner is null) return Result.Failure<TokenPair>(new("auth.invalid_refresh", "Refresh token is invalid or expired."));
        var user = await db.Users.FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE \"Id\" = {owner.UserId} AND \"OrganizationId\" = {owner.OrganizationId} FOR UPDATE")
            .IgnoreQueryFilters().SingleOrDefaultAsync(ct);
        if (user is null) return Result.Failure<TokenPair>(new("auth.invalid_refresh", "Refresh token is invalid."));
        var stored = await db.RefreshTokens.FromSqlInterpolated($"SELECT * FROM \"RefreshTokens\" WHERE \"TokenHash\" = {hash} FOR UPDATE")
            .IgnoreQueryFilters().SingleOrDefaultAsync(ct);
        if (stored is null || stored.UserId != user.Id || stored.OrganizationId != user.OrganizationId || stored.ExpiresAt <= clock.UtcNow)
            return Result.Failure<TokenPair>(new("auth.invalid_refresh", "Refresh token is invalid or expired."));
        if (!user.IsActive)
        {
            stored.Revoke(clock.UtcNow);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Result.Failure<TokenPair>(new("auth.invalid_refresh", "Refresh token is invalid."));
        }
        // A stale concurrent request must not log out a session already rotated by another request.
        // Never return credentials for a used token; this also works across API/BFF instances.
        if (stored.RevokedAt is not null) return Result.Failure<TokenPair>(new("auth.refresh_conflict", "Refresh token has already been rotated."));
        stored.Revoke(clock.UtcNow);
        var result = await CreatePairAsync(user, ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<Result<AuthenticatedUser>> GetCurrentAsync(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId || currentUser.OrganizationId is not Guid organizationId)
        {
            return Result.Failure<AuthenticatedUser>(new("auth.unauthorized", "Authentication is required."));
        }

        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, ct);
        if (user is null || !user.IsActive || user.OrganizationId != organizationId)
        {
            return Result.Failure<AuthenticatedUser>(new("auth.invalid_session", "The authenticated user is no longer active."));
        }

        var organizationName = await db.Organizations.AsNoTracking()
            .Where(x => x.Id == organizationId)
            .Select(x => x.Name)
            .SingleOrDefaultAsync(ct);

        return Result.Success(new AuthenticatedUser(user.Id, user.OrganizationId, user.Name, user.Email, user.Role, organizationName));
    }

    private async Task<Result<TokenPair>> CreatePairAsync(AppUser user, CancellationToken ct)
    {
        var refresh = tokens.CreateRefreshToken(user.Id, IdentityType.Tenant); db.RefreshTokens.Add(new(Guid.NewGuid(), user.OrganizationId, user.Id, tokens.HashToken(refresh), clock.UtcNow.AddDays(30), clock.UtcNow)); await db.SaveChangesAsync(ct);
        return Result.Success(new TokenPair(tokens.CreateAccessToken(user.Id, user.OrganizationId, user.Role, user.Email, user.UpdatedAt ?? user.CreatedAt), refresh));
    }
}
