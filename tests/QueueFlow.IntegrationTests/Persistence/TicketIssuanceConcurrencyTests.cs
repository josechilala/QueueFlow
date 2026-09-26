using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Abstractions.Clock;
using QueueFlow.Application.Abstractions.Persistence;
using QueueFlow.Application.Abstractions.Realtime;
using QueueFlow.Application.Features.Queues;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;
using QueueFlow.Infrastructure.Persistence;
using QueueFlow.IntegrationTests.Health;

namespace QueueFlow.IntegrationTests.Persistence;

public sealed class TicketIssuanceConcurrencyTests : IClassFixture<QueueFlowApiFactory>
{
    private readonly QueueFlowApiFactory _factory;

    public TicketIssuanceConcurrencyTests(QueueFlowApiFactory factory) => _factory = factory;

    [Fact]
    public async Task SimultaneousIssuesReceiveDistinctIncreasingSequences()
    {
        var client = _factory.CreateClient();
        var services = _factory.Services;
        await using var setupScope = services.CreateAsyncScope();
        var setupDb = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (!await setupDb.Database.CanConnectAsync(TestContext.Current.CancellationToken))
        {
            Assert.Skip("A configured QueueFlow PostgreSQL database is required for the concurrency test.");
        }
        if ((await setupDb.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken)).Any())
        {
            Assert.Skip("Apply pending QueueFlow migrations to the test database before running the concurrency test.");
        }

        var now = DateTimeOffset.UtcNow;
        var organization = new Organization(Guid.NewGuid(), "Concurrency Test", $"concurrency-{Guid.NewGuid():N}", "UTC", now);
        var branch = new Branch(Guid.NewGuid(), organization.Id, "Test Branch", "UTC", now);
        var service = new Service(Guid.NewGuid(), organization.Id, branch.Id, "Test Service", null, "T", 5, now);
        var counter = new QueueCounter(Guid.NewGuid(), organization.Id, branch.Id, "Counter 1", now);
        var queue = new QueueFlow.Domain.Entities.Queue(Guid.NewGuid(), organization.Id, branch.Id, service.Id, "Test Queue", null, now);
        queue.Open(now);
        setupDb.AddRange(organization, branch, service, counter, queue);
        await setupDb.SaveChangesAsync(TestContext.Current.CancellationToken);

        try
        {
            var issues = Enumerable.Range(0, 20).Select(async _ =>
            {
                await using var scope = services.CreateAsyncScope();
                var operations = scope.ServiceProvider.GetRequiredService<ITicketOperations>();
                return await operations.IssueAsync(queue.PublicId, TicketPriority.Normal, TestContext.Current.CancellationToken);
            });

            var tickets = await Task.WhenAll(issues);
            Assert.DoesNotContain(tickets, ticket => ticket is null);
            Assert.Equal(20, tickets.Select(ticket => ticket!.SequenceNumber).Distinct().Count());
            Assert.Equal(Enumerable.Range(1, 20).Select(value => (long)value), tickets.Select(ticket => ticket!.SequenceNumber).Order());
            Assert.Equal(20, tickets.Select(ticket => ticket!.TicketNumber).Distinct().Count());
            Assert.Equal(20, tickets.Select(ticket => ticket!.CustomerPublicToken).Distinct().Count());
            Assert.All(tickets, ticket => Assert.Matches("^T-[0-9]{3,}$", ticket!.TicketNumber));

            var persisted = await setupDb.QueueTickets.IgnoreQueryFilters().AsNoTracking()
                .Where(ticket => ticket.OrganizationId == organization.Id && ticket.QueueId == queue.Id)
                .OrderBy(ticket => ticket.SequenceNumber)
                .Select(ticket => new { ticket.SequenceNumber, ticket.TicketNumber, ticket.Status, ticket.IssuedAt })
                .ToListAsync(TestContext.Current.CancellationToken);
            Assert.Equal(20, persisted.Count);
            Assert.Equal(Enumerable.Range(1, 20).Select(value => (long)value), persisted.Select(ticket => ticket.SequenceNumber));
            Assert.All(persisted, ticket => { Assert.Equal(TicketStatus.Waiting, ticket.Status); Assert.NotEqual(default, ticket.IssuedAt); });

            var issuedTicket = tickets[0]!;
            using var publicResponse = await client.GetAsync($"/api/v1/public/tickets/{issuedTicket.CustomerPublicToken}", TestContext.Current.CancellationToken);
            publicResponse.EnsureSuccessStatusCode();
            var publicTicket = await publicResponse.Content.ReadFromJsonAsync<PublicTicketResponse>(TestContext.Current.CancellationToken);
            Assert.NotNull(publicTicket);
            Assert.Equal(issuedTicket.TicketNumber, publicTicket.TicketNumber);
            Assert.Equal("Waiting", publicTicket.Status);
            Assert.True(publicTicket.Position >= 1);

            var attendantId = Guid.NewGuid();
            var operations = setupScope.ServiceProvider.GetRequiredService<ITicketOperations>();
            var called = await operations.CallNextAsync(organization.Id, queue.Id, counter.Id, attendantId, TestContext.Current.CancellationToken);
            Assert.NotNull(called);
            var testUser = new TestCurrentUser(attendantId, organization.Id);
            var workflowOptions = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(setupDb.Database.GetConnectionString()).Options;
            await using var workflowDb = new ApplicationDbContext(workflowOptions, testUser);
            var workflow = new QueueOperationsService(workflowDb, new UnusedTicketOperations(), testUser, new TestClock(now.AddMinutes(1)), new NullRealtimeNotifier());
            var started = await workflow.StartAsync(called.Id, TestContext.Current.CancellationToken);
            Assert.True(started.IsSuccess);
            var completed = await workflow.CompleteAsync(called.Id, TestContext.Current.CancellationToken);
            Assert.True(completed.IsSuccess);

            var lifecycleEvents = await setupDb.TicketEvents.IgnoreQueryFilters().AsNoTracking()
                .Where(item => item.TicketId == called.Id)
                .OrderBy(item => item.CreatedAt)
                .Select(item => item.Status)
                .ToListAsync(TestContext.Current.CancellationToken);
            Assert.Equal([TicketStatus.Waiting, TicketStatus.Called, TicketStatus.InService, TicketStatus.Completed], lifecycleEvents);
        }
        finally
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            await setupDb.TicketEvents.IgnoreQueryFilters().Where(item => item.OrganizationId == organization.Id).ExecuteDeleteAsync(cancellationToken);
            await setupDb.QueueTickets.IgnoreQueryFilters().Where(item => item.OrganizationId == organization.Id).ExecuteDeleteAsync(cancellationToken);
            await setupDb.Queues.IgnoreQueryFilters().Where(item => item.OrganizationId == organization.Id).ExecuteDeleteAsync(cancellationToken);
            await setupDb.QueueCounters.IgnoreQueryFilters().Where(item => item.OrganizationId == organization.Id).ExecuteDeleteAsync(cancellationToken);
            await setupDb.Services.IgnoreQueryFilters().Where(item => item.OrganizationId == organization.Id).ExecuteDeleteAsync(cancellationToken);
            await setupDb.Branches.IgnoreQueryFilters().Where(item => item.OrganizationId == organization.Id).ExecuteDeleteAsync(cancellationToken);
            await setupDb.Organizations.Where(item => item.Id == organization.Id).ExecuteDeleteAsync(cancellationToken);
        }
    }

    private sealed record PublicTicketResponse(string TicketNumber, string Status, int Position);

    private sealed class TestCurrentUser(Guid userId, Guid organizationId) : ICurrentUser
    {
        public Guid? UserId => userId;
        public Guid? OrganizationId => organizationId;
        public UserRole? Role => UserRole.Attendant;
        public IdentityType? IdentityType => QueueFlow.Domain.Enums.IdentityType.Tenant;
        public bool IsAuthenticated => true;
    }

    private sealed class TestClock(DateTimeOffset now) : IClock
    {
        private DateTimeOffset _now = now;
        public DateTimeOffset UtcNow => _now = _now.AddSeconds(1);
    }

    private sealed class UnusedTicketOperations : ITicketOperations
    {
        public Task<QueueTicket?> IssueAsync(string queuePublicId, TicketPriority priority, CancellationToken cancellationToken) => Task.FromResult<QueueTicket?>(null);
        public Task<int> GetTicketsAheadAsync(Guid organizationId, Guid queueId, Guid ticketId, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<QueueTicket?> CallNextAsync(Guid organizationId, Guid queueId, Guid counterId, Guid attendantId, CancellationToken cancellationToken) => Task.FromResult<QueueTicket?>(null);
    }

    private sealed class NullRealtimeNotifier : IQueueRealtimeNotifier
    {
        public Task QueueEventAsync(string queuePublicId, string eventName, object payload, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task TicketEventAsync(string ticketPublicToken, string eventName, object payload, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
