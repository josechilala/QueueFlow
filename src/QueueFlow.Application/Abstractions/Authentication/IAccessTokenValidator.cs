namespace QueueFlow.Application.Abstractions.Authentication;

public sealed record AccessTokenIdentity(Guid UserId, Guid? OrganizationId, string IdentityType, string Role, long AccessVersion);

public interface IAccessTokenValidator
{
    Task<bool> ValidateAsync(AccessTokenIdentity identity, CancellationToken cancellationToken);
}
