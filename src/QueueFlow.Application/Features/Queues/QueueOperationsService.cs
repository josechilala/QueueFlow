using Microsoft.EntityFrameworkCore;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Abstractions.Clock;
using QueueFlow.Application.Abstractions.Persistence;
using QueueFlow.Application.Abstractions.Realtime;
using QueueFlow.Application.Common;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;
using QueueFlowQueue = QueueFlow.Domain.Entities.Queue;
using System.Text.Json;
using QueueFlow.Application.Abstractions.Auditing;
using QueueFlow.Application.Observability;

namespace QueueFlow.Application.Features.Queues;

public sealed record QueueDto(Guid Id, Guid BranchId, Guid ServiceId, string Name, string PublicId, QueueStatus Status, int? Capacity, bool IsActive);
public sealed record PublicQueueDto(string PublicId, string Name, string OrganizationName, string BranchName, string ServiceName, QueueStatus Status, int WaitingCount, int ActiveAttendants, int EstimatedWaitMinutes, bool AcceptsNewTickets);
public sealed record PublicBranchQueueDto(string PublicId, string Name, string ServiceName, QueueStatus Status, int WaitingCount, int EstimatedWaitMinutes, bool AcceptsNewTickets);
public sealed record PublicBranchServiceDto(string PublicId, string Name, ServiceAttendanceMode AttendanceMode);
public sealed record PublicBranchDto(string PublicId, string OrganizationName, string Name, IReadOnlyList<PublicBranchQueueDto> Queues, IReadOnlyList<PublicBranchServiceDto> AppointmentServices);
public sealed record PublicOrganizationServiceDto(string PublicId, string Name, string? Description, int AverageDurationMinutes, ServiceAttendanceMode AttendanceMode, string? QueuePublicId, bool CanJoinQueue, bool CanSchedule);
public sealed record PublicOrganizationBranchDto(string PublicId, string Name, string? Address, string TimeZone, IReadOnlyList<PublicOrganizationServiceDto> Services);
public sealed record PublicOrganizationDto(string Slug, string Name, IReadOnlyList<PublicOrganizationBranchDto> Branches);
public sealed record PublicServiceLandingDto(string OrganizationSlug, string OrganizationName, string BranchPublicId, string BranchName, string PublicId, string Name, string? Description, int AverageDurationMinutes, ServiceAttendanceMode AttendanceMode, string? QueuePublicId, bool CanJoinQueue, bool CanSchedule);
public sealed record TicketDto(Guid Id, string TicketNumber, string? CustomerPublicToken, TicketStatus Status, DateTimeOffset IssuedAt, int TicketsAhead, int EstimatedMinutes);
public sealed record PublicTicketDto(string TicketNumber, TicketStatus Status, DateTimeOffset IssuedAt, int Position, int TicketsAhead, int EstimatedMinutes, string? CounterName);
public sealed record OperationalBranchDto(Guid Id, string Name);
public sealed record OperationalQueueDto(Guid Id, Guid BranchId, string Name, QueueStatus Status, int WaitingCount);
public sealed record OperationalCounterDto(Guid Id, Guid BranchId, string Name);
public sealed record OperationalTicketDto(Guid Id, Guid QueueId, Guid? CounterId, string TicketNumber, TicketStatus Status, string? CounterName);
public sealed record AttendantContextDto(IReadOnlyList<OperationalBranchDto> Branches, IReadOnlyList<OperationalQueueDto> Queues, IReadOnlyList<OperationalCounterDto> Counters, OperationalTicketDto? CurrentTicket);
public sealed record DisplayCallDto(string TicketNumber, string CounterName, DateTimeOffset CalledAt);
public sealed record PublicDisplayDto(string BranchPublicId, string OrganizationName, string BranchName, IReadOnlyList<string> QueuePublicIds, IReadOnlyList<DisplayCallDto> LatestCalls);
public sealed record PublicNotificationDto(Guid Id, string Message, DateTimeOffset CreatedAt, bool IsRead);

public sealed class QueueOperationsService(IApplicationDbContext db, ITicketOperations tickets, ICurrentUser currentUser, IClock clock, IQueueRealtimeNotifier realtime, IAuditWriter? audit = null)
{
    private Guid Tenant => currentUser.OrganizationId ?? throw new UnauthorizedAccessException();
    public async Task<Result<QueueDto>> CreateAsync(Guid branchId, Guid serviceId, string name, int? capacity, CancellationToken ct)
    {
        var catalogExists = await db.Branches.AnyAsync(x => x.Id == branchId && x.IsActive, ct)
            && await db.Services.AnyAsync(x => x.Id == serviceId && x.BranchId == branchId && x.IsActive, ct);
        if (!catalogExists) return Result.Failure<QueueDto>(new("queue.catalog_not_found", "A unidade ou o serviço não foi encontrado, está inativo ou não possui o vínculo informado."));
        var queue = new QueueFlowQueue(Guid.NewGuid(), Tenant, branchId, serviceId, name, capacity, clock.UtcNow);
        db.Queues.Add(queue); await db.SaveChangesAsync(ct); return Result.Success(Map(queue));
    }
    public async Task<IReadOnlyList<QueueDto>> ListAsync(CancellationToken ct) => await db.Queues.AsNoTracking().OrderBy(x => x.Name).Select(x => new QueueDto(x.Id, x.BranchId, x.ServiceId, x.Name, x.PublicId, x.Status, x.Capacity, x.IsActive)).ToListAsync(ct);
    public async Task<Result<QueueDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var queue = await db.Queues.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return queue is null ? Result.Failure<QueueDto>(new("queue.not_found", "A fila não foi encontrada.")) : Result.Success(Map(queue));
    }
    public async Task<Result<PublicQueueDto>> GetPublicQueueAsync(string publicId, CancellationToken ct)
    {
        if (publicId.Length != 32 || !publicId.All(Uri.IsHexDigit)) return Result.Failure<PublicQueueDto>(new("queue.not_found", "A fila não foi encontrada."));
        var queue = await db.Queues.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.PublicId == publicId, ct);
        if (queue is null) return Result.Failure<PublicQueueDto>(new("queue.not_found", "A fila não foi encontrada."));
        var organizationName = await db.Organizations.AsNoTracking().Where(x => x.Id == queue.OrganizationId).Select(x => x.Name).SingleAsync(ct);
        var branchName = await db.Branches.IgnoreQueryFilters().Where(x => x.Id == queue.BranchId).Select(x => x.Name).SingleAsync(ct);
        var service = await db.Services.IgnoreQueryFilters().Where(x => x.Id == queue.ServiceId).Select(x => new { x.Name, x.AverageDurationMinutes, x.IsActive }).SingleAsync(ct);
        var waitingCount = await db.QueueTickets.IgnoreQueryFilters().CountAsync(x => x.OrganizationId == queue.OrganizationId && x.QueueId == queue.Id && x.Status == TicketStatus.Waiting, ct);
        var activeAttendants = await ActiveAttendantsAsync(queue.Id, ignoreQueryFilters: true, ct);
        var acceptsNewTickets = queue.IsActive && queue.Status == QueueStatus.Open && service.IsActive && (queue.Capacity is null || waitingCount < queue.Capacity.Value);
        return Result.Success(new PublicQueueDto(queue.PublicId, queue.Name, organizationName, branchName, service.Name, queue.Status, waitingCount, activeAttendants, WaitTimeEstimator.Calculate(waitingCount, service.AverageDurationMinutes, activeAttendants), acceptsNewTickets));
    }
    public async Task<Result<PublicBranchDto>> GetPublicBranchAsync(string publicId, CancellationToken ct)
    {
        if (publicId.Length != 32 || !publicId.All(Uri.IsHexDigit)) return Result.Failure<PublicBranchDto>(new("branch.not_found", "A unidade não foi encontrada."));
        var branch = await db.Branches.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.PublicId == publicId && x.IsActive, ct);
        if (branch is null) return Result.Failure<PublicBranchDto>(new("branch.not_found", "A unidade não foi encontrada."));
        var organizationName = await db.Organizations.AsNoTracking().Where(x => x.Id == branch.OrganizationId).Select(x => x.Name).SingleAsync(ct);
        var rows = await (from queue in db.Queues.IgnoreQueryFilters().AsNoTracking()
                          join service in db.Services.IgnoreQueryFilters().AsNoTracking() on queue.ServiceId equals service.Id
                          where queue.BranchId == branch.Id && queue.IsActive && service.IsActive
                          orderby service.Name, queue.Name
                          select new { Queue = queue, ServiceName = service.Name, service.AverageDurationMinutes }).ToListAsync(ct);
        var queues = new List<PublicBranchQueueDto>(rows.Count);
        foreach (var row in rows)
        {
            var waitingCount = await db.QueueTickets.IgnoreQueryFilters().CountAsync(x => x.OrganizationId == branch.OrganizationId && x.QueueId == row.Queue.Id && x.Status == TicketStatus.Waiting, ct);
            var activeAttendants = await ActiveAttendantsAsync(row.Queue.Id, ignoreQueryFilters: true, ct);
            var acceptsNewTickets = row.Queue.Status == QueueStatus.Open && (row.Queue.Capacity is null || waitingCount < row.Queue.Capacity.Value);
            queues.Add(new(row.Queue.PublicId, row.Queue.Name, row.ServiceName, row.Queue.Status, waitingCount, WaitTimeEstimator.Calculate(waitingCount, row.AverageDurationMinutes, activeAttendants), acceptsNewTickets));
        }
        var appointmentServices = await db.Services.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.OrganizationId == branch.OrganizationId && x.BranchId == branch.Id && x.IsActive && x.AttendanceMode != ServiceAttendanceMode.QueueOnly)
            .OrderBy(x => x.Name).Select(x => new PublicBranchServiceDto(x.PublicId, x.Name, x.AttendanceMode)).ToListAsync(ct);
        return Result.Success(new PublicBranchDto(branch.PublicId, organizationName, branch.Name, queues, appointmentServices));
    }
    public async Task<Result<PublicOrganizationDto>> GetPublicOrganizationAsync(string slug, CancellationToken ct)
    {
        var normalizedSlug = slug.Trim().ToLowerInvariant();
        if (normalizedSlug.Length is < 2 or > 100 || normalizedSlug.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-')) return OrganizationNotFound();
        var organization = await db.Organizations.AsNoTracking().SingleOrDefaultAsync(x => x.Slug == normalizedSlug && x.IsActive, ct);
        if (organization is null) return OrganizationNotFound();
        var branches = await db.Branches.IgnoreQueryFilters().AsNoTracking().Where(x => x.OrganizationId == organization.Id && x.IsActive).OrderBy(x => x.Name).ToListAsync(ct);
        var branchIds = branches.Select(x => x.Id).ToArray();
        var services = await db.Services.IgnoreQueryFilters().AsNoTracking().Where(x => x.OrganizationId == organization.Id && x.IsActive && branchIds.Contains(x.BranchId)).OrderBy(x => x.Name).ToListAsync(ct);
        var serviceIds = services.Select(x => x.Id).ToArray();
        var queues = await db.Queues.IgnoreQueryFilters().AsNoTracking().Where(x => x.OrganizationId == organization.Id && x.IsActive && serviceIds.Contains(x.ServiceId)).OrderBy(x => x.Name).ToListAsync(ct);
        var schedulingServiceIds = await db.ServiceSchedulingSettings.IgnoreQueryFilters().AsNoTracking().Where(x => x.OrganizationId == organization.Id && x.IsActive && serviceIds.Contains(x.ServiceId)).Select(x => x.ServiceId).ToListAsync(ct);
        var waitingCounts = await db.QueueTickets.IgnoreQueryFilters().AsNoTracking().Where(x => x.OrganizationId == organization.Id && x.Status == TicketStatus.Waiting).GroupBy(x => x.QueueId).Select(group => new { QueueId = group.Key, Count = group.Count() }).ToDictionaryAsync(x => x.QueueId, x => x.Count, ct);
        var result = branches.Select(branch => new PublicOrganizationBranchDto(branch.PublicId, branch.Name, branch.Address, branch.TimeZone,
            services.Where(service => service.BranchId == branch.Id).Select(service =>
            {
                var queue = queues.FirstOrDefault(value => value.ServiceId == service.Id);
                var canJoin = service.AttendanceMode != ServiceAttendanceMode.AppointmentOnly && queue is not null && queue.Status == QueueStatus.Open && (queue.Capacity is null || waitingCounts.GetValueOrDefault(queue.Id) < queue.Capacity.Value);
                var canSchedule = service.AttendanceMode != ServiceAttendanceMode.QueueOnly && schedulingServiceIds.Contains(service.Id);
                return new PublicOrganizationServiceDto(service.PublicId, service.Name, service.Description, service.AverageDurationMinutes, service.AttendanceMode, queue?.PublicId, canJoin, canSchedule);
            }).ToArray())).ToArray();
        return Result.Success(new PublicOrganizationDto(organization.Slug, organization.Name, result));
    }
    public async Task<Result<PublicServiceLandingDto>> GetPublicServiceAsync(string publicId, CancellationToken ct)
    {
        if (publicId.Length != 32 || !publicId.All(Uri.IsHexDigit)) return ServiceNotFound();
        var service = await db.Services.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.PublicId == publicId && x.IsActive, ct);
        if (service is null) return ServiceNotFound();
        var branch = await db.Branches.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Id == service.BranchId && x.OrganizationId == service.OrganizationId && x.IsActive, ct);
        var organization = await db.Organizations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == service.OrganizationId && x.IsActive, ct);
        if (branch is null || organization is null) return ServiceNotFound();
        var queue = await db.Queues.IgnoreQueryFilters().AsNoTracking().Where(x => x.OrganizationId == organization.Id && x.ServiceId == service.Id && x.IsActive).OrderBy(x => x.Name).FirstOrDefaultAsync(ct);
        var waitingCount = queue is null ? 0 : await db.QueueTickets.IgnoreQueryFilters().CountAsync(x => x.OrganizationId == organization.Id && x.QueueId == queue.Id && x.Status == TicketStatus.Waiting, ct);
        var canJoin = service.AttendanceMode != ServiceAttendanceMode.AppointmentOnly && queue is not null && queue.Status == QueueStatus.Open && (queue.Capacity is null || waitingCount < queue.Capacity.Value);
        var canSchedule = service.AttendanceMode != ServiceAttendanceMode.QueueOnly && await db.ServiceSchedulingSettings.IgnoreQueryFilters().AnyAsync(x => x.OrganizationId == organization.Id && x.ServiceId == service.Id && x.IsActive, ct);
        return Result.Success(new PublicServiceLandingDto(organization.Slug, organization.Name, branch.PublicId, branch.Name, service.PublicId, service.Name, service.Description, service.AverageDurationMinutes, service.AttendanceMode, queue?.PublicId, canJoin, canSchedule));
    }
    public async Task<Result<PublicDisplayDto>> GetPublicDisplayAsync(string branchPublicId, CancellationToken ct)
    {
        if (branchPublicId.Length != 32 || !branchPublicId.All(Uri.IsHexDigit)) return Result.Failure<PublicDisplayDto>(new("display.not_found", "A unidade não foi encontrada."));
        var branch = await db.Branches.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.PublicId == branchPublicId && x.IsActive, ct);
        if (branch is null) return Result.Failure<PublicDisplayDto>(new("display.not_found", "A unidade não foi encontrada."));
        var organizationName = await db.Organizations.AsNoTracking().Where(x => x.Id == branch.OrganizationId).Select(x => x.Name).SingleAsync(ct);
        var queuePublicIds = await db.Queues.IgnoreQueryFilters().AsNoTracking().Where(x => x.BranchId == branch.Id && x.IsActive).Select(x => x.PublicId).ToListAsync(ct);
        var rows = await db.QueueTickets.IgnoreQueryFilters().AsNoTracking().Where(x => x.BranchId == branch.Id && x.CalledAt != null && x.CounterId != null).OrderByDescending(x => x.CalledAt).Take(10).Select(x => new { x.TicketNumber, x.CounterId, CalledAt = x.CalledAt!.Value }).ToListAsync(ct);
        var counterIds = rows.Select(x => x.CounterId!.Value).Distinct().ToArray();
        var counterNames = await db.QueueCounters.IgnoreQueryFilters().AsNoTracking().Where(x => counterIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var latestCalls = rows.Where(x => counterNames.ContainsKey(x.CounterId!.Value)).Select(x => new DisplayCallDto(x.TicketNumber, counterNames[x.CounterId!.Value], x.CalledAt)).ToList();
        return Result.Success(new PublicDisplayDto(branch.PublicId, organizationName, branch.Name, queuePublicIds, latestCalls));
    }
    public Task<Result<QueueDto>> OpenAsync(Guid id, CancellationToken ct) => TransitionAsync(id, q => q.Open(clock.UtcNow), ct);
    public Task<Result<QueueDto>> PauseAsync(Guid id, CancellationToken ct) => TransitionAsync(id, q => q.Pause(clock.UtcNow), ct);
    public Task<Result<QueueDto>> CloseAsync(Guid id, CancellationToken ct) => TransitionAsync(id, q => q.Close(clock.UtcNow), ct);
    public async Task<Result<TicketDto>> IssueAsync(string publicId, TicketPriority priority, CancellationToken ct)
    {
        var ticket = await tickets.IssueAsync(publicId, priority, ct);
        if (ticket is null) return Result.Failure<TicketDto>(new("ticket.queue_unavailable", "A fila não está disponível ou atingiu sua capacidade."));
        var ahead = await db.QueueTickets.IgnoreQueryFilters().CountAsync(x => x.OrganizationId == ticket.OrganizationId && x.QueueId == ticket.QueueId && x.Status == TicketStatus.Waiting && x.SequenceNumber < ticket.SequenceNumber, ct);
        var average = await db.Services.IgnoreQueryFilters().Where(x => x.Id == ticket.ServiceId).Select(x => x.AverageDurationMinutes).SingleOrDefaultAsync(ct);
        var activeAttendants = await ActiveAttendantsAsync(ticket.QueueId, ignoreQueryFilters: true, ct);
        var queuePublicId = await db.Queues.IgnoreQueryFilters().Where(x => x.Id == ticket.QueueId).Select(x => x.PublicId).SingleOrDefaultAsync(ct) ?? publicId;
        var estimatedMinutes = WaitTimeEstimator.Calculate(ahead, average, activeAttendants);
        var payload = new { ticket.Id, ticket.TicketNumber, ticket.Status, ticketsAhead = ahead, estimatedMinutes };
        await realtime.QueueEventAsync(queuePublicId, "ticket.issued", payload, ct);
        await realtime.TicketEventAsync(ticket.CustomerPublicToken, "ticket.issued", payload, ct);
        return Result.Success(Map(ticket, ahead, estimatedMinutes, includePublicToken: true));
    }
    public async Task<Result<AttendantContextDto>> GetAttendantContextAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var role = currentUser.Role ?? throw new UnauthorizedAccessException();
        var allowedBranchIds = role == UserRole.Attendant
            ? db.UserBranches.Where(x => x.UserId == userId).Select(x => x.BranchId)
            : db.Branches.Select(x => x.Id);
        var branches = await db.Branches.AsNoTracking().Where(x => x.IsActive && allowedBranchIds.Contains(x.Id)).OrderBy(x => x.Name).Select(x => new OperationalBranchDto(x.Id, x.Name)).ToListAsync(ct);
        var branchIds = branches.Select(x => x.Id).ToArray();
        var waitingCounts = await db.QueueTickets.AsNoTracking().Where(x => x.Status == TicketStatus.Waiting).GroupBy(x => x.QueueId).Select(group => new { QueueId = group.Key, Count = group.Count() }).ToDictionaryAsync(x => x.QueueId, x => x.Count, ct);
        var queueRows = await db.Queues.AsNoTracking().Where(x => x.IsActive && x.Status != QueueStatus.Closed && branchIds.Contains(x.BranchId)).OrderBy(x => x.Name).Select(x => new { x.Id, x.BranchId, x.Name, x.Status }).ToListAsync(ct);
        var queues = queueRows.Select(x => new OperationalQueueDto(x.Id, x.BranchId, x.Name, x.Status, waitingCounts.GetValueOrDefault(x.Id))).ToList();
        var counters = await db.QueueCounters.AsNoTracking().Where(x => x.IsActive && branchIds.Contains(x.BranchId)).OrderBy(x => x.Name).Select(x => new OperationalCounterDto(x.Id, x.BranchId, x.Name)).ToListAsync(ct);
        var current = await db.QueueTickets.AsNoTracking().Where(x => x.AttendantUserId == userId && (x.Status == TicketStatus.Called || x.Status == TicketStatus.InService)).OrderByDescending(x => x.CalledAt).Select(x => new { x.Id, x.QueueId, x.CounterId, x.TicketNumber, x.Status }).FirstOrDefaultAsync(ct);
        string? counterName = null;
        if (current?.CounterId is not null) counterName = await db.QueueCounters.Where(x => x.Id == current.CounterId).Select(x => x.Name).SingleOrDefaultAsync(ct);
        var currentTicket = current is null ? null : new OperationalTicketDto(current.Id, current.QueueId, current.CounterId, current.TicketNumber, current.Status, counterName);
        return Result.Success(new AttendantContextDto(branches, queues, counters, currentTicket));
    }
    public async Task<Result<TicketDto>> CallNextAsync(Guid queueId, Guid counterId, CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        if (await db.QueueTickets.AnyAsync(x => x.AttendantUserId == userId && (x.Status == TicketStatus.Called || x.Status == TicketStatus.InService), ct))
            return Result.Failure<TicketDto>(new("ticket.current_exists", "Finalize o ticket atual antes de chamar o próximo."));
        var validSelection = await db.Queues.AnyAsync(x => x.Id == queueId && x.IsActive && x.Status == QueueStatus.Open, ct)
            && await db.QueueCounters.AnyAsync(x => x.Id == counterId && x.IsActive && db.Queues.Any(queue => queue.Id == queueId && queue.BranchId == x.BranchId), ct);
        if (!validSelection) return Result.Failure<TicketDto>(new("ticket.invalid_operation_context", "A fila e o guichê devem estar ativos e pertencer à mesma unidade."));
        var ticket = await tickets.CallNextAsync(Tenant, queueId, counterId, userId, ct);
        if (ticket is null) return Result.Failure<TicketDto>(new("ticket.none_waiting", "Não há tickets aguardando nesta fila."));
        QueueFlowTelemetry.Calls.Add(1);
        var onePersonAhead = await db.QueueTickets.AsNoTracking().Where(x => x.QueueId == queueId && x.Status == TicketStatus.Waiting)
            .OrderByDescending(x => x.Priority).ThenBy(x => x.IssuedAt).ThenBy(x => x.SequenceNumber).Skip(1).FirstOrDefaultAsync(ct);
        if (onePersonAhead is not null)
        {
            var notification = new Notification(Guid.NewGuid(), Tenant, onePersonAhead.CustomerPublicToken, "Falta 1 pessoa para sua vez", clock.UtcNow);
            db.Notifications.Add(notification);
            db.OutboxMessages.Add(new OutboxMessage(Guid.NewGuid(), Tenant, "notification.dispatch", JsonSerializer.Serialize(new { notificationId = notification.Id }), clock.UtcNow));
            await db.SaveChangesAsync(ct);
        }
        return Result.Success(Map(ticket));
    }
    public async Task<Result<PublicTicketDto>> GetPublicAsync(string token, CancellationToken ct)
    {
        if (token.Length != 32 || !token.All(Uri.IsHexDigit)) return Result.Failure<PublicTicketDto>(new("ticket.not_found", "A senha não foi encontrada."));
        var ticket = await db.QueueTickets.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CustomerPublicToken == token, ct);
        if (ticket is null) return Result.Failure<PublicTicketDto>(new("ticket.not_found", "A senha não foi encontrada."));
        var ahead = ticket.Status == TicketStatus.Waiting
            ? await db.QueueTickets.IgnoreQueryFilters().CountAsync(x => x.OrganizationId == ticket.OrganizationId && x.QueueId == ticket.QueueId && x.Status == TicketStatus.Waiting && x.SequenceNumber < ticket.SequenceNumber, ct)
            : 0;
        var average = await db.Services.IgnoreQueryFilters().Where(x => x.Id == ticket.ServiceId).Select(x => x.AverageDurationMinutes).SingleAsync(ct);
        var activeAttendants = await ActiveAttendantsAsync(ticket.QueueId, ignoreQueryFilters: true, ct);
        var counterName = ticket.CounterId is null ? null : await db.QueueCounters.IgnoreQueryFilters().Where(x => x.Id == ticket.CounterId).Select(x => x.Name).SingleOrDefaultAsync(ct);
        var position = ticket.Status == TicketStatus.Waiting ? ahead + 1 : 0;
        return Result.Success(new PublicTicketDto(ticket.TicketNumber, ticket.Status, ticket.IssuedAt, position, ahead, WaitTimeEstimator.Calculate(ahead, average, activeAttendants), counterName));
    }
    public async Task<Result<IReadOnlyList<PublicNotificationDto>>> GetNotificationsAsync(string token, CancellationToken ct)
    {
        if (token.Length != 32 || !token.All(Uri.IsHexDigit)) return Result.Failure<IReadOnlyList<PublicNotificationDto>>(new("ticket.not_found", "Ticket was not found."));
        var ticket = await db.QueueTickets.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CustomerPublicToken == token, ct);
        if (ticket is null) return Result.Failure<IReadOnlyList<PublicNotificationDto>>(new("ticket.not_found", "Ticket was not found."));
        var items = await db.Notifications.IgnoreQueryFilters().AsNoTracking().Where(x => x.OrganizationId == ticket.OrganizationId && x.RecipientPublicToken == token && x.Message != null)
            .OrderByDescending(x => x.CreatedAt).Take(20).Select(x => new PublicNotificationDto(x.Id, x.Message!, x.CreatedAt, x.ReadAt != null)).ToListAsync(ct);
        return Result.Success<IReadOnlyList<PublicNotificationDto>>(items);
    }

    public async Task<Result> MarkNotificationReadAsync(string token, Guid notificationId, CancellationToken ct)
    {
        var ticket = await db.QueueTickets.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CustomerPublicToken == token, ct);
        if (ticket is null) return Result.Failure(new("ticket.not_found", "Ticket was not found."));
        var notification = await db.Notifications.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == notificationId && x.OrganizationId == ticket.OrganizationId && x.RecipientPublicToken == token, ct);
        if (notification is null) return Result.Failure(new("notification.not_found", "Notification was not found."));
        notification.MarkRead(clock.UtcNow); await db.SaveChangesAsync(ct); return Result.Success();
    }

    public Task<Result<TicketDto>> StartAsync(Guid id, CancellationToken ct) => AttendantTicketTransitionAsync(id, x => x.Start(clock.UtcNow), null, ct);
    public Task<Result<TicketDto>> CompleteAsync(Guid id, CancellationToken ct) => AttendantTicketTransitionAsync(id, x => x.Complete(clock.UtcNow), null, ct);
    public Task<Result<TicketDto>> CancelAsync(Guid id, string? reason, CancellationToken ct) => TicketTransitionAsync(id, x => x.Cancel(clock.UtcNow), reason, ct);
    public Task<Result<TicketDto>> NoShowAsync(Guid id, CancellationToken ct) => AttendantTicketTransitionAsync(id, x => x.MarkNoShow(clock.UtcNow), null, ct);
    public Task<Result<TicketDto>> RecallAsync(Guid id, CancellationToken ct) => AttendantTicketTransitionAsync(id, x => x.Recall(clock.UtcNow), null, ct, "ticket.recalled");
    private async Task<Result<QueueDto>> TransitionAsync(Guid id, Action<QueueFlowQueue> transition, CancellationToken ct) { var queue = await db.Queues.SingleOrDefaultAsync(x => x.Id == id, ct); if (queue is null) return Result.Failure<QueueDto>(new("queue.not_found", "Queue was not found.")); transition(queue); audit?.Write(queue.Status switch { QueueStatus.Open => "queue.opened", QueueStatus.Paused => "queue.paused", QueueStatus.Closed => "queue.closed", _ => "queue.updated" }, "Queue", queue.Id, new { Status = queue.Status.ToString() }); await db.SaveChangesAsync(ct); await realtime.QueueEventAsync(queue.PublicId, "queue.updated", new { queue.Id, queue.Status, queue.IsActive }, ct); return Result.Success(Map(queue)); }
    private async Task<Result<TicketDto>> TicketTransitionAsync(Guid id, Action<QueueTicket> transition, string? reason, CancellationToken ct) { var ticket = await db.QueueTickets.SingleOrDefaultAsync(x => x.Id == id, ct); if (ticket is null) return Result.Failure<TicketDto>(new("ticket.not_found", "Ticket was not found.")); transition(ticket); db.TicketEvents.Add(new(Guid.NewGuid(), Tenant, ticket.Id, ticket.Status, reason, clock.UtcNow)); audit?.Write(ticket.Status == TicketStatus.Cancelled ? "ticket.cancelled_administratively" : $"ticket.{ticket.Status.ToString().ToLowerInvariant()}", "Ticket", ticket.Id, new { Reason = reason }); await db.SaveChangesAsync(ct); await PublishTicketStateAsync(ticket, EventName(ticket.Status), ct); return Result.Success(Map(ticket)); }
    private async Task<Result<TicketDto>> AttendantTicketTransitionAsync(Guid id, Action<QueueTicket> transition, string? reason, CancellationToken ct, string? eventName = null)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var ticket = await db.QueueTickets.SingleOrDefaultAsync(x => x.Id == id && x.AttendantUserId == userId, ct);
        if (ticket is null) return Result.Failure<TicketDto>(new("ticket.not_found", "O ticket não pertence ao atendente atual."));
        transition(ticket); db.TicketEvents.Add(new(Guid.NewGuid(), Tenant, ticket.Id, ticket.Status, reason, clock.UtcNow)); audit?.Write($"ticket.{ticket.Status.ToString().ToLowerInvariant()}", "Ticket", ticket.Id); await db.SaveChangesAsync(ct); await PublishTicketStateAsync(ticket, eventName ?? EventName(ticket.Status), ct); return Result.Success(Map(ticket));
    }
    private async Task PublishTicketStateAsync(QueueTicket ticket, string eventName, CancellationToken ct)
    {
        var queuePublicId = await db.Queues.IgnoreQueryFilters().Where(x => x.Id == ticket.QueueId).Select(x => x.PublicId).SingleAsync(ct);
        var counterName = ticket.CounterId is null ? null : await db.QueueCounters.IgnoreQueryFilters().Where(x => x.Id == ticket.CounterId).Select(x => x.Name).SingleOrDefaultAsync(ct);
        var payload = new { ticket.Id, ticket.TicketNumber, ticket.Status, ticket.QueueId, ticket.CounterId, CounterName = counterName };
        await realtime.TicketEventAsync(ticket.CustomerPublicToken, eventName, payload, ct);
        await realtime.QueueEventAsync(queuePublicId, eventName, payload, ct);
    }
    private static string EventName(TicketStatus status) => status switch { TicketStatus.Called => "ticket.called", TicketStatus.InService => "ticket.started", TicketStatus.Completed => "ticket.completed", TicketStatus.Cancelled => "ticket.cancelled", TicketStatus.NoShow => "ticket.no_show", _ => "queue.updated" };
    private async Task<int> ActiveAttendantsAsync(Guid queueId, bool ignoreQueryFilters, CancellationToken ct)
    {
        var ticketsQuery = ignoreQueryFilters ? db.QueueTickets.IgnoreQueryFilters() : db.QueueTickets;
        return await ticketsQuery.AsNoTracking().Where(x => x.QueueId == queueId && x.AttendantUserId != null && (x.Status == TicketStatus.Called || x.Status == TicketStatus.InService)).Select(x => x.AttendantUserId).Distinct().CountAsync(ct);
    }
    private static QueueDto Map(QueueFlowQueue q) => new(q.Id, q.BranchId, q.ServiceId, q.Name, q.PublicId, q.Status, q.Capacity, q.IsActive);
    private static Result<PublicOrganizationDto> OrganizationNotFound() => Result.Failure<PublicOrganizationDto>(new("organization.not_found", "A empresa não foi encontrada."));
    private static Result<PublicServiceLandingDto> ServiceNotFound() => Result.Failure<PublicServiceLandingDto>(new("service.not_found", "O serviço não foi encontrado."));
    private static TicketDto Map(QueueTicket ticket, int ticketsAhead = 0, int estimatedMinutes = 0, bool includePublicToken = false) => new(ticket.Id, ticket.TicketNumber, includePublicToken ? ticket.CustomerPublicToken : null, ticket.Status, ticket.IssuedAt, ticketsAhead, estimatedMinutes);
}
