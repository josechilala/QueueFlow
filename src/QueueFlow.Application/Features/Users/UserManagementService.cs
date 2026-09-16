using Microsoft.EntityFrameworkCore;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Abstractions.Clock;
using QueueFlow.Application.Abstractions.Persistence;
using QueueFlow.Application.Common;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;
using QueueFlow.Application.Abstractions.Auditing;

namespace QueueFlow.Application.Features.Users;

public sealed record ManagedUserDto(Guid Id, string Name, string Email, UserRole Role, bool IsActive, IReadOnlyList<Guid> BranchIds);
public sealed record CreateManagedUserRequest(string Name, string Email, string Password, UserRole Role, IReadOnlyList<Guid>? BranchIds);
public sealed record UpdateManagedUserRequest(string Name, UserRole Role, IReadOnlyList<Guid>? BranchIds);
public sealed record SetManagedUserStatusRequest(bool IsActive);
public sealed record ResetManagedUserPasswordRequest(string Password);

public sealed class UserManagementService(IApplicationDbContext db, ICurrentUser currentUser, IClock clock, IPasswordService passwords, IAuditWriter audit)
{
    private static readonly Error Forbidden = new("users.forbidden", "You cannot perform this user management operation.");

    public async Task<IReadOnlyList<ManagedUserDto>> ListAsync(CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
        var assignments = await db.UserBranches.AsNoTracking().ToListAsync(ct);
        return users.Select(user => Map(user, assignments.Where(x => x.UserId == user.Id).Select(x => x.BranchId).ToArray())).ToArray();
    }

    public async Task<Result<ManagedUserDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (user is null) return Result.Failure<ManagedUserDto>(new("users.not_found", "User was not found."));
        var branches = await db.UserBranches.AsNoTracking().Where(x => x.UserId == id).Select(x => x.BranchId).ToArrayAsync(ct);
        return Result.Success(Map(user, branches));
    }

    public async Task<Result<ManagedUserDto>> CreateAsync(CreateManagedUserRequest request, CancellationToken ct)
    {
        var actor = currentUser.Role;
        if (actor is null || !RolePermissions.CanAssignRole(actor.Value, request.Role)) return Result.Failure<ManagedUserDto>(Forbidden);
        if (string.IsNullOrEmpty(request.Password) || request.Password.Length < 12) return Result.Failure<ManagedUserDto>(new("users.password", "Password must contain at least 12 characters."));
        var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        if (email.Length == 0) return Result.Failure<ManagedUserDto>(new("users.email", "Email is required."));
        if (await db.Users.IgnoreQueryFilters().AnyAsync(x => x.Email == email, ct)) return Result.Failure<ManagedUserDto>(new("users.email_conflict", "Email is already in use."));
        var branchResult = await ValidateBranchesAsync(request.BranchIds, request.Role, ct);
        if (branchResult.IsFailure) return Result.Failure<ManagedUserDto>(branchResult.Error);

        var user = new AppUser(Guid.NewGuid(), currentUser.OrganizationId!.Value, request.Name, email, passwords.Hash(request.Password), request.Role, clock.UtcNow);
        db.Users.Add(user);
        AddAssignments(user, branchResult.Value);
        audit.Write("user.created", "User", user.Id, new { Role = user.Role.ToString(), BranchIds = branchResult.Value });
        await db.SaveChangesAsync(ct);
        return Result.Success(Map(user, branchResult.Value));
    }

    public async Task<Result<ManagedUserDto>> UpdateAsync(Guid id, UpdateManagedUserRequest request, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (user is null) return Result.Failure<ManagedUserDto>(new("users.not_found", "User was not found."));
        var actor = currentUser.Role;
        if (actor is null || !RolePermissions.CanAssignRole(actor.Value, user.Role) || !RolePermissions.CanAssignRole(actor.Value, request.Role)) return Result.Failure<ManagedUserDto>(Forbidden);
        if (user.Role == UserRole.Owner && request.Role != UserRole.Owner && !await HasAnotherOwnerAsync(user.Id, ct)) return Result.Failure<ManagedUserDto>(new("users.last_owner", "The last active Owner cannot be changed."));
        var branchResult = await ValidateBranchesAsync(request.BranchIds, request.Role, ct);
        if (branchResult.IsFailure) return Result.Failure<ManagedUserDto>(branchResult.Error);

        var previousRole = user.Role;
        user.Update(request.Name, request.Role, clock.UtcNow);
        var existing = await db.UserBranches.Where(x => x.UserId == id).ToListAsync(ct);
        db.UserBranches.RemoveRange(existing);
        AddAssignments(user, branchResult.Value);
        await RevokeSessionsAsync(id, ct);
        audit.Write(previousRole == request.Role ? "user.updated" : "user.permission_changed", "User", user.Id, new { PreviousRole = previousRole.ToString(), NewRole = request.Role.ToString(), BranchIds = branchResult.Value });
        await db.SaveChangesAsync(ct);
        return Result.Success(Map(user, branchResult.Value));
    }

    public async Task<Result<ManagedUserDto>> SetStatusAsync(Guid id, bool isActive, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (user is null) return Result.Failure<ManagedUserDto>(new("users.not_found", "User was not found."));
        var actor = currentUser.Role;
        if (actor is null || !RolePermissions.CanAssignRole(actor.Value, user.Role)) return Result.Failure<ManagedUserDto>(Forbidden);
        if (!isActive && id == currentUser.UserId) return Result.Failure<ManagedUserDto>(new("users.self_deactivation", "You cannot deactivate your own account."));
        if (!isActive && user.Role == UserRole.Owner && !await HasAnotherOwnerAsync(user.Id, ct)) return Result.Failure<ManagedUserDto>(new("users.last_owner", "The last active Owner cannot be deactivated."));
        user.SetActive(isActive, clock.UtcNow);
        if (!isActive) await RevokeSessionsAsync(id, ct);
        audit.Write("user.status_changed", "User", user.Id, new { IsActive = isActive });
        await db.SaveChangesAsync(ct);
        var branches = await db.UserBranches.Where(x => x.UserId == id).Select(x => x.BranchId).ToArrayAsync(ct);
        return Result.Success(Map(user, branches));
    }

    public async Task<Result> ResetPasswordAsync(Guid id, string password, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (user is null) return Result.Failure(new("users.not_found", "User was not found."));
        var actor = currentUser.Role;
        if (actor is null || !RolePermissions.CanAssignRole(actor.Value, user.Role)) return Result.Failure(Forbidden);
        if (string.IsNullOrEmpty(password) || password.Length < 12) return Result.Failure(new("users.password", "Password must contain at least 12 characters."));
        user.SetPasswordHash(passwords.Hash(password), clock.UtcNow);
        await RevokeSessionsAsync(id, ct);
        audit.Write("user.password_reset", "User", user.Id);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task<Result<Guid[]>> ValidateBranchesAsync(IReadOnlyList<Guid>? ids, UserRole role, CancellationToken ct)
    {
        var unique = (ids ?? []).Where(x => x != Guid.Empty).Distinct().ToArray();
        if (role == UserRole.Attendant && unique.Length == 0) return Result.Failure<Guid[]>(new("users.branch_required", "An attendant must be assigned to at least one branch."));
        var count = await db.Branches.CountAsync(x => unique.Contains(x.Id) && x.IsActive, ct);
        return count == unique.Length ? Result.Success(unique) : Result.Failure<Guid[]>(new("users.invalid_branch", "One or more branches are invalid or inactive."));
    }

    private void AddAssignments(AppUser user, IEnumerable<Guid> branchIds)
    {
        foreach (var branchId in branchIds) db.UserBranches.Add(new UserBranch(Guid.NewGuid(), user.OrganizationId, user.Id, branchId, user.Role));
    }

    private Task<bool> HasAnotherOwnerAsync(Guid id, CancellationToken ct) => db.Users.AnyAsync(x => x.Id != id && x.Role == UserRole.Owner && x.IsActive, ct);
    private async Task RevokeSessionsAsync(Guid id, CancellationToken ct) { foreach (var token in await db.RefreshTokens.Where(x => x.UserId == id && x.RevokedAt == null).ToListAsync(ct)) token.Revoke(clock.UtcNow); }
    private static ManagedUserDto Map(AppUser user, IReadOnlyList<Guid> branches) => new(user.Id, user.Name, user.Email, user.Role, user.IsActive, branches);
}
