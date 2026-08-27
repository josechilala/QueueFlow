namespace QueueFlow.Application.Abstractions.Auditing;

public interface IAuditWriter
{
    void Write(string action, string resourceType, Guid? resourceId, object? data = null);
}
