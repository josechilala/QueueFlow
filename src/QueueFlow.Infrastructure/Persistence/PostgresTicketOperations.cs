using Microsoft.EntityFrameworkCore;
using QueueFlow.Application.Abstractions.Clock;
using QueueFlow.Application.Abstractions.Persistence;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;
using System.Security.Cryptography;
using System.Text.Json;

namespace QueueFlow.Infrastructure.Persistence;

internal sealed class PostgresTicketOperations(ApplicationDbContext db, IClock clock) : ITicketOperations
{
    public async Task<QueueTicket?> IssueAsync(string queuePublicId, TicketPriority priority, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var queue = await db.Queues.FromSqlInterpolated($"SELECT * FROM \"Queues\" WHERE \"PublicId\" = {queuePublicId} FOR UPDATE").IgnoreQueryFilters().SingleOrDefaultAsync(ct);
        if (queue is null || !queue.IsActive || queue.Status != QueueStatus.Open) return null;
        var service = await db.Services.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == queue.ServiceId && x.OrganizationId == queue.OrganizationId && x.BranchId == queue.BranchId, ct);
        if (service is null || !service.IsActive || service.AttendanceMode == ServiceAttendanceMode.AppointmentOnly) return null;
        if (!await db.Branches.IgnoreQueryFilters().AnyAsync(x => x.Id == queue.BranchId && x.OrganizationId == queue.OrganizationId && x.IsActive, ct)
            || !await db.Organizations.AnyAsync(x => x.Id == queue.OrganizationId && x.IsActive, ct)) return null;
        if (queue.Capacity is not null)
        {
            var waitingCount = await db.QueueTickets.IgnoreQueryFilters().CountAsync(x => x.OrganizationId == queue.OrganizationId && x.QueueId == queue.Id && x.Status == TicketStatus.Waiting, ct);
            if (waitingCount >= queue.Capacity.Value) return null;
        }
        var sequence = queue.ReserveSequence();
        var ticket = new QueueTicket(Guid.NewGuid(), queue.OrganizationId, queue.BranchId, queue.Id, queue.ServiceId, $"{service.Prefix}-{sequence:000}", sequence, priority, Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(), clock.UtcNow);
        db.QueueTickets.Add(ticket); db.TicketEvents.Add(new(Guid.NewGuid(), queue.OrganizationId, ticket.Id, TicketStatus.Waiting, null, clock.UtcNow));
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return ticket;
    }
    public Task<int> GetTicketsAheadAsync(Guid organizationId, Guid queueId, Guid ticketId, CancellationToken ct) =>
        db.Database.SqlQuery<int>(PostgresQueueOrdering.TicketsAhead(organizationId, queueId, ticketId, clock.UtcNow)).SingleOrDefaultAsync(ct);

    public async Task<QueueTicket?> CallNextAsync(Guid organizationId, Guid queueId, Guid counterId, Guid attendantId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = clock.UtcNow;
        var ticket = await db.QueueTickets.FromSqlInterpolated(PostgresQueueOrdering.Next(organizationId, queueId, now))
            .IgnoreQueryFilters().SingleOrDefaultAsync(ct);
        if (ticket is null) return null;
        ticket.Call(counterId, attendantId, clock.UtcNow); db.TicketEvents.Add(new(Guid.NewGuid(), organizationId, ticket.Id, TicketStatus.Called, null, clock.UtcNow));
        db.OutboxMessages.Add(new OutboxMessage(Guid.NewGuid(), organizationId, "ticket.realtime", JsonSerializer.Serialize(new { eventName = "ticket.called", ticketToken = ticket.CustomerPublicToken, ticketId = ticket.Id, ticket.QueueId, ticket.TicketNumber, status = ticket.Status.ToString(), counterId }), clock.UtcNow));
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return ticket;
    }
}
