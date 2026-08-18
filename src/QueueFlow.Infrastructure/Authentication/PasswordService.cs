using Microsoft.AspNetCore.Identity;
using QueueFlow.Application.Abstractions.Authentication;

namespace QueueFlow.Infrastructure.Authentication;

internal sealed class PasswordService : IPasswordService
{
    private readonly PasswordHasher<object> _hasher = new();
    public string Hash(string password) => _hasher.HashPassword(this, password);
    public bool Verify(string hash, string password) => _hasher.VerifyHashedPassword(this, hash, password) is not PasswordVerificationResult.Failed;
}
