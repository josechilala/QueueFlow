using Microsoft.EntityFrameworkCore;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Abstractions.Clock;
using QueueFlow.Application.Abstractions.Persistence;
using QueueFlow.Application.Common;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;

namespace QueueFlow.Application.Features.Auth;

public sealed record TokenPair(string AccessToken, string RefreshToken);
public sealed record AuthenticatedUser(Guid UserId, Guid OrganizationId, string Name, string Email, UserRole Role);

public sealed class AuthService(IApplicationDbContext db, ITokenService tokens, IPasswordService passwords, IClock clock, ICurrentUser currentUser)
{
    public async Task<Result<TokenPair>> LoginAsync(string email, string password, CancellationToken ct)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var user = await db.Users.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Email == normalizedEmail, ct);
        if (user is null || !user.IsActive || !passwords.Verify(user.PasswordHash, password)) return Result.Failure<TokenPair>(new("auth.invalid_credentials", "Invalid credentials."));
        return await CreatePairAsync(user, ct);
    }
    public async Task<Result<TokenPair>> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var hash = tokens.HashToken(refreshToken); var stored = await db.RefreshTokens.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (stored is null || !stored.IsActive(clock.UtcNow)) return Result.Failure<TokenPair>(new("auth.invalid_refresh", "Refresh token is invalid or has been reused."));
        stored.Revoke(clock.UtcNow); var user = await db.Users.IgnoreQueryFilters().SingleAsync(x => x.Id == stored.UserId, ct);
        if (!user.IsActive) { await db.SaveChangesAsync(ct); return Result.Failure<TokenPair>(new("auth.invalid_refresh", "Refresh token is invalid or has been reused.")); }
        return await CreatePairAsync(user, ct);
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

        return Result.Success(new AuthenticatedUser(user.Id, user.OrganizationId, user.Name, user.Email, user.Role));
    }

    private async Task<Result<TokenPair>> CreatePairAsync(AppUser user, CancellationToken ct)
    {
        var refresh = tokens.CreateRefreshToken(); db.RefreshTokens.Add(new(Guid.NewGuid(), user.OrganizationId, user.Id, tokens.HashToken(refresh), clock.UtcNow.AddDays(30), clock.UtcNow)); await db.SaveChangesAsync(ct);
        return Result.Success(new TokenPair(tokens.CreateAccessToken(user.Id, user.OrganizationId, user.Role, user.Email), refresh));
    }
}
