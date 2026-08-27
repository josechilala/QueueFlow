using QueueFlow.Domain.Enums;

namespace QueueFlow.Application.Features.Users;

public static class RolePermissions
{
    public static bool CanManageUsers(UserRole role) => role is UserRole.Owner or UserRole.Admin or UserRole.Manager;
    public static bool CanManageOrganization(UserRole role) => role is UserRole.Owner;
    public static bool CanManageSubscription(UserRole role) => role is UserRole.Owner;

    public static bool CanAssignRole(UserRole actor, UserRole assigned) => actor switch
    {
        UserRole.Owner => true,
        UserRole.Admin => assigned is not UserRole.Owner,
        UserRole.Manager => assigned is UserRole.Attendant or UserRole.Viewer,
        _ => false,
    };
}
