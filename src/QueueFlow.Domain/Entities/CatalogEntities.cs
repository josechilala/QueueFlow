using QueueFlow.Domain.Common;

namespace QueueFlow.Domain.Entities;

public sealed class Service : AuditableEntity, ITenantEntity
{
    private Service() : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch) { }
    public Service(Guid id, Guid organizationId, Guid branchId, string name, string prefix, int averageDurationMinutes, DateTimeOffset now) : base(id, now)
    {
        if (organizationId == Guid.Empty || branchId == Guid.Empty || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(prefix) || averageDurationMinutes <= 0) throw new DomainException("Service data is invalid.");
        OrganizationId = organizationId; BranchId = branchId; Name = name.Trim(); Prefix = prefix.Trim().ToUpperInvariant(); AverageDurationMinutes = averageDurationMinutes;
    }
    public Guid OrganizationId { get; private set; }
    public Guid BranchId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Prefix { get; private set; } = string.Empty;
    public int AverageDurationMinutes { get; private set; }
    public bool IsActive { get; private set; } = true;
}

public sealed class QueueCounter : AuditableEntity, ITenantEntity
{
    private QueueCounter() : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch) { }
    public QueueCounter(Guid id, Guid organizationId, Guid branchId, string name, DateTimeOffset now) : base(id, now)
    { OrganizationId = organizationId; BranchId = branchId; Name = string.IsNullOrWhiteSpace(name) ? throw new DomainException("Counter name is required.") : name.Trim(); }
    public Guid OrganizationId { get; private set; }
    public Guid BranchId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;
}
