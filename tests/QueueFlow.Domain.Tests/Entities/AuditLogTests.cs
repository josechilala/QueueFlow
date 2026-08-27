using QueueFlow.Domain.Entities;

namespace QueueFlow.Domain.Tests.Entities;

public sealed class AuditLogTests
{
    [Fact]
    public void AuditKeepsActorResourceAndCorrelation()
    {
        var organizationId = Guid.NewGuid(); var userId = Guid.NewGuid(); var resourceId = Guid.NewGuid();
        var audit = new AuditLog(Guid.NewGuid(), organizationId, userId, "queue.opened", "Queue", resourceId, "{\"status\":\"Open\"}", "correlation-123", DateTimeOffset.UtcNow);
        Assert.Equal(organizationId, audit.OrganizationId); Assert.Equal(userId, audit.UserId); Assert.Equal(resourceId, audit.ResourceId);
        Assert.Equal("Queue", audit.ResourceType); Assert.Equal("queue.opened", audit.Action); Assert.Equal("correlation-123", audit.CorrelationId);
    }
}
