using Microsoft.Extensions.Configuration;

namespace QueueFlow.Infrastructure.Billing;

public sealed class AsaasSandboxTenantAccess(IConfiguration configuration)
{
    public Guid? AllowedTenantId => Guid.TryParse(configuration["Asaas:SandboxAllowedTenantId"], out var id) && id != Guid.Empty ? id : null;

    public bool IsAllowed(Guid? tenantId) => tenantId is Guid id && AllowedTenantId is Guid allowed && id == allowed;

    public void RequireAllowed(Guid tenantId)
    {
        if (!IsAllowed(tenantId)) throw new UnauthorizedAccessException("Tenant is not permitted to use Asaas Sandbox.");
    }
}
