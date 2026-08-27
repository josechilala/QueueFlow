using Microsoft.EntityFrameworkCore;
using QueueFlow.Application.Abstractions.Clock;
using QueueFlow.Application.Abstractions.Persistence;
using QueueFlow.Domain.Enums;
using QueueFlow.Application.Features.Queues;
using QueueFlow.Application.Abstractions.Authentication;

namespace QueueFlow.Application.Features.Reports;

public sealed record OperationalReport(int Waiting, int Called, int InService, int Completed, int Cancelled, double AverageWaitMinutes, double AverageServiceMinutes);
public sealed record QueueDashboardItem(Guid Id, string Name, string BranchName, string ServiceName, QueueStatus Status, int Waiting, int ActiveAttendants, int EstimatedWaitMinutes);
public sealed record AdministrativeDashboardSummary(string OrganizationSlug, int ActiveQueues, int Waiting, int CompletedToday, double AverageWaitMinutes, DateTimeOffset GeneratedAt, IReadOnlyList<QueueDashboardItem> QueuesInProgress);
public sealed record ReportBreakdownItem(Guid Id, string Name, int Volume);
public sealed record DailyVolumeItem(DateOnly Date, int Volume);
public sealed record HourlyVolumeItem(int Hour, int Volume);
public sealed record AppointmentReportSummary(int Total, int Scheduled, int Confirmed, int CheckedIn, int Completed, int Cancelled, int NoShows, int Rescheduled, double CheckInRatePercent);
public sealed record ManagementReport(DateTimeOffset From, DateTimeOffset To, int TotalTickets, int Completed, int NoShows, int Cancelled, double AverageWaitMinutes, double AverageServiceMinutes, AppointmentReportSummary Appointments, IReadOnlyList<DailyVolumeItem> ByDay, IReadOnlyList<ReportBreakdownItem> ByService, IReadOnlyList<ReportBreakdownItem> ByBranch, IReadOnlyList<HourlyVolumeItem> ByHour);

public sealed class ReportingService(IApplicationDbContext db, IClock clock, ICurrentUser currentUser)
{
    public async Task<AdministrativeDashboardSummary> GetDashboardAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var organizationSlug = await db.Organizations.AsNoTracking()
            .Where(x => x.Id == currentUser.OrganizationId)
            .Select(x => x.Slug)
            .SingleAsync(ct);
        var startOfTodayUtc = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var activeQueues = await db.Queues.AsNoTracking().CountAsync(x => x.Status == QueueStatus.Open, ct);
        var waiting = await db.QueueTickets.AsNoTracking().CountAsync(x => x.Status == TicketStatus.Waiting, ct);
        var completedToday = db.QueueTickets.AsNoTracking().Where(x => x.CompletedAt >= startOfTodayUtc);
        var completedCount = await completedToday.CountAsync(ct);
        var completedWaits = await completedToday
            .Where(x => x.CalledAt != null)
            .Select(x => new { x.IssuedAt, x.CalledAt })
            .ToListAsync(ct);
        var averageWait = completedWaits.Count == 0
            ? 0
            : completedWaits.Average(x => (x.CalledAt!.Value - x.IssuedAt).TotalMinutes);

        var queueRows = await db.Queues.AsNoTracking()
            .Where(x => x.IsActive && (x.Status == QueueStatus.Open || x.Status == QueueStatus.Paused))
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.BranchId, x.ServiceId, x.Name, x.Status })
            .ToListAsync(ct);
        var queueIds = queueRows.Select(x => x.Id).ToArray();
        var branchIds = queueRows.Select(x => x.BranchId).Distinct().ToArray();
        var serviceIds = queueRows.Select(x => x.ServiceId).Distinct().ToArray();
        var branchNames = await db.Branches.AsNoTracking().Where(x => branchIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var services = await db.Services.AsNoTracking().Where(x => serviceIds.Contains(x.Id)).Select(x => new { x.Id, x.Name, x.AverageDurationMinutes }).ToDictionaryAsync(x => x.Id, ct);
        var waitingByQueue = await db.QueueTickets.AsNoTracking().Where(x => queueIds.Contains(x.QueueId) && x.Status == TicketStatus.Waiting).GroupBy(x => x.QueueId).Select(group => new { QueueId = group.Key, Count = group.Count() }).ToDictionaryAsync(x => x.QueueId, x => x.Count, ct);
        var attendantsByQueue = await db.QueueTickets.AsNoTracking().Where(x => queueIds.Contains(x.QueueId) && x.AttendantUserId != null && (x.Status == TicketStatus.Called || x.Status == TicketStatus.InService)).GroupBy(x => x.QueueId).Select(group => new { QueueId = group.Key, Count = group.Select(x => x.AttendantUserId).Distinct().Count() }).ToDictionaryAsync(x => x.QueueId, x => x.Count, ct);
        var queuesInProgress = queueRows.Select(queue =>
        {
            var waitingForQueue = waitingByQueue.GetValueOrDefault(queue.Id);
            var activeAttendants = attendantsByQueue.GetValueOrDefault(queue.Id);
            var service = services[queue.ServiceId];
            var estimate = WaitTimeEstimator.Calculate(waitingForQueue, service.AverageDurationMinutes, activeAttendants);
            return new QueueDashboardItem(queue.Id, queue.Name, branchNames[queue.BranchId], service.Name, queue.Status, waitingForQueue, activeAttendants, estimate);
        }).ToList();

        return new(organizationSlug, activeQueues, waiting, completedCount, averageWait, now, queuesInProgress);
    }

    public async Task<OperationalReport> GetAsync(DateTimeOffset from, CancellationToken ct)
    {
        var tickets = db.QueueTickets.AsNoTracking().Where(x => x.IssuedAt >= from);
        var counts = await tickets.GroupBy(x => x.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Status, x => x.Count, ct);
        var completed = await tickets
            .Where(x => x.CompletedAt != null && x.ServiceStartedAt != null && x.CalledAt != null)
            .Select(x => new { x.IssuedAt, x.CalledAt, x.ServiceStartedAt, x.CompletedAt })
            .ToListAsync(ct);
        var averageWait = completed.Count == 0 ? 0 : completed.Average(x => (x.CalledAt!.Value - x.IssuedAt).TotalMinutes);
        var averageService = completed.Count == 0 ? 0 : completed.Average(x => (x.CompletedAt!.Value - x.ServiceStartedAt!.Value).TotalMinutes);
        return new(Get(counts, TicketStatus.Waiting), Get(counts, TicketStatus.Called), Get(counts, TicketStatus.InService), Get(counts, TicketStatus.Completed), Get(counts, TicketStatus.Cancelled), averageWait, averageService);
    }

    public async Task<ManagementReport> GetManagementReportAsync(DateTimeOffset from, DateTimeOffset to, Guid? branchId, Guid? serviceId, CancellationToken ct)
    {
        if (from >= to) throw new QueueFlow.Domain.Common.DomainException("The report start must be earlier than its end.");
        if (to - from > TimeSpan.FromDays(366)) throw new QueueFlow.Domain.Common.DomainException("The report period cannot exceed 366 days.");
        var query = db.QueueTickets.AsNoTracking().Where(x => x.IssuedAt >= from && x.IssuedAt < to);
        if (branchId is Guid branch) query = query.Where(x => x.BranchId == branch);
        if (serviceId is Guid service) query = query.Where(x => x.ServiceId == service);
        var tickets = await query.Select(x => new { x.BranchId, x.ServiceId, x.Status, x.IssuedAt, x.CalledAt, x.ServiceStartedAt, x.CompletedAt }).ToListAsync(ct);
        var branchIds = tickets.Select(x => x.BranchId).Distinct().ToArray(); var serviceIds = tickets.Select(x => x.ServiceId).Distinct().ToArray();
        var branches = await db.Branches.AsNoTracking().Where(x => branchIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var services = await db.Services.AsNoTracking().Where(x => serviceIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var waits = tickets.Where(x => x.CalledAt != null).Select(x => (x.CalledAt!.Value - x.IssuedAt).TotalMinutes).ToArray();
        var serviceTimes = tickets.Where(x => x.CompletedAt != null && x.ServiceStartedAt != null).Select(x => (x.CompletedAt!.Value - x.ServiceStartedAt!.Value).TotalMinutes).ToArray();
        var timeZoneId = await db.Organizations.AsNoTracking().Where(x => x.Id == currentUser.OrganizationId).Select(x => x.TimeZone).SingleOrDefaultAsync(ct) ?? "UTC";
        TimeZoneInfo timeZone; try { timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); } catch (TimeZoneNotFoundException) { timeZone = TimeZoneInfo.Utc; }
        var localTimes = tickets.Select(x => new { Ticket = x, LocalIssuedAt = TimeZoneInfo.ConvertTime(x.IssuedAt, timeZone) }).ToArray();
        var byDay = localTimes.GroupBy(x => DateOnly.FromDateTime(x.LocalIssuedAt.DateTime)).OrderBy(x => x.Key).Select(x => new DailyVolumeItem(x.Key, x.Count())).ToArray();
        var byService = tickets.GroupBy(x => x.ServiceId).OrderByDescending(x => x.Count()).Select(x => new ReportBreakdownItem(x.Key, services.GetValueOrDefault(x.Key, "Serviço removido"), x.Count())).ToArray();
        var byBranch = tickets.GroupBy(x => x.BranchId).OrderByDescending(x => x.Count()).Select(x => new ReportBreakdownItem(x.Key, branches.GetValueOrDefault(x.Key, "Unidade removida"), x.Count())).ToArray();
        var hourly = Enumerable.Range(0, 24).Select(hour => new HourlyVolumeItem(hour, localTimes.Count(x => x.LocalIssuedAt.Hour == hour))).ToArray();
        var appointmentQuery = db.Appointments.AsNoTracking().Where(x => x.ScheduledStart >= from && x.ScheduledStart < to); if (branchId is Guid appointmentBranch) appointmentQuery = appointmentQuery.Where(x => x.BranchId == appointmentBranch); if (serviceId is Guid appointmentService) appointmentQuery = appointmentQuery.Where(x => x.ServiceId == appointmentService);
        var appointmentCounts = await appointmentQuery.GroupBy(x => x.Status).Select(group => new { Status = group.Key, Count = group.Count() }).ToDictionaryAsync(x => x.Status, x => x.Count, ct); var appointmentTotal = appointmentCounts.Values.Sum(); var checkedIn = appointmentCounts.GetValueOrDefault(AppointmentStatus.CheckedIn) + appointmentCounts.GetValueOrDefault(AppointmentStatus.Completed);
        var appointments = new AppointmentReportSummary(appointmentTotal, appointmentCounts.GetValueOrDefault(AppointmentStatus.Scheduled), appointmentCounts.GetValueOrDefault(AppointmentStatus.Confirmed), appointmentCounts.GetValueOrDefault(AppointmentStatus.CheckedIn), appointmentCounts.GetValueOrDefault(AppointmentStatus.Completed), appointmentCounts.GetValueOrDefault(AppointmentStatus.Cancelled), appointmentCounts.GetValueOrDefault(AppointmentStatus.NoShow), appointmentCounts.GetValueOrDefault(AppointmentStatus.Rescheduled), appointmentTotal == 0 ? 0 : checkedIn * 100d / appointmentTotal);
        return new ManagementReport(from, to, tickets.Count, tickets.Count(x => x.Status == TicketStatus.Completed), tickets.Count(x => x.Status == TicketStatus.NoShow), tickets.Count(x => x.Status == TicketStatus.Cancelled), waits.Length == 0 ? 0 : waits.Average(), serviceTimes.Length == 0 ? 0 : serviceTimes.Average(), appointments, byDay, byService, byBranch, hourly);
    }
    private static int Get(IReadOnlyDictionary<TicketStatus, int> counts, TicketStatus status) => counts.GetValueOrDefault(status);
}
