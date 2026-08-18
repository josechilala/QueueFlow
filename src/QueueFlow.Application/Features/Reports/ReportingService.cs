using Microsoft.EntityFrameworkCore;
using QueueFlow.Application.Abstractions.Persistence;
using QueueFlow.Domain.Enums;

namespace QueueFlow.Application.Features.Reports;

public sealed record OperationalReport(int Waiting, int Called, int InService, int Completed, int Cancelled, double AverageWaitMinutes, double AverageServiceMinutes);

public sealed class ReportingService(IApplicationDbContext db)
{
    public async Task<OperationalReport> GetAsync(DateTimeOffset from, CancellationToken ct)
    {
        var tickets = db.QueueTickets.AsNoTracking().Where(x => x.IssuedAt >= from);
        var counts = await tickets.GroupBy(x => x.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Status, x => x.Count, ct);
        var completed = tickets.Where(x => x.CompletedAt != null && x.ServiceStartedAt != null && x.CalledAt != null);
        var averageWait = await completed.Select(x => (double)(x.CalledAt.GetValueOrDefault() - x.IssuedAt).TotalMinutes).DefaultIfEmpty().AverageAsync(ct);
        var averageService = await completed.Select(x => (double)(x.CompletedAt.GetValueOrDefault() - x.ServiceStartedAt.GetValueOrDefault()).TotalMinutes).DefaultIfEmpty().AverageAsync(ct);
        return new(Get(counts, TicketStatus.Waiting), Get(counts, TicketStatus.Called), Get(counts, TicketStatus.InService), Get(counts, TicketStatus.Completed), Get(counts, TicketStatus.Cancelled), averageWait, averageService);
    }
    private static int Get(IReadOnlyDictionary<TicketStatus, int> counts, TicketStatus status) => counts.GetValueOrDefault(status);
}
