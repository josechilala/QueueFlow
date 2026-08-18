using Microsoft.EntityFrameworkCore;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Abstractions.Clock;
using QueueFlow.Application.Abstractions.Persistence;
using QueueFlow.Application.Common;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;

namespace QueueFlow.Application.Features.Tenants;

public sealed record CreateOrganizationCommand(string Name, string Slug, string TimeZone, string AdminName, string AdminEmail, string Password);
public sealed record OrganizationCreated(Guid OrganizationId, Guid UserId);

public sealed class TenantService(IApplicationDbContext db, IClock clock, IPasswordService passwords)
{
    public async Task<Result<OrganizationCreated>> CreateOrganizationAsync(CreateOrganizationCommand command, CancellationToken cancellationToken)
    {
        var organization = new Organization(Guid.NewGuid(), command.Name, command.Slug, command.TimeZone, clock.UtcNow);
        if (command.Password.Length < 12) return Result.Failure<OrganizationCreated>(new("tenant.password", "Password must contain at least 12 characters."));
        if (await db.Organizations.AnyAsync(x => x.Slug == organization.Slug, cancellationToken)) return Result.Failure<OrganizationCreated>(new("tenant.slug_conflict", "Slug is already in use."));
        var user = new AppUser(Guid.NewGuid(), organization.Id, command.AdminName, command.AdminEmail, passwords.Hash(command.Password), clock.UtcNow);
        var subscription = new Subscription(Guid.NewGuid(), organization.Id, "Trial", clock.UtcNow.AddDays(14), clock.UtcNow);
        db.Organizations.Add(organization); db.Users.Add(user); db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success(new OrganizationCreated(organization.Id, user.Id));
    }
}
