using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Abstractions.Clock;
using QueueFlow.Application.Abstractions.Persistence;
using QueueFlow.Application.Abstractions.Realtime;
using QueueFlow.Application.Features.Queues;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;
using QueueFlow.Infrastructure.Persistence;

namespace QueueFlow.IntegrationTests.Queues;

public sealed class TicketBranchAuthorizationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AttendantCanCallNextInAssignedActiveBranch()
    {
        await using var test = await LocalTest.CreateAsync();
        using var client = test.Client();
        using var response = await client.PostAsJsonAsync($"/api/v1/queues/{test.Queue.Id}/call-next", new { counterId = test.Counter.Id }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = test.Context();
        var ticket = await db.QueueTickets.IgnoreQueryFilters().SingleAsync(x => x.Id == test.Ticket.Id, Ct);
        Assert.Equal(TicketStatus.Called, ticket.Status);
        Assert.Equal(test.Actor.Id, ticket.AttendantUserId);
        Assert.Equal(test.Counter.Id, ticket.CounterId);
    }

    [Theory]
    [InlineData("other_branch", HttpStatusCode.Forbidden)]
    [InlineData("removed", HttpStatusCode.Forbidden)]
    [InlineData("inactive_branch", HttpStatusCode.Forbidden)]
    [InlineData("inactive_user", HttpStatusCode.Unauthorized)]
    [InlineData("wrong_counter", HttpStatusCode.BadRequest)]
    [InlineData("foreign_queue", HttpStatusCode.BadRequest)]
    [InlineData("foreign_counter", HttpStatusCode.BadRequest)]
    public async Task DeniedCallDoesNotChangeTicketsEventsOrOtherOperationalData(string scenario, HttpStatusCode status)
    {
        await using var test = await LocalTest.CreateAsync();
        var queue = test.Queue;
        var counter = test.Counter;
        if (scenario == "other_branch") { queue = test.OtherQueue; counter = test.OtherCounter; }
        if (scenario == "foreign_queue") { queue = test.ForeignQueue; counter = test.ForeignCounter; }
        if (scenario == "wrong_counter") counter = test.OtherCounter;
        if (scenario == "foreign_counter") counter = test.ForeignCounter;
        if (scenario == "removed") await test.RemoveAssignmentAsync();
        if (scenario == "inactive_branch")
        {
            await using var db = test.Context();
            var branch = await db.Branches.IgnoreQueryFilters().SingleAsync(x => x.Id == test.Branch.Id, Ct);
            branch.SetActive(false, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(Ct);
        }
        if (scenario == "inactive_user")
        {
            await using var db = test.Context();
            var actor = await db.Users.IgnoreQueryFilters().SingleAsync(x => x.Id == test.Actor.Id, Ct);
            actor.SetActive(false, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(Ct);
        }
        var before = await test.SnapshotAsync();
        using var client = test.Client();
        using var response = await client.PostAsJsonAsync($"/api/v1/queues/{queue.Id}/call-next", new { counterId = counter.Id }, Ct);
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(before, await test.SnapshotAsync());
    }

    [Fact]
    public async Task AssignmentRemovedAfterLoadingContextIsRecheckedByMutation()
    {
        await using var test = await LocalTest.CreateAsync();
        using var client = test.Client();
        using var context = await client.GetAsync("/api/v1/operations/context", Ct);
        Assert.Equal(HttpStatusCode.OK, context.StatusCode);
        Assert.Contains(test.Queue.Id.ToString(), await context.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        await test.RemoveAssignmentAsync();
        var before = await test.SnapshotAsync();
        using var response = await client.PostAsJsonAsync($"/api/v1/queues/{test.Queue.Id}/call-next", new { counterId = test.Counter.Id }, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, await test.SnapshotAsync());
    }

    [Theory]
    [InlineData(UserRole.Owner)]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Manager)]
    public async Task ExistingTenantWideRolesCanStillCallInAnotherActiveBranch(UserRole role)
    {
        await using var test = await LocalTest.CreateAsync(role);
        using var client = test.Client();
        using var response = await client.PostAsJsonAsync($"/api/v1/queues/{test.OtherQueue.Id}/call-next", new { counterId = test.OtherCounter.Id }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(false, HttpStatusCode.Unauthorized)]
    [InlineData(true, HttpStatusCode.Forbidden)]
    public async Task AnonymousAndViewerCannotCallNext(bool authenticated, HttpStatusCode expected)
    {
        await using var test = await LocalTest.CreateAsync(UserRole.Viewer);
        using var client = test.Client(authenticated);
        var before = await test.SnapshotAsync();
        using var response = await client.PostAsJsonAsync($"/api/v1/queues/{test.Queue.Id}/call-next", new { counterId = test.Counter.Id }, Ct);
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(before, await test.SnapshotAsync());
    }

    [Theory]
    [InlineData(UserRole.Owner)]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Manager)]
    public async Task ExistingAdministrativeCancellationRemainsAllowed(UserRole role)
    {
        await using var test = await LocalTest.CreateAsync(role);
        using var client = test.Client();
        using var response = await client.PostAsJsonAsync($"/api/v1/tickets/{test.Ticket.Id}/cancel", new { reason = "Administrative cancellation" }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = test.Context();
        Assert.Equal(TicketStatus.Cancelled, await db.QueueTickets.IgnoreQueryFilters().Where(x => x.Id == test.Ticket.Id).Select(x => x.Status).SingleAsync(Ct));
        Assert.Equal(1, await db.TicketEvents.IgnoreQueryFilters().CountAsync(Ct));
        Assert.Equal(1, await db.AuditLogs.IgnoreQueryFilters().CountAsync(Ct));
    }

    [Theory]
    [InlineData("start")]
    [InlineData("complete")]
    [InlineData("no-show")]
    [InlineData("recall")]
    public async Task RemovedAttendantCannotMutatePreviouslyAssignedTicket(string action)
    {
        await using var test = await LocalTest.CreateAsync();
        await test.AssignTicketAsync(action == "complete");
        await test.RemoveAssignmentAsync();
        var before = await test.SnapshotAsync();
        using var client = test.Client();
        using var response = await client.PostAsJsonAsync($"/api/v1/tickets/{test.Ticket.Id}/{action}", new { }, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, await test.SnapshotAsync());
    }

    [Theory]
    [InlineData("start")]
    [InlineData("complete")]
    [InlineData("no-show")]
    [InlineData("recall")]
    public async Task AssignedAttendantCanStillMutateOwnTicket(string action)
    {
        await using var test = await LocalTest.CreateAsync();
        await test.AssignTicketAsync(action == "complete");
        using var client = test.Client();
        using var response = await client.PostAsJsonAsync($"/api/v1/tickets/{test.Ticket.Id}/{action}", new { }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("start")]
    [InlineData("complete")]
    [InlineData("no-show")]
    [InlineData("recall")]
    public async Task ForeignTenantTicketCannotBeMutated(string action)
    {
        await using var test = await LocalTest.CreateAsync();
        var before = await test.SnapshotAsync();
        using var client = test.Client();
        using var response = await client.PostAsJsonAsync($"/api/v1/tickets/{test.ForeignTicket.Id}/{action}", new { reason = "test" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await test.SnapshotAsync());
    }

    [Fact]
    public async Task AttendantCannotCancelAnyTicketWithoutSideEffects()
    {
        await using var test = await LocalTest.CreateAsync();
        using var client = test.Client();
        var before = await test.SnapshotAsync();
        foreach (var ticketId in new[] { test.Ticket.Id, test.ForeignTicket.Id })
        {
            using var response = await client.PostAsJsonAsync($"/api/v1/tickets/{ticketId}/cancel", new { reason = "Denied" }, Ct);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        Assert.Equal(before, await test.SnapshotAsync());
    }

    [Fact]
    public async Task DirectAttendantCancellationIsDeniedWithoutTrackedChangesOrWrites()
    {
        await using var test = await LocalTest.CreateAsync();
        var before = await test.SnapshotAsync();
        await using var scope = test.Services.CreateAsyncScope();
        await using var db = test.Context();
        var service = new QueueOperationsService(db, scope.ServiceProvider.GetRequiredService<ITicketOperations>(),
            new ActorIdentity(test.Actor), scope.ServiceProvider.GetRequiredService<IClock>(), new NoopRealtime());
        var result = await service.CancelAsync(test.Ticket.Id, "Denied direct call", Ct);
        Assert.True(result.IsFailure);
        Assert.Equal("ticket.cancel_forbidden", result.Error.Code);
        Assert.False(db.ChangeTracker.HasChanges());
        await db.SaveChangesAsync(Ct);
        Assert.Equal(before, await test.SnapshotAsync());
    }

    [Theory]
    [InlineData(UserRole.Owner)]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Manager)]
    public async Task AdministratorCannotCancelForeignTenantTicketWithoutSideEffects(UserRole role)
    {
        await using var test = await LocalTest.CreateAsync(role);
        var before = await test.SnapshotAsync();
        using var client = test.Client();
        using var response = await client.PostAsJsonAsync($"/api/v1/tickets/{test.ForeignTicket.Id}/cancel", new { reason = "Foreign tenant" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await test.SnapshotAsync());
    }

    private sealed class ActorIdentity(AppUser user) : ICurrentUser
    {
        public Guid? UserId => user.Id;
        public Guid? OrganizationId => user.OrganizationId;
        public UserRole? Role => user.Role;
        public bool IsAuthenticated => true;
    }

    private sealed class LocalTest(string connection, string schema) : IAsyncDisposable
    {
        private WebApplicationFactory<Program>? factory;
        public IServiceProvider Services => factory!.Services;
        private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
        public Guid Tenant { get; } = Guid.NewGuid();
        private Guid ForeignTenant { get; } = Guid.NewGuid();
        public AppUser Actor { get; private set; } = null!;
        public Branch Branch { get; private set; } = null!;
        public QueueFlow.Domain.Entities.Queue Queue { get; private set; } = null!;
        public QueueFlow.Domain.Entities.Queue OtherQueue { get; private set; } = null!;
        public QueueFlow.Domain.Entities.Queue ForeignQueue { get; private set; } = null!;
        public QueueCounter Counter { get; private set; } = null!;
        public QueueCounter OtherCounter { get; private set; } = null!;
        public QueueCounter ForeignCounter { get; private set; } = null!;
        public QueueTicket Ticket { get; private set; } = null!;
        public QueueTicket ForeignTicket { get; private set; } = null!;

        public ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).Options, new SetupIdentity());

        public static async Task<LocalTest> CreateAsync(UserRole role = UserRole.Attendant)
        {
            // No User Secrets/configuration fallback. This suite never runs migrations.
            var configured = Environment.GetEnvironmentVariable("QUEUEFLOW_Q02_TEST_DATABASE");
            Assert.False(string.IsNullOrWhiteSpace(configured), "Set the explicitly disposable local Q02 test connection.");
            var settings = new NpgsqlConnectionStringBuilder(configured);
            Assert.Equal("127.0.0.1", settings.Host);
            Assert.Equal("queueflow_q02_test", settings.Database);
            Assert.Equal("queueflow_q02_test", settings.Username);
            var schema = $"q02_{Guid.NewGuid():N}";
            settings.SearchPath = schema;
            settings.Pooling = false;
            var test = new LocalTest(settings.ConnectionString, schema);
            try
            {
                await using var setup = new NpgsqlConnection(settings.ConnectionString);
                await setup.OpenAsync(Ct);
                await using (var identity = new NpgsqlCommand("SELECT current_database()", setup))
                    Assert.Equal("queueflow_q02_test", await identity.ExecuteScalarAsync(Ct));
                await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", setup)) await create.ExecuteNonQueryAsync(Ct);
                // Minimal normalization dependency for ASCII fixture users. Production migrations are never invoked.
                await using (var normalize = new NpgsqlCommand("CREATE FUNCTION queueflow_normalize_email(text) RETURNS text LANGUAGE sql IMMUTABLE AS 'SELECT lower(btrim($1))'", setup))
                    await normalize.ExecuteNonQueryAsync(Ct);
                await using var db = test.Context();
                await using (var createTables = new NpgsqlCommand(db.Database.GenerateCreateScript(), setup)) await createTables.ExecuteNonQueryAsync(Ct);
                var organization = new Organization(test.Tenant, "Q02", $"q02-{test.Tenant:N}", "UTC", Now);
                var foreign = new Organization(test.ForeignTenant, "Foreign", $"foreign-{test.ForeignTenant:N}", "UTC", Now);
                test.Actor = new AppUser(Guid.NewGuid(), test.Tenant, "Test actor", "actor@example.test", "unused", role, Now);
                test.Branch = new Branch(Guid.NewGuid(), test.Tenant, "Assigned", "UTC", Now);
                var otherBranch = new Branch(Guid.NewGuid(), test.Tenant, "Other", "UTC", Now);
                var foreignBranch = new Branch(Guid.NewGuid(), test.ForeignTenant, "Foreign", "UTC", Now);
                db.AddRange(organization, foreign, test.Actor, test.Branch, otherBranch, foreignBranch,
                    new UserBranch(Guid.NewGuid(), test.Tenant, test.Actor.Id, test.Branch.Id, role));
                (test.Queue, test.Counter, test.Ticket) = AddBranchData(db, test.Branch);
                (test.OtherQueue, test.OtherCounter, _) = AddBranchData(db, otherBranch);
                (test.ForeignQueue, test.ForeignCounter, test.ForeignTicket) = AddBranchData(db, foreignBranch);
                await db.SaveChangesAsync(Ct);
                test.factory = new LocalFactory(settings.ConnectionString);
                return test;
            }
            catch { await test.DisposeAsync(); throw; }
        }

        private static (QueueFlow.Domain.Entities.Queue, QueueCounter, QueueTicket) AddBranchData(ApplicationDbContext db, Branch branch)
        {
            var service = new Service(Guid.NewGuid(), branch.OrganizationId, branch.Id, "Service", null, "A", 5, Now);
            var queue = new QueueFlow.Domain.Entities.Queue(Guid.NewGuid(), branch.OrganizationId, branch.Id, service.Id, "Queue", null, Now);
            queue.Open(Now);
            var counter = new QueueCounter(Guid.NewGuid(), branch.OrganizationId, branch.Id, "Counter", Now);
            var ticket = new QueueTicket(Guid.NewGuid(), branch.OrganizationId, branch.Id, queue.Id, service.Id, "A-001", 1,
                TicketPriority.Normal, Guid.NewGuid().ToString("N"), Now);
            db.AddRange(service, queue, counter, ticket);
            return (queue, counter, ticket);
        }

        public HttpClient Client(bool authenticated = true)
        {
            var client = factory!.CreateClient();
            if (authenticated)
            {
                var token = factory.Services.GetRequiredService<ITokenService>().CreateAccessToken(Actor.Id, Tenant, Actor.Role, Actor.Email, Actor.UpdatedAt ?? Actor.CreatedAt);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
            return client;
        }

        public async Task RemoveAssignmentAsync()
        {
            await using var db = Context();
            await db.UserBranches.IgnoreQueryFilters().Where(x => x.UserId == Actor.Id).ExecuteDeleteAsync(Ct);
        }

        public async Task AssignTicketAsync(bool inService)
        {
            await using var db = Context();
            var ticket = await db.QueueTickets.IgnoreQueryFilters().SingleAsync(x => x.Id == Ticket.Id, Ct);
            ticket.Call(Counter.Id, Actor.Id, Now);
            if (inService) ticket.Start(Now);
            await db.SaveChangesAsync(Ct);
        }

        public async Task<string> SnapshotAsync()
        {
            await using var db = Context();
            return JsonSerializer.Serialize(new
            {
                Tickets = await db.QueueTickets.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).ToListAsync(Ct),
                Queues = await db.Queues.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).ToListAsync(Ct),
                Events = await db.TicketEvents.IgnoreQueryFilters().CountAsync(Ct),
                Audit = await db.AuditLogs.IgnoreQueryFilters().CountAsync(Ct),
                Outbox = await db.OutboxMessages.IgnoreQueryFilters().CountAsync(Ct),
                Notifications = await db.Notifications.IgnoreQueryFilters().CountAsync(Ct)
            });
        }

        public async ValueTask DisposeAsync()
        {
            if (factory is not null) await factory.DisposeAsync();
            await using var cleanup = new NpgsqlConnection(connection);
            await cleanup.OpenAsync(CancellationToken.None);
            // Only the random schema created by this fixture, in the guarded disposable local database.
            await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", cleanup);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private sealed class SetupIdentity : ICurrentUser
    {
        public Guid? UserId => null;
        public Guid? OrganizationId => null;
        public UserRole? Role => null;
        public bool IsAuthenticated => false;
    }

    private sealed class LocalFactory(string connection) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Test");
            builder.UseSetting("ConnectionStrings:QueueFlowDatabase", connection);
            builder.UseSetting("Authentication:JwtKey", "q02-local-tests-key-with-at-least-thirty-two-characters");
            builder.UseSetting("Authentication:Issuer", "q02-tests");
            builder.UseSetting("Authentication:Audience", "q02-tests");
            builder.UseSetting("Asaas:Environment", "");
            builder.UseSetting("Asaas:CheckoutEnabled", "false");
            builder.UseSetting("OTEL_EXPORTER_OTLP_ENDPOINT", "");
            builder.UseSetting("Redis:UseBackplane", "false");
            builder.UseSetting("RateLimiting:UseRedis", "false");
            for (var i = 0; i < 4; i++) builder.UseSetting($"Cors:Origins:{i}", "https://q02.example.test");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IQueueRealtimeNotifier>();
                services.AddSingleton<IQueueRealtimeNotifier, NoopRealtime>();
            });
        }
    }

    private sealed class NoopRealtime : IQueueRealtimeNotifier
    {
        public Task QueueEventAsync(string queuePublicId, string eventName, object payload, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task TicketEventAsync(string ticketPublicToken, string eventName, object payload, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
