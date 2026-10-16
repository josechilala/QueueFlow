using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Abstractions.Clock;
using QueueFlow.Application.Abstractions.Persistence;
using QueueFlow.Application.Abstractions.Realtime;
using QueueFlow.Application.Features.Queues;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;
using QueueFlow.Infrastructure.Auditing;
using QueueFlow.Infrastructure.Persistence;

namespace QueueFlow.IntegrationTests.Persistence;

public sealed class AttendantCallConcurrencyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
    public async Task SameAttendantGetsOnlyOneTicketAcrossSimultaneousCalls(bool differentBranches, int repetition)
    {
        Assert.InRange(repetition, 1, 3);
        await using var test = await LocalSchema.CreateAsync();
        var gate = new AsyncGate();
        await using var first = test.Context(test.Actor);
        await using var second = test.Context(test.Actor);
        var realtime = new RecordingRealtime();
        // Both requests must pass Application's initial active-ticket check before either enters PostgreSQL.
        var results = await Task.WhenAll(
            test.Service(first, test.Actor, realtime, gate).CallNextAsync(test.Queues[0].Id, test.Counters[0].Id, Ct),
            test.Service(second, test.Actor, realtime, gate).CallNextAsync(test.Queues[differentBranches ? 1 : 0].Id, test.Counters[differentBranches ? 1 : 0].Id, Ct));

        Assert.Single(results, x => x.IsSuccess);
        Assert.Equal("ticket.current_exists", Assert.Single(results, x => x.IsFailure).Error.Code);
        await using var check = test.Context();
        var active = await check.QueueTickets.IgnoreQueryFilters().Where(x => x.Status == TicketStatus.Called).SingleAsync(Ct);
        Assert.Equal(test.Actor, active.AttendantUserId);
        Assert.Equal(1, active.SequenceNumber);
        Assert.Equal(7, await check.QueueTickets.IgnoreQueryFilters().CountAsync(x => x.Status == TicketStatus.Waiting, Ct));
        Assert.Equal(1, await check.TicketEvents.IgnoreQueryFilters().CountAsync(Ct));
        Assert.Equal(1, await check.OutboxMessages.IgnoreQueryFilters().CountAsync(x => x.Type == "ticket.realtime", Ct));
        Assert.Equal(1, await check.OutboxMessages.IgnoreQueryFilters().CountAsync(x => x.Type == "notification.dispatch", Ct));
        Assert.Equal(1, await check.Notifications.IgnoreQueryFilters().CountAsync(Ct));
        Assert.Equal(0, await check.AuditLogs.IgnoreQueryFilters().CountAsync(Ct));
        Assert.Equal(2, realtime.Events.Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task DifferentAttendantsLockDifferentTicketsConcurrentlyAndKeepQueueOrder(int repetition)
    {
        Assert.InRange(repetition, 1, 2);
        await using var test = await LocalSchema.CreateAsync();
        var selected = new SelectedTicketGate();
        await using var first = test.Context(test.Actor, selected);
        await using var second = test.Context(test.OtherActor, selected);
        var realtime = new RecordingRealtime();
        var results = await Task.WhenAll(
            test.Service(first, test.Actor, realtime).CallNextAsync(test.Queues[0].Id, test.Counters[0].Id, Ct),
            test.Service(second, test.OtherActor, realtime).CallNextAsync(test.Queues[0].Id, test.ExtraCounter.Id, Ct));
        Assert.All(results, result => Assert.True(result.IsSuccess));
        Assert.Equal(2, results.Select(x => x.Value.Id).Distinct().Count());
        Assert.Equal(2, selected.Arrivals);
        await using var check = test.Context();
        var active = await check.QueueTickets.IgnoreQueryFilters().Where(x => x.Status == TicketStatus.Called).OrderBy(x => x.SequenceNumber).ToListAsync(Ct);
        Assert.Equal(new long[] { 1, 2 }, active.Select(x => x.SequenceNumber).ToArray());
        Assert.Equal(2, active.Select(x => x.AttendantUserId).Distinct().Count());
        Assert.Equal(2, await check.TicketEvents.IgnoreQueryFilters().CountAsync(Ct));
        Assert.Equal(2, await check.OutboxMessages.IgnoreQueryFilters().CountAsync(x => x.Type == "ticket.realtime", Ct));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TransactionalGuardRejectsActiveAttendantWithoutSideEffectsAndAllowsNextAfterExistingClosure(bool inService)
    {
        await using var test = await LocalSchema.CreateAsync();
        Guid originalId;
        await using (var seed = test.Context())
        {
            var original = await seed.QueueTickets.IgnoreQueryFilters().SingleAsync(x => x.QueueId == test.Queues[0].Id && x.SequenceNumber == 1, Ct);
            original.Call(test.Counters[0].Id, test.Actor, test.Clock.UtcNow);
            if (inService) original.Start(test.Clock.UtcNow);
            originalId = original.Id;
            await seed.SaveChangesAsync(Ct);
        }
        await using (var rejected = test.Context(test.Actor))
        {
            // Direct port call bypasses Application's early check, proving the PostgreSQL guard itself.
            Assert.Null(await new PostgresTicketOperations(rejected, test.Clock).CallNextAsync(
                test.Tenant, test.Queues[1].Id, test.Counters[1].Id, test.Actor, Ct));
        }
        await using (var check = test.Context())
        {
            Assert.Equal(7, await check.QueueTickets.IgnoreQueryFilters().CountAsync(x => x.Status == TicketStatus.Waiting, Ct));
            Assert.Equal(0, await check.TicketEvents.IgnoreQueryFilters().CountAsync(Ct));
            Assert.Equal(0, await check.OutboxMessages.IgnoreQueryFilters().CountAsync(Ct));
            Assert.Equal(0, await check.Notifications.IgnoreQueryFilters().CountAsync(Ct));
            Assert.Equal(0, await check.AuditLogs.IgnoreQueryFilters().CountAsync(Ct));
        }
        await using (var closing = test.Context(test.Actor))
        {
            var service = test.Service(closing, test.Actor, new RecordingRealtime());
            var closed = inService ? await service.CompleteAsync(originalId, Ct) : await service.NoShowAsync(originalId, Ct);
            Assert.True(closed.IsSuccess);
        }
        await using var next = test.Context(test.Actor);
        var called = await test.Service(next, test.Actor, new RecordingRealtime()).CallNextAsync(test.Queues[0].Id, test.Counters[0].Id, Ct);
        Assert.True(called.IsSuccess);
        await using var final = test.Context();
        Assert.Equal(2, (await final.QueueTickets.IgnoreQueryFilters().SingleAsync(x => x.Id == called.Value.Id, Ct)).SequenceNumber);
        Assert.Equal(inService ? TicketStatus.Completed : TicketStatus.NoShow,
            (await final.QueueTickets.IgnoreQueryFilters().SingleAsync(x => x.Id == originalId, Ct)).Status);
    }

    [Fact]
    public async Task EmptyQueueStillReturnsExistingNoWaitingResultWithoutWrites()
    {
        await using var test = await LocalSchema.CreateAsync();
        await using (var seed = test.Context())
            await seed.QueueTickets.IgnoreQueryFilters().Where(x => x.QueueId == test.Queues[0].Id).ExecuteDeleteAsync(Ct);
        await using var db = test.Context(test.Actor);
        var result = await test.Service(db, test.Actor, new RecordingRealtime()).CallNextAsync(test.Queues[0].Id, test.Counters[0].Id, Ct);
        Assert.True(result.IsFailure);
        Assert.Equal("ticket.none_waiting", result.Error.Code);
        Assert.Equal(0, await db.TicketEvents.CountAsync(Ct));
        Assert.Equal(0, await db.OutboxMessages.CountAsync(Ct));
        Assert.Equal(0, await db.Notifications.CountAsync(Ct));
        Assert.Equal(0, await db.AuditLogs.CountAsync(Ct));
    }

    private sealed class AsyncGate
    {
        private readonly TaskCompletionSource<bool> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;
        public int Arrivals => Volatile.Read(ref arrivals);
        public async Task EnterAsync(CancellationToken ct)
        {
            if (Interlocked.Increment(ref arrivals) == 2) ready.TrySetResult(true);
            await ready.Task.WaitAsync(TimeSpan.FromMinutes(2), ct);
        }
    }

    private sealed class SelectedTicketGate : DbCommandInterceptor
    {
        private readonly AsyncGate gate = new();
        public int Arrivals => gate.Arrivals;
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FOR UPDATE OF ticket SKIP LOCKED", StringComparison.Ordinal))
                await gate.EnterAsync(cancellationToken);
            return result;
        }
    }

    private sealed class GatedOperations(ITicketOperations inner, AsyncGate gate) : ITicketOperations
    {
        public async Task<QueueTicket?> CallNextAsync(Guid organizationId, Guid queueId, Guid counterId, Guid attendantId, CancellationToken cancellationToken)
        {
            await gate.EnterAsync(cancellationToken);
            return await inner.CallNextAsync(organizationId, queueId, counterId, attendantId, cancellationToken);
        }
        public Task<QueueTicket?> IssueAsync(string queuePublicId, TicketPriority priority, CancellationToken cancellationToken) => inner.IssueAsync(queuePublicId, priority, cancellationToken);
        public Task<int> GetTicketsAheadAsync(Guid organizationId, Guid queueId, Guid ticketId, CancellationToken cancellationToken) => inner.GetTicketsAheadAsync(organizationId, queueId, ticketId, cancellationToken);
    }

    private sealed class RecordingRealtime : IQueueRealtimeNotifier
    {
        public ConcurrentQueue<string> Events { get; } = new();
        public Task QueueEventAsync(string queuePublicId, string eventName, object payload, CancellationToken cancellationToken) { Events.Enqueue(eventName); return Task.CompletedTask; }
        public Task TicketEventAsync(string ticketPublicToken, string eventName, object payload, CancellationToken cancellationToken) { Events.Enqueue(eventName); return Task.CompletedTask; }
    }

    private sealed class Identity(Guid? tenant = null, Guid? user = null) : ICurrentUser
    {
        public Guid? UserId => user;
        public Guid? OrganizationId => tenant;
        public UserRole? Role => tenant is null ? null : UserRole.Attendant;
        public IdentityType? IdentityType => tenant is null ? null : QueueFlow.Domain.Enums.IdentityType.Tenant;
        public bool IsAuthenticated => tenant is not null;
    }

    private sealed class FixedClock : IClock { public DateTimeOffset UtcNow { get; } = DateTimeOffset.UtcNow; }

    private sealed class LocalSchema(string connection, string schema) : IAsyncDisposable
    {
        public Guid Tenant { get; } = Guid.NewGuid();
        public Guid Actor { get; } = Guid.NewGuid();
        public Guid OtherActor { get; } = Guid.NewGuid();
        public FixedClock Clock { get; } = new();
        public List<QueueFlow.Domain.Entities.Queue> Queues { get; } = [];
        public List<QueueCounter> Counters { get; } = [];
        public QueueCounter ExtraCounter { get; private set; } = null!;
        public ApplicationDbContext Context(Guid? actor = null, IInterceptor? interceptor = null)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection);
            if (interceptor is not null) options.AddInterceptors(interceptor);
            return new(options.Options, actor is null ? new Identity() : new Identity(Tenant, actor));
        }
        public QueueOperationsService Service(ApplicationDbContext db, Guid actor, RecordingRealtime realtime, AsyncGate? gate = null)
        {
            var identity = new Identity(Tenant, actor);
            ITicketOperations operations = new PostgresTicketOperations(db, Clock);
            if (gate is not null) operations = new GatedOperations(operations, gate);
            return new(db, operations, identity, Clock, realtime, new AuditWriter(db, identity, Clock, new HttpContextAccessor()));
        }
        public static async Task<LocalSchema> CreateAsync()
        {
            var configured = Environment.GetEnvironmentVariable("QUEUEFLOW_Q03_TEST_DATABASE");
            Assert.False(string.IsNullOrWhiteSpace(configured), "Set the explicitly disposable local Q03 connection.");
            var settings = new NpgsqlConnectionStringBuilder(configured);
            Assert.Equal("127.0.0.1", settings.Host);
            Assert.Equal("queueflow_q03_test", settings.Database);
            Assert.Equal("queueflow_q03_test", settings.Username);
            var schema = $"q03_{Guid.NewGuid():N}";
            settings.SearchPath = schema;
            settings.Pooling = false;
            var test = new LocalSchema(settings.ConnectionString, schema);
            try
            {
                await using var setup = new NpgsqlConnection(settings.ConnectionString);
                await setup.OpenAsync(Ct);
                await using (var identity = new NpgsqlCommand("SELECT current_database()", setup))
                    Assert.Equal("queueflow_q03_test", await identity.ExecuteScalarAsync(Ct));
                await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", setup)) await create.ExecuteNonQueryAsync(Ct);
                await using (var normalize = new NpgsqlCommand("CREATE FUNCTION queueflow_normalize_email(text) RETURNS text LANGUAGE sql IMMUTABLE AS 'SELECT lower(btrim($1))'", setup))
                    await normalize.ExecuteNonQueryAsync(Ct);
                await using var db = test.Context();
                // Disposable fixtures only: no migrations or real data are used.
                await using (var create = new NpgsqlCommand(db.Database.GenerateCreateScript(), setup)) await create.ExecuteNonQueryAsync(Ct);
                db.Add(new Organization(test.Tenant, "Concurrency", $"q03-{test.Tenant:N}", "UTC", test.Clock.UtcNow));
                db.AddRange(new AppUser(test.Actor, test.Tenant, "First", "first@example.test", "unused", UserRole.Attendant, test.Clock.UtcNow),
                    new AppUser(test.OtherActor, test.Tenant, "Second", "second@example.test", "unused", UserRole.Attendant, test.Clock.UtcNow));
                for (var branchIndex = 0; branchIndex < 2; branchIndex++)
                {
                    var branch = new Branch(Guid.NewGuid(), test.Tenant, $"Branch {branchIndex}", "UTC", test.Clock.UtcNow);
                    var service = new Service(Guid.NewGuid(), test.Tenant, branch.Id, "Service", null, "A", 5, test.Clock.UtcNow);
                    var queue = new QueueFlow.Domain.Entities.Queue(Guid.NewGuid(), test.Tenant, branch.Id, service.Id, "Queue", null, test.Clock.UtcNow);
                    queue.Open(test.Clock.UtcNow);
                    var counter = new QueueCounter(Guid.NewGuid(), test.Tenant, branch.Id, "Counter", test.Clock.UtcNow);
                    db.AddRange(branch, service, queue, counter,
                        new UserBranch(Guid.NewGuid(), test.Tenant, test.Actor, branch.Id, UserRole.Attendant),
                        new UserBranch(Guid.NewGuid(), test.Tenant, test.OtherActor, branch.Id, UserRole.Attendant));
                    test.Queues.Add(queue); test.Counters.Add(counter);
                    for (var sequence = 1; sequence <= 4; sequence++)
                        db.Add(new QueueTicket(Guid.NewGuid(), test.Tenant, branch.Id, queue.Id, service.Id, $"A-{sequence:000}", sequence,
                            TicketPriority.Normal, Guid.NewGuid().ToString("N"), test.Clock.UtcNow));
                }
                test.ExtraCounter = new QueueCounter(Guid.NewGuid(), test.Tenant, test.Queues[0].BranchId, "Extra", test.Clock.UtcNow);
                db.Add(test.ExtraCounter);
                await db.SaveChangesAsync(Ct);
                return test;
            }
            catch { await test.DisposeAsync(); throw; }
        }
        public async ValueTask DisposeAsync()
        {
            await using var cleanup = new NpgsqlConnection(connection);
            await cleanup.OpenAsync(CancellationToken.None);
            await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", cleanup);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
