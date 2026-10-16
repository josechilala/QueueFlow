using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Features.Billing;
using QueueFlow.Application.Observability;
using QueueFlow.Domain.Enums;
using QueueFlow.Infrastructure.Billing;
using QueueFlow.Infrastructure.Persistence;
using QueueFlow.IntegrationTests.Persistence;
using StackExchange.Redis;

namespace QueueFlow.IntegrationTests.Platform;

[CollectionDefinition("Billing polling process state", DisableParallelization = true)]
public sealed class BillingPollingProcessTestScope;

[Collection("Billing polling process state")]
public sealed class BillingWorkerStatusTests
{
    private const string Route = "/api/v1/platform/billing/inbox/worker-status";
    private static readonly string[] ResponseFields = ["ageSeconds", "lastSuccessfulPollAtUtc"];
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("anonymous", HttpStatusCode.Unauthorized)]
    [InlineData("owner", HttpStatusCode.Forbidden)]
    [InlineData("admin", HttpStatusCode.Forbidden)]
    [InlineData("platform", HttpStatusCode.OK)]
    public async Task EndpointEnforcesPlatformPolicyWithoutResolvingExternalDependencies(string identity, HttpStatusCode expected)
    {
        await using var original = new BillingEndpointSecurityTests.OfflineFactory();
        await using var factory = original.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ApplicationDbContext>();
            services.AddScoped<ApplicationDbContext>(_ => throw new InvalidOperationException("Database must not be resolved."));
            services.RemoveAll<IConnectionMultiplexer>();
            services.AddSingleton<IConnectionMultiplexer>(_ => throw new InvalidOperationException("Redis must not be resolved."));
            services.RemoveAll<AsaasSandboxClient>();
            services.AddScoped<AsaasSandboxClient>(_ => throw new InvalidOperationException("Asaas must not be resolved."));
            services.RemoveAll<IBillingWebhookDispatcher>();
            services.AddScoped<IBillingWebhookDispatcher>(_ => throw new InvalidOperationException("Dispatcher must not be resolved."));
        }));
        using var client = factory.CreateClient();
        var tokens = factory.Services.GetRequiredService<ITokenService>();
        if (identity != "anonymous")
        {
            var token = identity == "platform"
                ? tokens.CreatePlatformAccessToken(Guid.NewGuid(), "platform@example.test")
                : tokens.CreateAccessToken(Guid.NewGuid(), Guid.NewGuid(), identity == "admin" ? UserRole.Admin : UserRole.Owner, "tenant@example.test");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        using var response = await client.GetAsync(Route, Ct);
        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK)
        {
            Assert.True(response.Headers.CacheControl?.NoStore);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
            Assert.Equal(ResponseFields, body.RootElement.EnumerateObject().Select(x => x.Name).Order().ToArray());
        }
    }

    [Fact]
    public async Task EmptyWorkerPollingAdvancesExistingStateWhileGetNeverWritesIt()
    {
        // A new host in the same test process does not reset static process state.
        // Isolate/restore just this key, under a nonparallel collection, to model a fresh process.
        using var state = new FreshProcessState();
        await using var factory = new BillingEndpointSecurityTests.OfflineFactory();
        using var client = factory.CreateClient();
        var token = factory.Services.GetRequiredService<ITokenService>().CreatePlatformAccessToken(Guid.NewGuid(), "platform@example.test");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        for (var index = 0; index < 2; index++)
        {
            using var response = await client.GetAsync(Route, Ct);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
            Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("lastSuccessfulPollAtUtc").ValueKind);
            Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("ageSeconds").ValueKind);
            Assert.Null(QueueFlowTelemetry.GetBillingLastSuccessfulPollAtUtc());
        }

        using var services = new ServiceCollection().AddScoped<IBillingWebhookDispatcher, EmptyDispatcher>().BuildServiceProvider();
        using var worker = new BillingWebhookWorker(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<BillingWebhookWorker>.Instance);
        await worker.StartAsync(Ct);
        try
        {
            var first = await WaitForProgressAsync(null);
            var second = await WaitForProgressAsync(first);
            for (var index = 0; index < 2; index++)
            {
                using var response = await client.GetAsync(Route, Ct);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.True(response.Headers.CacheControl?.NoStore);
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
                Assert.True(body.RootElement.GetProperty("lastSuccessfulPollAtUtc").GetDateTimeOffset() >= second);
                Assert.True(body.RootElement.GetProperty("ageSeconds").GetInt64() >= 0);
            }
        }
        finally { await worker.StopAsync(Ct); }

        var stopped = QueueFlowTelemetry.GetBillingLastSuccessfulPollAtUtc();
        for (var index = 0; index < 2; index++)
        {
            using var response = await client.GetAsync(Route, Ct);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
            Assert.Equal(stopped, body.RootElement.GetProperty("lastSuccessfulPollAtUtc").GetDateTimeOffset());
            Assert.Equal(stopped, QueueFlowTelemetry.GetBillingLastSuccessfulPollAtUtc());
        }
    }

    private static async Task<DateTimeOffset> WaitForProgressAsync(DateTimeOffset? previous)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        while (true)
        {
            var current = QueueFlowTelemetry.GetBillingLastSuccessfulPollAtUtc();
            if (current is { } value && (previous is null || value > previous)) return value;
            await Task.Delay(20, timeout.Token);
        }
    }

    public sealed class EmptyDispatcher : IBillingWebhookDispatcher
    {
        public Task<bool> ProcessNextAsync(CancellationToken ct) => Task.FromResult(false);
    }

    private sealed class FreshProcessState : IDisposable
    {
        private readonly ConcurrentDictionary<string, double> state =
            (ConcurrentDictionary<string, double>)typeof(QueueFlowTelemetry).GetField("JobLastSuccess", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        private readonly double? previous;
        public FreshProcessState()
        {
            previous = state.TryRemove("billing_inbox", out var value) ? value : null;
        }
        public void Dispose()
        {
            state.TryRemove("billing_inbox", out _);
            if (previous is { } value) state["billing_inbox"] = value;
        }
    }
}
