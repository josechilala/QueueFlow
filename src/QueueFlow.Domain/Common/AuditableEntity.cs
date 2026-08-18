namespace QueueFlow.Domain.Common;

public abstract class AuditableEntity(Guid id, DateTimeOffset createdAt) : BaseEntity(id)
{
    public DateTimeOffset CreatedAt { get; private set; } = createdAt;
    public DateTimeOffset? UpdatedAt { get; private set; }

    protected void MarkUpdated(DateTimeOffset timestamp) => UpdatedAt = timestamp;
}

public interface ITenantEntity
{
    Guid OrganizationId { get; }
}
