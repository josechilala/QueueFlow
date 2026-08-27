using QueueFlow.Application.Features.Users;
using QueueFlow.Domain.Enums;

namespace QueueFlow.Application.Tests;

public sealed class RolePermissionsTests
{
    [Fact] public void OwnerCanAssignEveryRole() => Assert.All(Enum.GetValues<UserRole>(), role => Assert.True(RolePermissions.CanAssignRole(UserRole.Owner, role)));
    [Fact] public void ManagerCanAssignOnlyOperationalRoles()
    {
        Assert.True(RolePermissions.CanAssignRole(UserRole.Manager, UserRole.Attendant));
        Assert.True(RolePermissions.CanAssignRole(UserRole.Manager, UserRole.Viewer));
        Assert.False(RolePermissions.CanAssignRole(UserRole.Manager, UserRole.Manager));
        Assert.False(RolePermissions.CanAssignRole(UserRole.Manager, UserRole.Admin));
        Assert.False(RolePermissions.CanAssignRole(UserRole.Manager, UserRole.Owner));
    }
    [Theory]
    [InlineData(UserRole.Attendant)]
    [InlineData(UserRole.Viewer)]
    public void OperationalRolesCannotManageSubscriptionOrOrganization(UserRole role)
    {
        Assert.False(RolePermissions.CanManageSubscription(role));
        Assert.False(RolePermissions.CanManageOrganization(role));
        Assert.False(RolePermissions.CanManageUsers(role));
    }
}
