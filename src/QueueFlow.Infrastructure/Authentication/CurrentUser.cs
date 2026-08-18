using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Domain.Enums;

namespace QueueFlow.Infrastructure.Authentication;

internal sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;
    public Guid? UserId => Parse(ClaimTypes.NameIdentifier);
    public Guid? OrganizationId => Parse("organization_id");
    public UserRole? Role => Enum.TryParse<UserRole>(Principal?.FindFirstValue(ClaimTypes.Role), out var role) ? role : null;
    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
    private Guid? Parse(string type) => Guid.TryParse(Principal?.FindFirstValue(type), out var value) ? value : null;
}
