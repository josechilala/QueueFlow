using Microsoft.EntityFrameworkCore;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Abstractions.Clock;
using QueueFlow.Application.Abstractions.Persistence;
using QueueFlow.Application.Common;
using QueueFlow.Application.Features.Auth;

namespace QueueFlow.Application.Features.Platform;

public sealed record PlatformAuthenticatedUser(Guid UserId, string Name, string Email);

public sealed class PlatformAuthService(IApplicationDbContext db, ITokenService tokens, IPasswordService passwords, IClock clock, ICurrentUser currentUser)
{
    public async Task<Result<TokenPair>> LoginAsync(string email, string password, CancellationToken ct)
    {
        var normalized = email?.Trim().ToLowerInvariant() ?? string.Empty;
        var user = await db.PlatformUsers.SingleOrDefaultAsync(x => x.Email == normalized, ct);
        if (user is null || !user.IsActive || !passwords.Verify(user.PasswordHash, password)) return Result.Failure<TokenPair>(new("platform.invalid_credentials", "Invalid credentials."));
        return await CreatePairAsync(user, ct);
    }

    public async Task<Result<TokenPair>> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        await using var transaction = await db.BeginTransactionAsync(ct);
        var hash = tokens.HashToken(refreshToken);
        var stored = await db.PlatformRefreshTokens.FromSqlInterpolated($"SELECT * FROM \"PlatformRefreshTokens\" WHERE \"TokenHash\" = {hash} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (stored is null || stored.ExpiresAt <= clock.UtcNow) return Result.Failure<TokenPair>(new("platform.invalid_refresh", "Refresh token is invalid."));
        var user = await db.PlatformUsers.SingleAsync(x => x.Id == stored.PlatformUserId, ct);
        if (!user.IsActive)
        {
            stored.Revoke(clock.UtcNow);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Result.Failure<TokenPair>(new("platform.invalid_refresh", "Refresh token is invalid."));
        }
        // Preserve the winning request's cookies without allowing replay of the old token.
        if (stored.RevokedAt is not null) return Result.Failure<TokenPair>(new("platform.refresh_conflict", "Refresh token has already been rotated."));
        stored.Revoke(clock.UtcNow);
        var result = await CreatePairAsync(user, ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<Result<PlatformAuthenticatedUser>> GetCurrentAsync(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.IdentityType != QueueFlow.Domain.Enums.IdentityType.Platform || currentUser.PlatformUserId is not Guid id || currentUser.OrganizationId is not null)
            return Result.Failure<PlatformAuthenticatedUser>(new("platform.unauthorized", "Platform authentication is required."));
        var user = await db.PlatformUsers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.IsActive, ct);
        return user is null
            ? Result.Failure<PlatformAuthenticatedUser>(new("platform.unauthorized", "Platform authentication is required."))
            : Result.Success(new PlatformAuthenticatedUser(user.Id, user.Name, user.Email));
    }

    private async Task<Result<TokenPair>> CreatePairAsync(QueueFlow.Domain.Entities.PlatformUser user, CancellationToken ct)
    {
        var refresh = tokens.CreateRefreshToken();
        db.PlatformRefreshTokens.Add(new(Guid.NewGuid(), user.Id, tokens.HashToken(refresh), clock.UtcNow.AddDays(30), clock.UtcNow));
        await db.SaveChangesAsync(ct);
        return Result.Success(new TokenPair(tokens.CreatePlatformAccessToken(user.Id, user.Email), refresh));
    }
}
