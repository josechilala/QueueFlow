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
    string CreateAccessToken(Guid userId, Guid organizationId, UserRole role, string email);
    string CreatePlatformAccessToken(Guid platformUserId, string email);
    string CreateRefreshToken();
    string CreateRefreshToken(Guid userId, IdentityType identityType);
    Guid? GetRefreshRateLimitIdentity(string token, IdentityType identityType);
    string HashToken(string token);
}

public interface IPasswordService
{
    string Hash(string password);
    bool Verify(string hash, string password);
}
