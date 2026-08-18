using Microsoft.EntityFrameworkCore;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Abstractions.Clock;
using QueueFlow.Application.Abstractions.Persistence;
using QueueFlow.Application.Common;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;
using QueueFlowQueue = QueueFlow.Domain.Entities.Queue;

namespace QueueFlow.Application.Features.Queues;

public sealed record QueueDto(Guid Id, Guid BranchId, Guid ServiceId, string Name, string PublicId, QueueStatus Status);
public sealed record TicketDto(Guid Id, string TicketNumber, TicketStatus Status, DateTimeOffset IssuedAt, int TicketsAhead, int EstimatedMinutes);

public sealed class QueueOperationsService(IApplicationDbContext db, ITicketOperations tickets, ICurrentUser currentUser, IClock clock)
{
    private Guid Tenant => currentUser.OrganizationId ?? throw new UnauthorizedAccessException();
    public async Task<Result<QueueDto>> CreateAsync(Guid branchId, Guid serviceId, string name, CancellationToken ct)
    { if (!await db.Services.AnyAsync(x => x.Id == serviceId && x.BranchId == branchId, ct)) return Result.Failure<QueueDto>(new("queue.catalog_not_found", "Branch/service was not found.")); var queue = new QueueFlowQueue(Guid.NewGuid(), Tenant, branchId, serviceId, name, clock.UtcNow); db.Queues.Add(queue); await db.SaveChangesAsync(ct); return Result.Success(Map(queue)); }
    public async Task<IReadOnlyList<QueueDto>> ListAsync(CancellationToken ct) => await db.Queues.AsNoTracking().OrderBy(x => x.Name).Select(x => new QueueDto(x.Id, x.BranchId, x.ServiceId, x.Name, x.PublicId, x.Status)).ToListAsync(ct);
    public Task<Result<QueueDto>> OpenAsync(Guid id, CancellationToken ct) => TransitionAsync(id, q => q.Open(clock.UtcNow), ct);
    public Task<Result<QueueDto>> PauseAsync(Guid id, CancellationToken ct) => TransitionAsync(id, q => q.Pause(clock.UtcNow), ct);
    public Task<Result<QueueDto>> CloseAsync(Guid id, CancellationToken ct) => TransitionAsync(id, q => q.Close(clock.UtcNow), ct);
    public async Task<Result<TicketDto>> IssueAsync(string publicId, TicketPriority priority, CancellationToken ct) { var ticket = await tickets.IssueAsync(publicId, priority, ct); return ticket is null ? Result.Failure<TicketDto>(new("ticket.queue_unavailable", "Queue is unavailable.")) : Result.Success(new TicketDto(ticket.Id, ticket.TicketNumber, ticket.Status, ticket.IssuedAt, 0, 0)); }
    public async Task<Result<TicketDto>> CallNextAsync(Guid queueId, Guid counterId, CancellationToken ct) { var userId = currentUser.UserId ?? throw new UnauthorizedAccessException(); var ticket = await tickets.CallNextAsync(Tenant, queueId, counterId, userId, ct); return ticket is null ? Result.Failure<TicketDto>(new("ticket.none_waiting", "No eligible ticket is waiting.")) : Result.Success(new TicketDto(ticket.Id, ticket.TicketNumber, ticket.Status, ticket.IssuedAt, 0, 0)); }
    public async Task<Result<TicketDto>> GetPublicAsync(string token, CancellationToken ct) { var ticket = await db.QueueTickets.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CustomerPublicToken == token, ct); if (ticket is null) return Result.Failure<TicketDto>(new("ticket.not_found", "Ticket was not found.")); var ahead = await db.QueueTickets.IgnoreQueryFilters().CountAsync(x => x.OrganizationId == ticket.OrganizationId && x.QueueId == ticket.QueueId && x.Status == TicketStatus.Waiting && x.SequenceNumber < ticket.SequenceNumber, ct); var average = await db.Services.IgnoreQueryFilters().Where(x => x.Id == ticket.ServiceId).Select(x => x.AverageDurationMinutes).SingleAsync(ct); return Result.Success(new TicketDto(ticket.Id, ticket.TicketNumber, ticket.Status, ticket.IssuedAt, ahead, ahead * average)); }
    public Task<Result<TicketDto>> StartAsync(Guid id, CancellationToken ct) => TicketTransitionAsync(id, x => x.Start(clock.UtcNow), null, ct);
    public Task<Result<TicketDto>> CompleteAsync(Guid id, CancellationToken ct) => TicketTransitionAsync(id, x => x.Complete(clock.UtcNow), null, ct);
    public Task<Result<TicketDto>> CancelAsync(Guid id, string? reason, CancellationToken ct) => TicketTransitionAsync(id, x => x.Cancel(clock.UtcNow), reason, ct);
    public Task<Result<TicketDto>> NoShowAsync(Guid id, CancellationToken ct) => TicketTransitionAsync(id, x => x.MarkNoShow(clock.UtcNow), null, ct);
    private async Task<Result<QueueDto>> TransitionAsync(Guid id, Action<QueueFlowQueue> transition, CancellationToken ct) { var queue = await db.Queues.SingleOrDefaultAsync(x => x.Id == id, ct); if (queue is null) return Result.Failure<QueueDto>(new("queue.not_found", "Queue was not found.")); transition(queue); await db.SaveChangesAsync(ct); return Result.Success(Map(queue)); }
    private async Task<Result<TicketDto>> TicketTransitionAsync(Guid id, Action<QueueTicket> transition, string? reason, CancellationToken ct) { var ticket = await db.QueueTickets.SingleOrDefaultAsync(x => x.Id == id, ct); if (ticket is null) return Result.Failure<TicketDto>(new("ticket.not_found", "Ticket was not found.")); transition(ticket); db.TicketEvents.Add(new(Guid.NewGuid(), Tenant, ticket.Id, ticket.Status, reason, clock.UtcNow)); await db.SaveChangesAsync(ct); return Result.Success(new TicketDto(ticket.Id, ticket.TicketNumber, ticket.Status, ticket.IssuedAt, 0, 0)); }
    private static QueueDto Map(QueueFlowQueue q) => new(q.Id, q.BranchId, q.ServiceId, q.Name, q.PublicId, q.Status);
}
