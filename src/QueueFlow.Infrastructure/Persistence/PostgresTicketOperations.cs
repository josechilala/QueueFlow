using Microsoft.EntityFrameworkCore;
using QueueFlow.Application.Abstractions.Clock;
using QueueFlow.Application.Abstractions.Persistence;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;

namespace QueueFlow.Infrastructure.Persistence;

internal sealed class PostgresTicketOperations(ApplicationDbContext db, IClock clock) : ITicketOperations
{
    public async Task<QueueTicket?> IssueAsync(string queuePublicId, TicketPriority priority, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var queue = await db.Queues.FromSqlInterpolated($"SELECT * FROM \"Queues\" WHERE \"PublicId\" = {queuePublicId} FOR UPDATE").IgnoreQueryFilters().SingleOrDefaultAsync(ct);
        if (queue is null || queue.Status != QueueStatus.Open) return null;
        var service = await db.Services.IgnoreQueryFilters().SingleAsync(x => x.Id == queue.ServiceId, ct);
        var sequence = queue.ReserveSequence();
        var ticket = new QueueTicket(Guid.NewGuid(), queue.OrganizationId, queue.BranchId, queue.Id, queue.ServiceId, $"{service.Prefix}{sequence:000}", sequence, priority, Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant(), clock.UtcNow);
        db.QueueTickets.Add(ticket); db.TicketEvents.Add(new(Guid.NewGuid(), queue.OrganizationId, ticket.Id, TicketStatus.Waiting, null, clock.UtcNow));
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return ticket;
    }
    public async Task<QueueTicket?> CallNextAsync(Guid organizationId, Guid queueId, Guid counterId, Guid attendantId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var ticket = await db.QueueTickets.FromSqlInterpolated($"SELECT * FROM \"QueueTickets\" WHERE \"OrganizationId\" = {organizationId} AND \"QueueId\" = {queueId} AND \"Status\" = {(int)TicketStatus.Waiting} ORDER BY \"Priority\" DESC, \"IssuedAt\", \"SequenceNumber\" FOR UPDATE SKIP LOCKED LIMIT 1").SingleOrDefaultAsync(ct);
        if (ticket is null) return null;
        ticket.Call(counterId, attendantId, clock.UtcNow); db.TicketEvents.Add(new(Guid.NewGuid(), organizationId, ticket.Id, TicketStatus.Called, null, clock.UtcNow));
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return ticket;
    }
}
