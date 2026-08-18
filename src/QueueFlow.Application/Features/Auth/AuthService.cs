using Microsoft.EntityFrameworkCore;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Abstractions.Clock;
using QueueFlow.Application.Abstractions.Persistence;
using QueueFlow.Application.Common;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;

namespace QueueFlow.Application.Features.Auth;

public sealed record TokenPair(string AccessToken, string RefreshToken);

public sealed class AuthService(IApplicationDbContext db, ITokenService tokens, IPasswordService passwords, IClock clock)
{
    public async Task<Result<TokenPair>> LoginAsync(string email, string password, CancellationToken ct)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var user = await db.Users.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Email == normalizedEmail, ct);
        if (user is null || !user.IsActive || !passwords.Verify(user.PasswordHash, password)) return Result.Failure<TokenPair>(new("auth.invalid_credentials", "Invalid credentials."));
        return await CreatePairAsync(user, UserRole.Owner, ct);
    }
    public async Task<Result<TokenPair>> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var hash = tokens.HashToken(refreshToken); var stored = await db.RefreshTokens.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (stored is null || !stored.IsActive(clock.UtcNow)) return Result.Failure<TokenPair>(new("auth.invalid_refresh", "Refresh token is invalid or has been reused."));
        stored.Revoke(clock.UtcNow); var user = await db.Users.IgnoreQueryFilters().SingleAsync(x => x.Id == stored.UserId, ct); return await CreatePairAsync(user, UserRole.Owner, ct);
    }
    private async Task<Result<TokenPair>> CreatePairAsync(AppUser user, UserRole role, CancellationToken ct)
    {
        var refresh = tokens.CreateRefreshToken(); db.RefreshTokens.Add(new(Guid.NewGuid(), user.OrganizationId, user.Id, tokens.HashToken(refresh), clock.UtcNow.AddDays(30), clock.UtcNow)); await db.SaveChangesAsync(ct);
        return Result.Success(new TokenPair(tokens.CreateAccessToken(user.Id, user.OrganizationId, role, user.Email), refresh));
    }
}
