using Microsoft.AspNetCore.Identity;
using QueueFlow.Application.Abstractions.Authentication;

namespace QueueFlow.Infrastructure.Authentication;

internal sealed class PasswordService : IPasswordService
{
    private static readonly string DummyHash = new PasswordHasher<object>().HashPassword(new object(), Guid.NewGuid().ToString("N"));
    private readonly PasswordHasher<object> _hasher = new();
    public string Hash(string password) => _hasher.HashPassword(this, password);
    public bool Verify(string? hash, string password)
    {
        var verified = _hasher.VerifyHashedPassword(this, hash ?? DummyHash, password) is not PasswordVerificationResult.Failed;
        return hash is not null && verified;
    }
}
