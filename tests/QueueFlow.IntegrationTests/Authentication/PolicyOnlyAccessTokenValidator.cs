using QueueFlow.Application.Abstractions.Authentication;

namespace QueueFlow.IntegrationTests.Authentication;

// Policy/envelope fixtures intentionally use synthetic identities without a persisted user.
// JWT signature/lifetime and authorization policies remain real; revocation is covered by
// JwtRevocationTests against PostgreSQL with the production validator and no replacement.
internal sealed class PolicyOnlyAccessTokenValidator : IAccessTokenValidator
{
    public Task<bool> ValidateAsync(AccessTokenIdentity identity, CancellationToken cancellationToken) => Task.FromResult(true);
}
