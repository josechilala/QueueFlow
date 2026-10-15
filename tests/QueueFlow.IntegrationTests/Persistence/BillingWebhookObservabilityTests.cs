using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QueueFlow.Application.Features.Billing;
using QueueFlow.Application.Observability;
using QueueFlow.Domain.Entities;
using QueueFlow.Infrastructure;
using QueueFlow.Infrastructure.Authentication;
using QueueFlow.Infrastructure.Billing;
using QueueFlow.Infrastructure.Persistence;
using TestSchema = QueueFlow.IntegrationTests.Persistence.BillingPostgresTests.TestSchema;

namespace QueueFlow.IntegrationTests.Persistence;

public sealed class BillingWebhookObservabilityTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly AsyncLocal<object?> ObservationScope = new();
    private static readonly BillingWebhookEnvelope Envelope = new("evt_observe", "PAYMENT_RECEIVED", "pay_observe");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WorkerLogsLifecycleAndEmptyPollingWithoutPerPollLogs(bool throwingLogger)
    {
        var logs = new CapturingLogger<BillingWebhookWorker> { Throw = throwingLogger };
        var empty = new EmptyDispatcher();
        using var provider = new ServiceCollection().AddScoped<IBillingWebhookDispatcher>(_ => empty).BuildServiceProvider();
        using var metrics = new Metrics();
        using var worker = new BillingWebhookWorker(provider.GetRequiredService<IServiceScopeFactory>(), logs);
        await worker.StartAsync(Ct);
        await empty.Polled.Task.WaitAsync(Ct);
        await worker.StopAsync(Ct);
        metrics.Listener.RecordObservableInstruments();
        Assert.True(metrics.LastPoll > 0);
        Assert.Single(logs.Messages, x => x.Contains("BillingInboxWorkerStarted", StringComparison.Ordinal));
        Assert.Single(logs.Messages, x => x.Contains("BillingInboxWorkerStopping", StringComparison.Ordinal));
        Assert.Equal(2, logs.Messages.Count);
    }

    [Fact]
    public async Task DisabledWorkerIsReportedWithoutRegisteringFinancialConsumer()
    {
        var logs = new CapturingLogger<BillingInboxWorkerDisabledReporter>();
        using var services = new ServiceCollection().AddLogging()
            .AddSingleton<ILogger<BillingInboxWorkerDisabledReporter>>(logs)
            .AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:QueueFlowDatabase"] = "Host=127.0.0.1;Port=1;Database=unused" }).Build())
            .BuildServiceProvider();
        var hosted = services.GetServices<IHostedService>().ToArray();
        Assert.DoesNotContain(hosted, x => x is BillingWebhookWorker);
        var reporter = Assert.Single(hosted.OfType<BillingInboxWorkerDisabledReporter>());
        await reporter.StartAsync(Ct);
        Assert.Contains(logs.Messages, x => x.Contains("BillingInboxWorkerDisabled", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EmptyInboxUpdatesPollingWithoutOutcomeLogs()
    {
        await using var schema = await TestSchema.CreateAsync();
        await using var db = schema.Context(null);
        var logs = new CapturingLogger<BillingWebhookInboxProcessor>();
        using var metrics = new Metrics();
        Assert.False(await new BillingWebhookInboxProcessor(db, new OutcomeProcessor(), TimeProvider.System, logs).ProcessNextAsync(Ct));
        metrics.Listener.RecordObservableInstruments();
        Assert.True(metrics.LastPoll > 0);
        Assert.Empty(logs.Messages);
        Assert.Equal(0, metrics.BillingEvents);
    }

    [Fact]
    public async Task QueryFailureIsClassifiedWithoutProviderDetailsOrFalseSuccess()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=unused")
            .AddInterceptors(new QueryFailureInterceptor()).Options, new CurrentUser(new HttpContextAccessor()));
        var logs = new CapturingLogger<BillingWebhookInboxProcessor>();
        using var metrics = new Metrics();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new BillingWebhookInboxProcessor(db, new OutcomeProcessor(), TimeProvider.System, logs).ProcessNextAsync(Ct));
        Assert.Contains(logs.Messages, x => x.Contains("Stage=Query", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Messages, x => x.Contains("private-provider-detail", StringComparison.Ordinal));
        Assert.Equal(0, metrics.Outcomes);
    }

    [Theory]
    [InlineData("Ignored")]
    [InlineData("Review")]
    [InlineData("Processed")]
    [InlineData("Retry")]
    public async Task OutcomeAndRetryAreObservedOnlyAfterCommitWithoutCountingPaymentConfirmation(string outcome)
    {
        await using var schema = await TestSchema.CreateAsync();
        await ReceiveAsync(schema);
        var commits = new CommitInterceptor();
        await using var db = Context(schema, commits);
        var logs = new CapturingLogger<BillingWebhookInboxProcessor> { BeforeLog = () =>
        {
            Assert.True(commits.Committed);
            return true;
        }};
        using var metrics = new Metrics();
        var processor = new OutcomeProcessor { Outcome = Enum.Parse<BillingWebhookOutcome>(outcome) };
        Assert.True(await new BillingWebhookInboxProcessor(db, processor, TimeProvider.System, logs).ProcessNextAsync(Ct));
        var expected = outcome == "Retry" ? "Pending" : outcome;
        Assert.Equal(expected, (await db.BillingWebhookInbox.AsNoTracking().SingleAsync(Ct)).Status);
        Assert.Equal(0, metrics.BillingEvents);
        Assert.Equal(outcome == "Retry" ? 1 : 0, metrics.Retries);
        Assert.Contains(logs.Messages, x => x.Contains(outcome == "Retry" ? "BillingInboxRetryCommitted" : "BillingInboxOutcomeCommitted", StringComparison.Ordinal));
        Assert.All(logs.Preconditions, Assert.True);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommitFailureDoesNotClaimSuccessEvenWhenDatabaseActuallyCommitted(bool afterCommit)
    {
        await using var schema = await TestSchema.CreateAsync();
        await ReceiveAsync(schema);
        await using var db = Context(schema, new CommitInterceptor { FailBefore = !afterCommit, FailAfter = afterCommit });
        var logs = new CapturingLogger<BillingWebhookInboxProcessor>();
        using var metrics = new Metrics();
        await Assert.ThrowsAnyAsync<Exception>(() => new BillingWebhookInboxProcessor(db, new OutcomeProcessor(), TimeProvider.System, logs).ProcessNextAsync(Ct));
        Assert.Equal(0, metrics.Outcomes);
        Assert.Equal(0, metrics.Retries);
        Assert.Equal(0, metrics.BillingEvents);
        Assert.DoesNotContain(logs.Messages, x => x.Contains("OutcomeCommitted", StringComparison.Ordinal));
        Assert.Contains(logs.Messages, x => x.Contains("Stage=Commit", StringComparison.Ordinal) && x.Contains("CommitOutcome=Unknown", StringComparison.Ordinal));
        await using var check = schema.Context(null);
        Assert.Equal(afterCommit ? "Ignored" : "Pending", (await check.BillingWebhookInbox.SingleAsync(Ct)).Status);
        Assert.DoesNotContain(logs.Messages, x => x.Contains("private-provider-detail", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FinancialMetricCountsNewLedgerOnlyAfterOwnerCommitAndTelemetryFailureDoesNotAffectSettlement(bool throwingTelemetry)
    {
        await using var schema = await TestSchema.CreateAsync();
        await using (var seed = schema.Context(schema.TenantA))
        {
            var charge = new BillingCharge(Guid.NewGuid(), schema.TenantA, "professional", "Monthly", "Pix", 69.90m, "observe", DateTimeOffset.UtcNow);
            charge.AttachCustomer("cus_observe", DateTimeOffset.UtcNow);
            charge.AttachSubscription("sub_observe", DateTimeOffset.UtcNow);
            charge.MarkUncertain(DateTimeOffset.UtcNow);
            seed.BillingCharges.Add(charge);
            await seed.SaveChangesAsync(Ct);
        }
        await ReceiveAsync(schema);
        var commits = new CommitInterceptor();
        await using var db = Context(schema, commits);
        var logs = new CapturingLogger<BillingWebhookInboxProcessor> { Throw = throwingTelemetry };
        using var metrics = new Metrics { Throw = throwingTelemetry, CommitAcknowledged = () => commits.Committed };
        using var http = new HttpClient(new PaymentTransport()) { BaseAddress = new Uri(AsaasSandboxClient.SandboxBaseUrl) };
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Asaas:Environment"] = "Sandbox", ["Asaas:SandboxApiKey"] = "fake-observability-key",
            ["Asaas:SandboxAllowedTenantId"] = schema.TenantA.ToString()
        }).Build();
        var processor = new BillingWebhookProcessor(db, new AsaasSandboxClient(http, config), new AsaasSandboxTenantAccess(config));
        var dispatcher = new BillingWebhookInboxProcessor(db, processor, TimeProvider.System, logs);
        Assert.True(await dispatcher.ProcessNextAsync(Ct));
        Assert.False(await dispatcher.ProcessNextAsync(Ct));
        Assert.Equal(1, metrics.BillingEvents);
        Assert.All(metrics.Preconditions, Assert.True);
        await using var check = schema.Context(schema.TenantA);
        Assert.Equal("Paid", (await check.BillingCharges.SingleAsync(Ct)).Status);
        Assert.Single(await check.BillingWebhookEvents.ToListAsync(Ct));
    }

    [Fact]
    public async Task ProcessingFailureIsSanitizedAndRetryIsCommittedAfterRollback()
    {
        await using var schema = await TestSchema.CreateAsync();
        await ReceiveAsync(schema);
        await using var db = schema.Context(null);
        var logs = new CapturingLogger<BillingWebhookInboxProcessor>();
        var processor = new OutcomeProcessor { Fail = true };
        Assert.True(await new BillingWebhookInboxProcessor(db, processor, TimeProvider.System, logs).ProcessNextAsync(Ct));
        Assert.Contains(logs.Messages, x => x.Contains("Stage=Processing", StringComparison.Ordinal));
        Assert.Contains(logs.Messages, x => x.Contains("BillingInboxRetryCommitted", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Messages, x => x.Contains("private-provider-detail", StringComparison.Ordinal));
        Assert.Equal(1, (await db.BillingWebhookInbox.SingleAsync(Ct)).Attempts);
    }

    private static async Task ReceiveAsync(TestSchema schema)
    {
        await using var db = schema.Context(null);
        await new PostgresBillingWebhookInbox(db, TimeProvider.System).PersistAsync(Envelope, Ct);
    }

    private static ApplicationDbContext Context(TestSchema schema, DbTransactionInterceptor interceptor)
    {
        using var original = schema.Context(null);
        return new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(original.Database.GetConnectionString())
            .AddInterceptors(interceptor).Options, new CurrentUser(new HttpContextAccessor()));
    }

    private sealed class CommitInterceptor : DbTransactionInterceptor
    {
        public bool FailBefore { get; init; }
        public bool FailAfter { get; init; }
        public bool Committed { get; private set; }
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (FailBefore) throw new InvalidOperationException("private-provider-detail");
            return ValueTask.FromResult(result);
        }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            Committed = true;
            if (FailAfter) throw new InvalidOperationException("private-provider-detail");
            return Task.CompletedTask;
        }
    }

    private sealed class QueryFailureInterceptor : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default) => throw new InvalidOperationException("private-provider-detail");
    }

    private sealed class OutcomeProcessor : IBillingWebhookProcessor
    {
        public BillingWebhookOutcome Outcome { get; init; } = BillingWebhookOutcome.Ignored;
        public bool Fail { get; init; }
        public Task<BillingWebhookOutcome> ProcessAsync(string eventId, string eventType, string paymentId, CancellationToken ct) =>
            Fail ? throw new HttpRequestException("private-provider-detail") : Task.FromResult(Outcome);
    }

    private sealed class EmptyDispatcher : IBillingWebhookDispatcher
    {
        public TaskCompletionSource Polled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<bool> ProcessNextAsync(CancellationToken ct) { Polled.TrySetResult(); return Task.FromResult(false); }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ConcurrentQueue<bool> Preconditions { get; } = new();
        public bool Throw { get; init; }
        public Func<bool>? BeforeLog { get; init; }
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Assert.Null(exception);
            if (BeforeLog is not null) Preconditions.Enqueue(BeforeLog());
            Messages.Enqueue(formatter(state, exception));
            if (Throw) throw new InvalidOperationException("private-logger-detail");
        }
    }

    private sealed class Metrics : IDisposable
    {
        private readonly object marker = new();
        public MeterListener Listener { get; } = new();
        public long BillingEvents { get; private set; }
        public long Outcomes { get; private set; }
        public long Retries { get; private set; }
        public double LastPoll { get; private set; }
        public bool Throw { get; init; }
        public Func<bool>? CommitAcknowledged { get; init; }
        public ConcurrentQueue<bool> Preconditions { get; } = new();
        public Metrics()
        {
            ObservationScope.Value = marker;
            Listener.InstrumentPublished = (instrument, listener) => { if (instrument.Meter.Name == QueueFlowTelemetry.Name) listener.EnableMeasurementEvents(instrument); };
            Listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
            {
                if (!ReferenceEquals(ObservationScope.Value, marker)) return;
                if (instrument.Name == "queueflow.billing.events")
                {
                    if (CommitAcknowledged is not null) Preconditions.Enqueue(CommitAcknowledged());
                    BillingEvents += value;
                }
                if (instrument.Name == "queueflow.billing.inbox.outcomes") Outcomes += value;
                if (instrument.Name == "queueflow.billing.inbox.retries") Retries += value;
                if (Throw) throw new InvalidOperationException("private-listener-detail");
            });
            Listener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
            {
                if (instrument.Name != "queueflow.job.last_success") return;
                foreach (var tag in tags) if (tag.Key == "job_name" && Equals(tag.Value, "billing_inbox")) LastPoll = value;
            });
            Listener.Start();
        }
        public void Dispose() { Listener.Dispose(); ObservationScope.Value = null; }
    }

    private sealed class PaymentTransport : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new
            {
                id = "pay_observe", status = "RECEIVED", customer = "cus_observe", subscription = "sub_observe",
                value = 69.90m, billingType = "PIX"
            }) });
        }
    }
}
