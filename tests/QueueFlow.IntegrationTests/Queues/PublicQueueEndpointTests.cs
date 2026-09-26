using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using QueueFlow.Application.Abstractions.Persistence;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;

namespace QueueFlow.IntegrationTests.Queues;

public sealed class PublicQueueEndpointTests : IClassFixture<PublicQueueApiFactory>
{
    private readonly HttpClient _client;

    public PublicQueueEndpointTests(PublicQueueApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task IssueTicketReturnsPublicTokenNeededForStatusLookup()
    {
        using var response = await _client.PostAsJsonAsync(
            "/api/v1/public/queues/open-queue/tickets",
            new { priority = "Normal" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IssueTicketResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal("A001", body.TicketNumber);
        Assert.Equal("non-enumerable-public-token", body.CustomerPublicToken);
        Assert.Equal("Waiting", body.Status);
    }

    [Fact]
    public async Task IssueTicketForUnavailableQueueReturnsBadRequest()
    {
        using var response = await _client.PostAsJsonAsync(
            "/api/v1/public/queues/closed-queue/tickets",
            new { priority = "Normal" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SequentialPublicQueueIdIsNotAccepted()
    {
        using var response = await _client.GetAsync(
            "/api/v1/public/queues/1",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SequentialTicketTokenIsNotAccepted()
    {
        using var response = await _client.GetAsync(
            "/api/v1/public/tickets/1",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SequentialBranchDisplayIdIsNotAccepted()
    {
        using var response = await _client.GetAsync(
            "/api/v1/public/displays/1",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record IssueTicketResponse(string TicketNumber, string CustomerPublicToken, string Status);
}

public sealed class PublicQueueApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Authentication:JwtKey", "integration-test-key-with-at-least-thirty-two-characters");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ITicketOperations>();
            services.AddSingleton<ITicketOperations, StubTicketOperations>();
        });
    }

    private sealed class StubTicketOperations : ITicketOperations
    {
        public Task<QueueTicket?> IssueAsync(string queuePublicId, TicketPriority priority, CancellationToken cancellationToken)
        {
            QueueTicket? ticket = queuePublicId == "open-queue"
                ? new QueueTicket(
                    Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                    "A001", 1, priority, "non-enumerable-public-token", DateTimeOffset.UtcNow)
                : null;
            return Task.FromResult(ticket);
        }

        public Task<int> GetTicketsAheadAsync(Guid organizationId, Guid queueId, Guid ticketId, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<QueueTicket?> CallNextAsync(Guid organizationId, Guid queueId, Guid counterId, Guid attendantId, CancellationToken cancellationToken) =>
            Task.FromResult<QueueTicket?>(null);
    }
}
