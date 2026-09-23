using QueueFlow.Domain.Enums;

namespace QueueFlow.Application.Features.Users;

public static class RolePermissions
{
    public static bool CanManageUsers(UserRole role) => role is UserRole.Owner or UserRole.Admin or UserRole.Manager;
    public static bool CanManageOrganization(UserRole role) => role is UserRole.Owner;
    public static bool CanManageSubscription(UserRole role) => role is UserRole.Owner;

    public static bool CanAssignRole(UserRole actor, UserRole assigned) =>
        assigned is UserRole.Admin or UserRole.Manager or UserRole.Attendant && CanManageRole(actor, assigned);

    // Existing Viewers remain manageable; ownership is outside ordinary user management.
    public static bool CanManageRole(UserRole actor, UserRole target) => actor switch
    {
        UserRole.Owner or UserRole.Admin => target is UserRole.Admin or UserRole.Manager or UserRole.Attendant or UserRole.Viewer,
        UserRole.Manager => target is UserRole.Attendant or UserRole.Viewer,
        _ => false,
    };
}
