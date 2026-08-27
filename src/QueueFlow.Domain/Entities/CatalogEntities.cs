using QueueFlow.Domain.Common;
using QueueFlow.Domain.Enums;
using System.Security.Cryptography;

namespace QueueFlow.Domain.Entities;

public sealed class Service : AuditableEntity, ITenantEntity
{
    private Service() : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch) { }
    public Service(Guid id, Guid organizationId, Guid branchId, string name, string? description, string prefix, int averageDurationMinutes, DateTimeOffset now) : base(id, now)
    {
        if (organizationId == Guid.Empty || branchId == Guid.Empty || string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200 || string.IsNullOrWhiteSpace(prefix) || prefix.Trim().Length > 10 || averageDurationMinutes is <= 0 or > 1440 || description?.Trim().Length > 1000) throw new DomainException("Service data is invalid.");
        OrganizationId = organizationId; BranchId = branchId; PublicId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(); Name = name.Trim(); Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(); Prefix = prefix.Trim().ToUpperInvariant(); AverageDurationMinutes = averageDurationMinutes;
    }
    public Guid OrganizationId { get; private set; }
    public Guid BranchId { get; private set; }
    public string PublicId { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string Prefix { get; private set; } = string.Empty;
    public int AverageDurationMinutes { get; private set; }
    public bool IsActive { get; private set; } = true;
    public ServiceAttendanceMode AttendanceMode { get; private set; } = ServiceAttendanceMode.QueueOnly;
    public void SetAttendanceMode(ServiceAttendanceMode mode, DateTimeOffset now)
    {
        if (!Enum.IsDefined(mode)) throw new DomainException("Service attendance mode is invalid.");
        AttendanceMode = mode;
        MarkUpdated(now);
    }
}

public sealed class QueueCounter : AuditableEntity, ITenantEntity
{
    private QueueCounter() : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch) { }
    public QueueCounter(Guid id, Guid organizationId, Guid branchId, string name, DateTimeOffset now) : base(id, now)
    {
        if (organizationId == Guid.Empty || branchId == Guid.Empty || string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200) throw new DomainException("Counter data is invalid.");
        OrganizationId = organizationId; BranchId = branchId; Name = name.Trim();
    }
    public Guid OrganizationId { get; private set; }
    public Guid BranchId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;

    public void Update(string name, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200) throw new DomainException("Counter name is invalid.");
        Name = name.Trim();
        MarkUpdated(now);
    }

    public void SetActive(bool isActive, DateTimeOffset now)
    {
        IsActive = isActive;
        MarkUpdated(now);
    }
}
