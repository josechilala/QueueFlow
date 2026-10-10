using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using QueueFlow.Application.Abstractions.Authentication;

namespace QueueFlow.Infrastructure.Persistence;

/// <summary>Offline EF tooling never loads API User Secrets or starts application services.</summary>
public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(
            "Host=127.0.0.1;Port=1;Database=queueflow_design_only;Username=design_only;Password=design_only;Timeout=1").Options,
        new DesignIdentity());

    private sealed class DesignIdentity : ICurrentUser
    {
        public Guid? UserId => null;
        public Guid? OrganizationId => null;
        public QueueFlow.Domain.Enums.UserRole? Role => null;
        public bool IsAuthenticated => false;
    }
}
