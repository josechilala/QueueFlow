using Microsoft.EntityFrameworkCore;
using QueueFlow.Domain.Entities;
using QueueFlowQueue = QueueFlow.Domain.Entities.Queue;

namespace QueueFlow.Application.Abstractions.Persistence;

public interface IApplicationDbContext
{
    DbSet<Organization> Organizations { get; }
    DbSet<Branch> Branches { get; }
    DbSet<AppUser> Users { get; }
    DbSet<UserBranch> UserBranches { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Service> Services { get; }
    DbSet<QueueCounter> QueueCounters { get; }
    DbSet<QueueFlowQueue> Queues { get; }
    DbSet<QueueTicket> QueueTickets { get; }
    DbSet<TicketEvent> TicketEvents { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<Subscription> Subscriptions { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<OutboxMessage> OutboxMessages { get; }
    DbSet<QueueMetricSnapshot> QueueMetricSnapshots { get; }
    DbSet<Appointment> Appointments { get; }
    DbSet<ServiceSchedule> ServiceSchedules { get; }
    DbSet<ServiceSchedulingSettings> ServiceSchedulingSettings { get; }
    DbSet<ScheduleBlock> ScheduleBlocks { get; }
    DbSet<AppointmentStatusHistory> AppointmentStatusHistory { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
