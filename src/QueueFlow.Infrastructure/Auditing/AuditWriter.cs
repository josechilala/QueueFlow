using System.Text.Json;
using Microsoft.AspNetCore.Http;
using QueueFlow.Application.Abstractions.Auditing;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Abstractions.Clock;
using QueueFlow.Domain.Entities;
using QueueFlow.Infrastructure.Persistence;

namespace QueueFlow.Infrastructure.Auditing;

internal sealed class AuditWriter(ApplicationDbContext db, ICurrentUser currentUser, IClock clock, IHttpContextAccessor httpContext) : IAuditWriter
{
    public void Write(string action, string resourceType, Guid? resourceId, object? data = null)
    {
        if (currentUser.OrganizationId is not Guid organizationId) return;
        var correlationId = httpContext.HttpContext?.TraceIdentifier ?? Guid.NewGuid().ToString("N");
        db.AuditLogs.Add(new AuditLog(Guid.NewGuid(), organizationId, currentUser.UserId, action, resourceType, resourceId, data is null ? null : JsonSerializer.Serialize(data), correlationId, clock.UtcNow));
    }
}
