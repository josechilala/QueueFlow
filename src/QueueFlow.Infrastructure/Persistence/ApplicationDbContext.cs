using Microsoft.EntityFrameworkCore;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Abstractions.Persistence;
using QueueFlow.Domain.Common;
using QueueFlow.Domain.Entities;
using QueueFlowQueue = QueueFlow.Domain.Entities.Queue;

namespace QueueFlow.Infrastructure.Persistence;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, ICurrentUser currentUser)
    : DbContext(options), IApplicationDbContext
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<UserBranch> UserBranches => Set<UserBranch>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<QueueCounter> QueueCounters => Set<QueueCounter>();
    public DbSet<QueueFlowQueue> Queues => Set<QueueFlowQueue>();
    public DbSet<QueueTicket> QueueTickets => Set<QueueTicket>();
    public DbSet<TicketEvent> TicketEvents => Set<TicketEvent>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Organization>(b => { b.HasIndex(x => x.Slug).IsUnique(); b.Property(x => x.Name).HasMaxLength(200); b.Property(x => x.Slug).HasMaxLength(100); });
        ConfigureTenant<Branch>(modelBuilder, x => x.OrganizationId == currentUser.OrganizationId);
        ConfigureTenant<AppUser>(modelBuilder, x => x.OrganizationId == currentUser.OrganizationId);
        ConfigureTenant<UserBranch>(modelBuilder, x => x.OrganizationId == currentUser.OrganizationId);
        ConfigureTenant<RefreshToken>(modelBuilder, x => x.OrganizationId == currentUser.OrganizationId);
        ConfigureTenant<Service>(modelBuilder, x => x.OrganizationId == currentUser.OrganizationId);
        ConfigureTenant<QueueCounter>(modelBuilder, x => x.OrganizationId == currentUser.OrganizationId);
        ConfigureTenant<QueueFlowQueue>(modelBuilder, x => x.OrganizationId == currentUser.OrganizationId);
        ConfigureTenant<QueueTicket>(modelBuilder, x => x.OrganizationId == currentUser.OrganizationId);
        ConfigureTenant<TicketEvent>(modelBuilder, x => x.OrganizationId == currentUser.OrganizationId);
        ConfigureTenant<Notification>(modelBuilder, x => x.OrganizationId == currentUser.OrganizationId);
        ConfigureTenant<Subscription>(modelBuilder, x => x.OrganizationId == currentUser.OrganizationId);
        ConfigureTenant<AuditLog>(modelBuilder, x => x.OrganizationId == currentUser.OrganizationId);
        modelBuilder.Entity<AppUser>().HasIndex(x => new { x.OrganizationId, x.Email }).IsUnique();
        modelBuilder.Entity<QueueFlowQueue>().HasIndex(x => x.PublicId).IsUnique();
        modelBuilder.Entity<QueueTicket>().HasIndex(x => new { x.OrganizationId, x.QueueId, x.Status, x.Priority, x.SequenceNumber });
        modelBuilder.Entity<QueueTicket>().Property(x => x.Version).IsRowVersion();
        modelBuilder.Entity<TicketEvent>().HasIndex(x => new { x.TicketId, x.CreatedAt });
        modelBuilder.Entity<Notification>().HasIndex(x => new { x.Status, x.NextAttemptAt });
    }

    private static void ConfigureTenant<TEntity>(ModelBuilder modelBuilder, System.Linq.Expressions.Expression<Func<TEntity, bool>> filter)
        where TEntity : class, ITenantEntity
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(filter);
        modelBuilder.Entity<TEntity>().HasIndex(x => x.OrganizationId);
    }
}
