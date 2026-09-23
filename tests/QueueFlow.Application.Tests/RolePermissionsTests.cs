using QueueFlow.Application.Features.Users;
using QueueFlow.Domain.Enums;

namespace QueueFlow.Application.Tests;

public sealed class RolePermissionsTests
{
    [Theory]
    [InlineData(UserRole.Owner)]
    [InlineData(UserRole.Admin)]
    public void AdministratorsCanAssignOnlyCommonRoles(UserRole actor)
    {
        foreach (var role in Enum.GetValues<UserRole>())
            Assert.Equal(role is UserRole.Admin or UserRole.Manager or UserRole.Attendant, RolePermissions.CanAssignRole(actor, role));
        Assert.False(RolePermissions.CanAssignRole(actor, (UserRole)999));
    }
    [Fact] public void ManagerCanAssignOnlyOperationalRoles()
    {
        Assert.True(RolePermissions.CanAssignRole(UserRole.Manager, UserRole.Attendant));
        Assert.False(RolePermissions.CanAssignRole(UserRole.Manager, UserRole.Viewer));
        Assert.False(RolePermissions.CanAssignRole(UserRole.Manager, UserRole.Manager));
        Assert.False(RolePermissions.CanAssignRole(UserRole.Manager, UserRole.Admin));
        Assert.False(RolePermissions.CanAssignRole(UserRole.Manager, UserRole.Owner));
    }
    [Fact]
    public void OwnershipIsProtectedAndLegacyViewersRemainManageable()
    {
        foreach (var actor in Enum.GetValues<UserRole>())
        {
            Assert.False(RolePermissions.CanAssignRole(actor, UserRole.Owner));
            Assert.False(RolePermissions.CanAssignRole(actor, UserRole.Viewer));
            Assert.False(RolePermissions.CanManageRole(actor, UserRole.Owner));
            Assert.Equal(RolePermissions.CanManageUsers(actor), RolePermissions.CanManageRole(actor, UserRole.Viewer));
        }
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
