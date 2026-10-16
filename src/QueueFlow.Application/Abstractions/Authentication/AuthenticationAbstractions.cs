using QueueFlow.Domain.Enums;

namespace QueueFlow.Application.Abstractions.Authentication;

public interface ICurrentUser
{
    Guid? UserId { get; }
    Guid? PlatformUserId => null;
    Guid? OrganizationId { get; }
    UserRole? Role { get; }
    IdentityType? IdentityType => null;
    bool IsAuthenticated { get; }
}

public interface ITokenService
{
    // Supply the persisted user's UpdatedAt ?? CreatedAt. Unversioned tokens fail access validation.
    string CreateAccessToken(Guid userId, Guid organizationId, UserRole role, string email, DateTimeOffset? accessVersion = null);
    string CreatePlatformAccessToken(Guid platformUserId, string email, DateTimeOffset? accessVersion = null);
    string CreateRefreshToken();
    string CreateRefreshToken(Guid userId, IdentityType identityType);
    Guid? GetRefreshRateLimitIdentity(string token, IdentityType identityType);
    string HashToken(string token);
}

public interface IPasswordService
{
    string Hash(string password);
    // A missing hash must still perform password verification work and return false.
    bool Verify(string? hash, string password);
}
