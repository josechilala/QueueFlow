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
public sealed record TenantProvisioning(Organization Organization, AppUser Owner, Subscription Subscription);

public sealed class TenantService(IApplicationDbContext db, IClock clock, IPasswordService passwords)
{
    public async Task<Result<OrganizationCreated>> CreateOrganizationAsync(CreateOrganizationCommand command, CancellationToken cancellationToken)
    {
        var provisioned = await ProvisionAsync(command, cancellationToken);
        if (provisioned.IsFailure) return Result.Failure<OrganizationCreated>(provisioned.Error);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success(new OrganizationCreated(provisioned.Value.Organization.Id, provisioned.Value.Owner.Id));
    }

    public async Task<Result<TenantProvisioning>> ProvisionAsync(CreateOrganizationCommand command, CancellationToken cancellationToken)
    {
        var organization = new Organization(Guid.NewGuid(), command.Name, command.Slug, command.TimeZone, clock.UtcNow);
        if (string.IsNullOrWhiteSpace(command.Password) || command.Password.Length < 12) return Result.Failure<TenantProvisioning>(new("tenant.password", "Password must contain at least 12 characters."));
        var email = command.AdminEmail?.Trim().ToLowerInvariant() ?? string.Empty;
        if (await db.Organizations.AnyAsync(x => x.Slug == organization.Slug, cancellationToken)) return Result.Failure<TenantProvisioning>(new("tenant.slug_conflict", "Slug is already in use."));
        if (await db.Users.IgnoreQueryFilters().AnyAsync(x => x.Email == email, cancellationToken)) return Result.Failure<TenantProvisioning>(new("tenant.email_conflict", "Email is already in use."));
        var user = new AppUser(Guid.NewGuid(), organization.Id, command.AdminName, email, passwords.Hash(command.Password), UserRole.Owner, clock.UtcNow);
        var subscription = new Subscription(Guid.NewGuid(), organization.Id, "Trial", clock.UtcNow.AddDays(14), clock.UtcNow);
        db.Organizations.Add(organization); db.Users.Add(user); db.Subscriptions.Add(subscription);
        return Result.Success(new TenantProvisioning(organization, user, subscription));
    }
}
