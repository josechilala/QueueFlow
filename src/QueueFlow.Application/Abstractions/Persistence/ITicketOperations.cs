using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;

namespace QueueFlow.Application.Abstractions.Persistence;

public interface ITicketOperations
{
    Task<QueueTicket?> IssueAsync(string queuePublicId, TicketPriority priority, CancellationToken cancellationToken);
    Task<QueueTicket?> CallNextAsync(Guid organizationId, Guid queueId, Guid counterId, Guid attendantId, CancellationToken cancellationToken);
}
