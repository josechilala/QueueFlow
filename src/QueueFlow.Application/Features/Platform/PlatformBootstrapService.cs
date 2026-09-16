using Microsoft.EntityFrameworkCore;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Abstractions.Clock;
using QueueFlow.Application.Abstractions.Persistence;
using QueueFlow.Application.Common;
using QueueFlow.Domain.Entities;

namespace QueueFlow.Application.Features.Platform;

public sealed class PlatformBootstrapService(IApplicationDbContext db, IPasswordService passwords, IClock clock)
{
    public async Task<Result> ProvisionFirstAdminAsync(string name, string email, string password, CancellationToken ct)
    {
        var normalizedEmail = email?.Trim().ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(normalizedEmail) || string.IsNullOrWhiteSpace(password) || password.Length < 12)
            return Result.Failure(new("platform.bootstrap_invalid", "Nome, e-mail e senha com ao menos 12 caracteres são obrigatórios."));
        await using var transaction = await db.BeginTransactionAsync(ct);
        await db.LockPlatformBootstrapAsync(ct);
        var sameEmail = await db.PlatformUsers.SingleOrDefaultAsync(x => x.Email == normalizedEmail, ct);
        if (sameEmail is not null) return Result.Success();
        if (await db.PlatformUsers.AnyAsync(ct)) return Result.Failure(new("platform.bootstrap_exists", "Já existe um PlatformAdmin. Use uma operação administrativa explícita para criar outro."));
        db.PlatformUsers.Add(new PlatformUser(Guid.NewGuid(), name, normalizedEmail, passwords.Hash(password), clock.UtcNow));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result.Success();
    }
}
