using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Domain.Enums;

namespace QueueFlow.Infrastructure.Authentication;

internal sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;
    public Guid? UserId => IdentityType == QueueFlow.Domain.Enums.IdentityType.Tenant ? Parse(ClaimTypes.NameIdentifier) : null;
    public Guid? PlatformUserId => IdentityType == QueueFlow.Domain.Enums.IdentityType.Platform ? Parse(ClaimTypes.NameIdentifier) : null;
    public Guid? OrganizationId => Parse("organization_id");
    public UserRole? Role => Enum.TryParse<UserRole>(Principal?.FindFirstValue(ClaimTypes.Role), out var role) ? role : null;
    public IdentityType? IdentityType => Principal?.FindFirstValue("identity_type")?.ToLowerInvariant() switch { "tenant" => QueueFlow.Domain.Enums.IdentityType.Tenant, "platform" => QueueFlow.Domain.Enums.IdentityType.Platform, _ => null };
    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
    private Guid? Parse(string type) => Guid.TryParse(Principal?.FindFirstValue(type), out var value) ? value : null;
}
