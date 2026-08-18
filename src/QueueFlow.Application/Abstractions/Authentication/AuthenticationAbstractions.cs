using QueueFlow.Domain.Enums;

namespace QueueFlow.Application.Abstractions.Authentication;

public interface ICurrentUser
{
    Guid? UserId { get; }
    Guid? OrganizationId { get; }
    UserRole? Role { get; }
    bool IsAuthenticated { get; }
}

public interface ITokenService
{
    string CreateAccessToken(Guid userId, Guid organizationId, UserRole role, string email);
    string CreateRefreshToken();
    string HashToken(string token);
}

public interface IPasswordService
{
    string Hash(string password);
    bool Verify(string hash, string password);
}
