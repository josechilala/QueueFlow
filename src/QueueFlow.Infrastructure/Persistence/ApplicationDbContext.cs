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
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<QueueMetricSnapshot> QueueMetricSnapshots => Set<QueueMetricSnapshot>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<ServiceSchedule> ServiceSchedules => Set<ServiceSchedule>();
    public DbSet<ServiceSchedulingSettings> ServiceSchedulingSettings => Set<ServiceSchedulingSettings>();
    public DbSet<ScheduleBlock> ScheduleBlocks => Set<ScheduleBlock>();
    public DbSet<AppointmentStatusHistory> AppointmentStatusHistory => Set<AppointmentStatusHistory>();
    public DbSet<PlatformUser> PlatformUsers => Set<PlatformUser>();
    public DbSet<PlatformRefreshToken> PlatformRefreshTokens => Set<PlatformRefreshToken>();
    public DbSet<OrganizationInvitation> OrganizationInvitations => Set<OrganizationInvitation>();
    public DbSet<PlatformAuditLog> PlatformAuditLogs => Set<PlatformAuditLog>();
    public DbSet<TrialRequest> TrialRequests => Set<TrialRequest>();
    public async Task LockTrialRequestEmailAsync(string email, CancellationToken cancellationToken = default) =>
        await Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({email}, 710041904))", cancellationToken);
    public Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) => Database.BeginTransactionAsync(cancellationToken);
    public async Task LockPlatformBootstrapAsync(CancellationToken cancellationToken = default) => await Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(710041903)", cancellationToken);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateTenantWrites();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ValidateTenantWrites();
        try { return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505", ConstraintName: "IX_Users_Email" })
        { throw new QueueFlow.Application.Common.ConflictException("Email is already in use."); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505", ConstraintName: "UX_Queues_ActiveService" })
        { throw new DomainException("Este serviço já possui uma fila ativa nesta unidade."); }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Organization>(b => { b.HasIndex(x => x.Slug).IsUnique(); b.Property(x => x.Name).HasMaxLength(200); b.Property(x => x.Slug).HasMaxLength(100); });
        ConfigureTenant<Branch>(modelBuilder, x => x.OrganizationId == currentUser.OrganizationId);
        modelBuilder.Entity<Branch>().HasIndex(x => x.PublicId).IsUnique();
        ConfigureTenant<AppUser>(modelBuilder, x => x.OrganizationId == currentUser.OrganizationId);
        ConfigureTenant<UserBranch>(modelBuilder, x => x.OrganizationId == currentUser.OrganizationId);
        ConfigureTenant<RefreshToken>(modelBuilder, x => x.OrganizationId == currentUser.OrganizationId);
        ConfigureTenant<Service>(modelBuilder, x => x.OrganizationId == currentUser.OrganizationId);
        modelBuilder.Entity<Service>().HasIndex(x => x.PublicId).IsUnique();
        ConfigureTenant<QueueCounter>(modelBuilder, x => x.OrganizationId == currentUser.OrganizationId);
        ConfigureTenant<QueueFlowQueue>(modelBuilder, x => x.OrganizationId == currentUser.OrganizationId);
        ConfigureTenant<QueueTicket>(modelBuilder, x => x.OrganizationId == currentUser.OrganizationId);
        ConfigureTenant<TicketEvent>(modelBuilder, x => x.OrganizationId == currentUser.OrganizationId);
        ConfigureTenant<Notification>(modelBuilder, x => x.OrganizationId == currentUser.OrganizationId);
        ConfigureTenant<Subscription>(modelBuilder, x => x.OrganizationId == currentUser.OrganizationId);
        ConfigureTenant<AuditLog>(modelBuilder, x => x.OrganizationId == currentUser.OrganizationId);
        modelBuilder.Entity<AuditLog>().HasIndex(x => new { x.OrganizationId, x.CreatedAt });
        modelBuilder.Entity<AuditLog>().Property(x => x.Action).HasMaxLength(200);
        modelBuilder.Entity<AuditLog>().Property(x => x.ResourceType).HasMaxLength(200);
        modelBuilder.Entity<AuditLog>().Property(x => x.CorrelationId).HasMaxLength(200);
        ConfigureTenant<OutboxMessage>(modelBuilder, x => x.OrganizationId == currentUser.OrganizationId);
        ConfigureTenant<QueueMetricSnapshot>(modelBuilder, x => x.OrganizationId == currentUser.OrganizationId);
        ConfigureTenant<Appointment>(modelBuilder, x => x.OrganizationId == currentUser.OrganizationId);
        ConfigureTenant<ServiceSchedule>(modelBuilder, x => x.OrganizationId == currentUser.OrganizationId);
        ConfigureTenant<ServiceSchedulingSettings>(modelBuilder, x => x.OrganizationId == currentUser.OrganizationId);
        ConfigureTenant<ScheduleBlock>(modelBuilder, x => x.OrganizationId == currentUser.OrganizationId);
        ConfigureTenant<AppointmentStatusHistory>(modelBuilder, x => x.OrganizationId == currentUser.OrganizationId);
        modelBuilder.Entity<AppUser>().HasIndex(x => x.Email).IsUnique();
        modelBuilder.Entity<AppUser>().ToTable("Users", table => table.HasCheckConstraint("CK_Users_CanonicalEmail", "\"Email\" COLLATE \"C\" = queueflow_normalize_email(\"Email\") COLLATE \"C\""));
        modelBuilder.Entity<PlatformUser>(b => { b.HasIndex(x => x.Email).IsUnique(); b.Property(x => x.Name).HasMaxLength(200); b.Property(x => x.Email).HasMaxLength(320); b.Property(x => x.PasswordHash).HasMaxLength(2000); });
        modelBuilder.Entity<PlatformRefreshToken>(b => { b.HasIndex(x => x.TokenHash).IsUnique(); b.HasIndex(x => new { x.PlatformUserId, x.ExpiresAt }); b.Property(x => x.TokenHash).HasMaxLength(200); });
        modelBuilder.Entity<OrganizationInvitation>(b =>
        {
            b.HasIndex(x => x.TokenHash).IsUnique(); b.HasIndex(x => new { x.Email, x.ExpiresAt }); b.HasIndex(x => x.ActivatedOrganizationId).IsUnique().HasFilter("\"ActivatedOrganizationId\" IS NOT NULL");
            b.Property(x => x.Email).HasMaxLength(320); b.Property(x => x.ResponsibleName).HasMaxLength(200); b.Property(x => x.OrganizationName).HasMaxLength(200); b.Property(x => x.Plan).HasMaxLength(100); b.Property(x => x.TokenHash).HasMaxLength(200); b.Property(x => x.VerificationCodeHash).HasMaxLength(200); b.Property(x => x.ActivationAuthorizationHash).HasMaxLength(200);
        });
        modelBuilder.Entity<PlatformAuditLog>(b => { b.HasIndex(x => x.CreatedAt); b.HasIndex(x => new { x.PlatformUserId, x.CreatedAt }); b.Property(x => x.Action).HasMaxLength(200); b.Property(x => x.ResourceType).HasMaxLength(200); b.Property(x => x.CorrelationId).HasMaxLength(200); });
        modelBuilder.Entity<TrialRequest>(b => {
            b.HasIndex(x => x.Email).IsUnique(); b.HasIndex(x => new { x.Status, x.CreatedAt });
            b.HasIndex(x => x.InvitationId).IsUnique();
            b.HasOne<OrganizationInvitation>().WithMany().HasForeignKey(x => x.InvitationId).OnDelete(DeleteBehavior.Restrict);
            b.Property(x => x.Name).HasMaxLength(200); b.Property(x => x.Email).HasMaxLength(320);
            b.Property(x => x.CompanyName).HasMaxLength(200); b.Property(x => x.Phone).HasMaxLength(30); b.Property(x => x.TermsVersion).HasMaxLength(50);
        });
        modelBuilder.Entity<Service>(b =>
        {
            b.Property(x => x.PublicId).HasMaxLength(32);
            b.Property(x => x.Name).HasMaxLength(200);
            b.Property(x => x.Description).HasMaxLength(1000);
            b.Property(x => x.Prefix).HasMaxLength(10);
            b.Property(x => x.AttendanceMode).HasDefaultValue(QueueFlow.Domain.Enums.ServiceAttendanceMode.QueueOnly);
        });
        modelBuilder.Entity<QueueCounter>().Property(x => x.Name).HasMaxLength(200);
        modelBuilder.Entity<QueueFlowQueue>().Property(x => x.Name).HasMaxLength(200);
        modelBuilder.Entity<QueueFlowQueue>().HasIndex(x => x.PublicId).IsUnique();
        modelBuilder.Entity<QueueFlowQueue>().HasIndex(x => new { x.OrganizationId, x.BranchId, x.ServiceId })
            .HasDatabaseName("UX_Queues_ActiveService").IsUnique().HasFilter("\"IsActive\" = true");
        modelBuilder.Entity<QueueTicket>().HasIndex(x => new { x.OrganizationId, x.QueueId, x.Status, x.Priority, x.SequenceNumber });
        modelBuilder.Entity<QueueTicket>().HasIndex(x => new { x.QueueId, x.SequenceNumber }).IsUnique();
        modelBuilder.Entity<QueueTicket>().HasIndex(x => x.CustomerPublicToken).IsUnique();
        modelBuilder.Entity<QueueTicket>().Property(x => x.Version).IsRowVersion();
        modelBuilder.Entity<QueueTicket>().Property(x => x.CustomerName).HasMaxLength(200);
        modelBuilder.Entity<QueueTicket>().Property(x => x.CustomerPhone).HasMaxLength(30);
        modelBuilder.Entity<QueueTicket>().Property(x => x.CustomerPublicToken).HasMaxLength(100);
        modelBuilder.Entity<TicketEvent>().HasIndex(x => new { x.TicketId, x.CreatedAt });
        modelBuilder.Entity<Notification>().HasIndex(x => new { x.Status, x.NextAttemptAt });
        modelBuilder.Entity<Notification>().HasIndex(x => new { x.OrganizationId, x.RecipientPublicToken, x.CreatedAt });
        modelBuilder.Entity<Notification>().Property(x => x.RecipientPublicToken).HasMaxLength(100);
        modelBuilder.Entity<Notification>().Property(x => x.Message).HasMaxLength(500);
        modelBuilder.Entity<OutboxMessage>().HasIndex(x => new { x.ProcessedAt, x.NextAttemptAt });
        modelBuilder.Entity<OutboxMessage>().Property(x => x.Type).HasMaxLength(200);
        modelBuilder.Entity<OutboxMessage>().Property(x => x.LastError).HasMaxLength(1000);
        modelBuilder.Entity<QueueMetricSnapshot>().HasIndex(x => new { x.OrganizationId, x.QueueId, x.CreatedAt });
        modelBuilder.Entity<Appointment>(b =>
        {
            b.HasIndex(x => x.PublicToken).IsUnique();
            b.HasIndex(x => new { x.OrganizationId, x.BranchId, x.ServiceId, x.ScheduledStart });
            b.HasIndex(x => new { x.OrganizationId, x.Status, x.ScheduledStart });
            b.HasIndex(x => x.QueueTicketId).IsUnique().HasFilter("\"QueueTicketId\" IS NOT NULL");
            b.Property(x => x.Version).IsRowVersion();
            b.Property(x => x.CustomerName).HasMaxLength(200);
            b.Property(x => x.CustomerPhone).HasMaxLength(30);
            b.Property(x => x.CustomerEmail).HasMaxLength(320);
            b.Property(x => x.TimeZone).HasMaxLength(100);
            b.Property(x => x.PublicToken).HasMaxLength(100);
            b.Property(x => x.ConfirmationCode).HasMaxLength(20);
            b.Property(x => x.Notes).HasMaxLength(1000);
        });
        modelBuilder.Entity<ServiceSchedule>().HasIndex(x => new { x.OrganizationId, x.BranchId, x.ServiceId, x.DayOfWeek });
        modelBuilder.Entity<ServiceSchedulingSettings>().HasIndex(x => new { x.OrganizationId, x.ServiceId }).IsUnique();
        modelBuilder.Entity<ScheduleBlock>(b =>
        {
            b.HasIndex(x => new { x.OrganizationId, x.BranchId, x.StartAt, x.EndAt });
            b.Property(x => x.Reason).HasMaxLength(500);
        });
        modelBuilder.Entity<AppointmentStatusHistory>(b =>
        {
            b.HasIndex(x => new { x.AppointmentId, x.CreatedAt });
            b.Property(x => x.Reason).HasMaxLength(500);
        });
    }

    private static void ConfigureTenant<TEntity>(ModelBuilder modelBuilder, System.Linq.Expressions.Expression<Func<TEntity, bool>> filter)
        where TEntity : class, ITenantEntity
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(filter);
        modelBuilder.Entity<TEntity>().HasIndex(x => x.OrganizationId);
    }

    private void ValidateTenantWrites()
    {
        if (!currentUser.IsAuthenticated) return;
        if (currentUser.IdentityType == QueueFlow.Domain.Enums.IdentityType.Platform)
        {
            var platformWrite = ChangeTracker.Entries<ITenantEntity>().FirstOrDefault(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
            if (platformWrite is not null) throw new UnauthorizedAccessException("Platform identities cannot write tenant entities.");
            return;
        }
        if (currentUser.IdentityType != QueueFlow.Domain.Enums.IdentityType.Tenant || currentUser.OrganizationId is not Guid tenantId)
            throw new UnauthorizedAccessException("Authenticated identities must have a valid tenant context for tenant writes.");
        var invalid = ChangeTracker.Entries<ITenantEntity>().FirstOrDefault(entry =>
            entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted && entry.Entity.OrganizationId != tenantId);
        if (invalid is not null) throw new UnauthorizedAccessException("Cross-tenant writes are not allowed.");
    }
}
