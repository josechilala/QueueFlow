using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueueFlow.Application.Common;
using QueueFlow.Application.Features.Billing;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;
using QueueFlow.Infrastructure.Billing;
using QueueFlow.Infrastructure.Persistence;
using TestSchema = QueueFlow.IntegrationTests.Persistence.BillingPostgresTests.TestSchema;

namespace QueueFlow.IntegrationTests.Persistence;

public sealed class BillingWebhookInboxTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly BillingWebhookEnvelope Envelope = new("evt_inbox", "PAYMENT_RECEIVED", "pay_inbox");

    [Fact]
    public async Task HttpAcknowledgesOnlyDurableReceiptWithoutCallingAsaas()
    {
        await using var schema = await TestSchema.CreateAsync();
        using var handler = new PaymentTransport { FailIfCalled = true };
        using var http = new HttpClient(handler) { BaseAddress = new Uri(AsaasSandboxClient.SandboxBaseUrl) };
        await using var original = new BillingEndpointSecurityTests.OfflineFactory(true, schema.TenantA.ToString());
        await using var factory = original.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ApplicationDbContext>();
            services.AddScoped(_ => schema.Context(null));
            services.RemoveAll<AsaasSandboxClient>();
            services.AddScoped(_ => Provider(http));
        }));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("asaas-access-token", "offline-webhook-token-of-at-least-32-characters");
        using var response = await client.PostAsJsonAsync("/api/v1/billing/webhooks/asaas",
            new { id = Envelope.EventId, @event = Envelope.EventType, payment = new { id = Envelope.PaymentId } }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var check = schema.Context(null);
        var receipt = await check.BillingWebhookInbox.SingleAsync(Ct);
        Assert.Equal("Pending", receipt.Status);
        Assert.Empty(await check.BillingWebhookEvents.IgnoreQueryFilters().ToListAsync(Ct));
        Assert.Equal(0, handler.Calls);
        using var duplicate = await client.PostAsJsonAsync("/api/v1/billing/webhooks/asaas",
            new { id = Envelope.EventId, @event = Envelope.EventType, payment = new { id = Envelope.PaymentId } }, Ct);
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        using var conflict = await client.PostAsJsonAsync("/api/v1/billing/webhooks/asaas",
            new { id = Envelope.EventId, @event = Envelope.EventType, payment = new { id = "pay_other" } }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task ConcurrentReceiptsAndWorkersSettleOnceAfterReceiverRestart()
    {
        await using var schema = await TestSchema.CreateAsync();
        await SeedAsync(schema);
        var clock = new TestClock();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var receiver = schema.Context(null);
            await new ReceiveBillingWebhook(new PostgresBillingWebhookInbox(receiver, clock)).AcceptAsync(Envelope, Ct);
        }));
        using var handler = new PaymentTransport();
        using var http = Http(handler);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => DispatchAsync(schema, http, clock)));
        // A new receiver and dispatcher see durable completion, not process-local state.
        await ReceiveAsync(schema, clock);
        Assert.False(await DispatchAsync(schema, http, clock));
        await using var check = schema.Context(schema.TenantA);
        var entry = await check.BillingWebhookInbox.SingleAsync(Ct);
        Assert.Equal("Processed", entry.Status);
        Assert.Equal(1, entry.Attempts);
        Assert.Single(await check.BillingWebhookEvents.ToListAsync(Ct));
        Assert.Equal("Paid", (await check.BillingCharges.SingleAsync(Ct)).Status);
        Assert.Equal(SubscriptionStatus.Active, (await check.Subscriptions.SingleAsync(Ct)).Status);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task ReusedEventIdCannotReplaceItsEnvelope()
    {
        await using var schema = await TestSchema.CreateAsync();
        var clock = new TestClock();
        await ReceiveAsync(schema, clock);
        await using var db = schema.Context(null);
        await Assert.ThrowsAsync<ConflictException>(() => new PostgresBillingWebhookInbox(db, clock)
            .PersistAsync(Envelope with { PaymentId = "pay_other" }, Ct));
        Assert.Equal("pay_inbox", (await db.BillingWebhookInbox.AsNoTracking().SingleAsync(Ct)).PaymentId);
    }

    [Fact]
    public async Task DuplicateReceptionDoesNotWaitForSlowConsumerRowLock()
    {
        await using var schema = await TestSchema.CreateAsync();
        var clock = new TestClock();
        await ReceiveAsync(schema, clock);
        await using var db = schema.Context(null);
        var paused = new PausedProcessor();
        var processing = new BillingWebhookInboxProcessor(db, paused, clock).ProcessNextAsync(Ct);
        await paused.Entered.Task.WaitAsync(Ct);
        try
        {
            // Consumer holds FOR UPDATE until released. Reception must use the committed receipt.
            await ReceiveAsync(schema, clock);
        }
        finally { paused.Release.TrySetResult(); }
        Assert.True(await processing);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task TransientFailureSurvivesRestartAndRespectsBackoff(HttpStatusCode? status)
    {
        await using var schema = await TestSchema.CreateAsync();
        await SeedAsync(schema);
        var clock = new TestClock();
        await ReceiveAsync(schema, clock);
        using var handler = new PaymentTransport { Error = status, NetworkFailure = status is null };
        using var http = Http(handler);
        Assert.True(await DispatchAsync(schema, http, clock));
        Assert.False(await DispatchAsync(schema, http, clock));
        await using (var check = schema.Context(schema.TenantA))
        {
            var entry = await check.BillingWebhookInbox.SingleAsync(Ct);
            Assert.Equal("Pending", entry.Status);
            Assert.Equal(1, entry.Attempts);
            Assert.True(entry.NextAttemptAt > clock.GetUtcNow());
            Assert.Equal("Uncertain", (await check.BillingCharges.SingleAsync(Ct)).Status);
        }
        handler.Error = null;
        handler.NetworkFailure = false;
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await DispatchAsync(schema, http, clock));
        await using var final = schema.Context(schema.TenantA);
        Assert.Equal("Paid", (await final.BillingCharges.SingleAsync(Ct)).Status);
        Assert.Equal(2, (await final.BillingWebhookInbox.SingleAsync(Ct)).Attempts);
    }

    [Fact]
    public async Task SavepointRollsBackPartialSettlementBeforePersistingRetry()
    {
        await using var schema = await TestSchema.CreateAsync();
        await SeedAsync(schema);
        var clock = new TestClock();
        await ReceiveAsync(schema, clock);
        await using (var db = schema.Context(null))
        {
            var dispatcher = new BillingWebhookInboxProcessor(db, new PartialSettlement(db), clock);
            Assert.True(await dispatcher.ProcessNextAsync(Ct));
        }
        await using var check = schema.Context(schema.TenantA);
        Assert.Equal("Uncertain", (await check.BillingCharges.SingleAsync(Ct)).Status);
        Assert.Equal("Pending", (await check.BillingWebhookInbox.SingleAsync(Ct)).Status);
        Assert.Empty(await check.BillingWebhookEvents.ToListAsync(Ct));
    }

    [Fact]
    public async Task InterruptedWorkerRollsBackClaimAndSettlementForNextInstance()
    {
        await using var schema = await TestSchema.CreateAsync();
        await SeedAsync(schema);
        var clock = new TestClock();
        await ReceiveAsync(schema, clock);
        using var interrupted = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        await using (var db = schema.Context(null))
        {
            var dispatcher = new BillingWebhookInboxProcessor(db, new PartialSettlement(db, interrupted), clock);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dispatcher.ProcessNextAsync(interrupted.Token));
        }
        await using (var check = schema.Context(schema.TenantA))
        {
            Assert.Equal("Uncertain", (await check.BillingCharges.SingleAsync(Ct)).Status);
            Assert.Equal(0, (await check.BillingWebhookInbox.SingleAsync(Ct)).Attempts);
        }
        using var handler = new PaymentTransport();
        using var http = Http(handler);
        Assert.True(await DispatchAsync(schema, http, clock));
    }

    [Theory]
    [InlineData("PENDING", "PIX", "PAYMENT_CREATED", "Ignored")]
    [InlineData("RECEIVED", "PIX", "PAYMENT_RECEIVED", "Review")]
    [InlineData("RECEIVED", "BOLETO", "PAYMENT_RECEIVED", "Review")]
    [InlineData("PENDING", "BOLETO", "PAYMENT_CREATED", "Review")]
    public async Task CancelledReservationNeverReactivates(string paymentStatus, string method, string eventType, string expected)
    {
        await using var schema = await TestSchema.CreateAsync();
        await SeedAsync(schema, cancelled: true);
        var clock = new TestClock();
        await ReceiveAsync(schema, clock, Envelope with { EventType = eventType });
        using var handler = new PaymentTransport { Status = paymentStatus, Method = method };
        using var http = Http(handler);
        Assert.True(await DispatchAsync(schema, http, clock));
        Assert.False(await DispatchAsync(schema, http, clock));
        await using var check = schema.Context(schema.TenantA);
        Assert.Equal(expected, (await check.BillingWebhookInbox.SingleAsync(Ct)).Status);
        var charge = await check.BillingCharges.SingleAsync(Ct);
        Assert.Equal("Cancelled", charge.Status);
        Assert.Null(charge.AsaasPaymentId);
        Assert.Equal(SubscriptionStatus.Trial, (await check.Subscriptions.SingleAsync(Ct)).Status);
    }

    [Theory]
    [InlineData("method")]
    [InlineData("customer")]
    [InlineData("subscription")]
    [InlineData("amount")]
    [InlineData("tenant")]
    [InlineData("payment")]
    public async Task PermanentMismatchRequiresReviewWithoutRetry(string mismatch)
    {
        await using var schema = await TestSchema.CreateAsync();
        await SeedAsync(schema, linked: true);
        var clock = new TestClock();
        await ReceiveAsync(schema, clock);
        using var handler = new PaymentTransport { Mismatch = mismatch };
        using var http = Http(handler);
        Assert.True(await DispatchAsync(schema, http, clock, mismatch == "tenant" ? schema.TenantB : null));
        clock.Advance(TimeSpan.FromDays(2));
        Assert.False(await DispatchAsync(schema, http, clock));
        await using var check = schema.Context(schema.TenantA);
        Assert.Equal("Review", (await check.BillingWebhookInbox.SingleAsync(Ct)).Status);
        Assert.Equal("Uncertain", (await check.BillingCharges.SingleAsync(Ct)).Status);
        Assert.Equal(SubscriptionStatus.Trial, (await check.Subscriptions.SingleAsync(Ct)).Status);
    }

    [Fact]
    public async Task UnsupportedEventIsDurablyIgnoredWithoutProviderRead()
    {
        await using var schema = await TestSchema.CreateAsync();
        var clock = new TestClock();
        await ReceiveAsync(schema, clock, new("evt_unsupported", "UNKNOWN_EVENT", null));
        using var handler = new PaymentTransport { FailIfCalled = true };
        using var http = Http(handler);
        Assert.True(await DispatchAsync(schema, http, clock));
        await using var check = schema.Context(null);
        Assert.Equal("Ignored", (await check.BillingWebhookInbox.SingleAsync(Ct)).Status);
    }

    [Fact]
    public async Task ConfirmationEnvelopeCannotActivateWhileProviderStillReportsPending()
    {
        await using var schema = await TestSchema.CreateAsync();
        await SeedAsync(schema);
        var clock = new TestClock();
        await ReceiveAsync(schema, clock);
        using var handler = new PaymentTransport { Status = "PENDING" };
        using var http = Http(handler);
        Assert.True(await DispatchAsync(schema, http, clock));
        await using var check = schema.Context(schema.TenantA);
        Assert.Equal("Uncertain", (await check.BillingCharges.SingleAsync(Ct)).Status);
        Assert.Equal(SubscriptionStatus.Trial, (await check.Subscriptions.SingleAsync(Ct)).Status);
        Assert.Equal("Pending", (await check.BillingWebhookInbox.SingleAsync(Ct)).Status);
        Assert.Empty(await check.BillingWebhookEvents.ToListAsync(Ct));
    }

    [Fact]
    public async Task CancelledOrganizationSubscriptionRequiresReviewAndRollsBackReferences()
    {
        await using var schema = await TestSchema.CreateAsync();
        await SeedAsync(schema);
        await using (var db = schema.Context(schema.TenantA))
        {
            (await db.Subscriptions.SingleAsync(Ct)).Cancel(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(Ct);
        }
        var clock = new TestClock();
        await ReceiveAsync(schema, clock);
        using var handler = new PaymentTransport();
        using var http = Http(handler);
        Assert.True(await DispatchAsync(schema, http, clock));
        await using var check = schema.Context(schema.TenantA);
        var charge = await check.BillingCharges.SingleAsync(Ct);
        Assert.Equal("Uncertain", charge.Status);
        Assert.Null(charge.AsaasPaymentId);
        Assert.Equal(SubscriptionStatus.Cancelled, (await check.Subscriptions.SingleAsync(Ct)).Status);
        Assert.Equal("Review", (await check.BillingWebhookInbox.SingleAsync(Ct)).Status);
    }

    [Fact]
    public async Task AdditiveMigrationPreservesExistingChargeAndSubscription()
    {
        await using var schema = await TestSchema.CreateAsync();
        // Downgrade only this disposable schema to the version immediately preceding the additive migration.
        await using (var before = schema.Context(null))
            await before.GetService<IMigrator>().MigrateAsync("20261010033728_AddBillingCharges", Ct);
        await SeedAsync(schema);
        await using var db = schema.Context(schema.TenantA);
        var chargeId = (await db.BillingCharges.SingleAsync(Ct)).Id;
        await db.Database.MigrateAsync(Ct);
        Assert.Equal(chargeId, (await db.BillingCharges.AsNoTracking().SingleAsync(Ct)).Id);
        Assert.Equal("Uncertain", (await db.BillingCharges.AsNoTracking().SingleAsync(Ct)).Status);
        Assert.Equal(SubscriptionStatus.Trial, (await db.Subscriptions.SingleAsync(Ct)).Status);
        Assert.Empty(await db.BillingWebhookInbox.ToListAsync(Ct));
        Assert.True(await db.Database.SqlQuery<bool>($"""
            SELECT relrowsecurity AS "Value" FROM pg_class WHERE oid = '"BillingWebhookInbox"'::regclass
            """).SingleAsync(Ct));
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task RetriesAreBoundedAndExhaustionBecomesDurableReview()
    {
        await using var schema = await TestSchema.CreateAsync();
        var clock = new TestClock();
        await ReceiveAsync(schema, clock);
        using var handler = new PaymentTransport { NetworkFailure = true };
        using var http = Http(handler);
        for (var index = 0; index < 12; index++)
        {
            Assert.True(await DispatchAsync(schema, http, clock));
            clock.Advance(TimeSpan.FromHours(2));
        }
        Assert.False(await DispatchAsync(schema, http, clock));
        await using var check = schema.Context(null);
        var entry = await check.BillingWebhookInbox.SingleAsync(Ct);
        Assert.Equal("Review", entry.Status);
        Assert.Equal(12, entry.Attempts);
        Assert.Equal("RetryLimitExceeded", entry.LastErrorCode);
    }

    private static async Task SeedAsync(TestSchema schema, bool cancelled = false, bool linked = false)
    {
        await using var db = schema.Context(schema.TenantA);
        var charge = new BillingCharge(Guid.NewGuid(), schema.TenantA, "professional", "Monthly", "Pix", 69.90m, "inbox-charge", DateTimeOffset.UtcNow);
        charge.AttachCustomer("cus_inbox", DateTimeOffset.UtcNow);
        charge.AttachSubscription("sub_inbox", DateTimeOffset.UtcNow);
        if (linked) charge.AttachProviderReferences("pay_inbox", "sub_inbox", DateTimeOffset.UtcNow);
        if (cancelled) charge.Cancel(DateTimeOffset.UtcNow);
        else charge.MarkUncertain(DateTimeOffset.UtcNow);
        db.BillingCharges.Add(charge);
        await db.SaveChangesAsync(Ct);
    }

    private static async Task ReceiveAsync(TestSchema schema, TimeProvider clock, BillingWebhookEnvelope? envelope = null)
    {
        await using var db = schema.Context(null);
        await new ReceiveBillingWebhook(new PostgresBillingWebhookInbox(db, clock)).AcceptAsync(envelope ?? Envelope, Ct);
    }

    private static async Task<bool> DispatchAsync(TestSchema schema, HttpClient http, TimeProvider clock, Guid? allowedTenant = null)
    {
        await using var db = schema.Context(null);
        var access = new AsaasSandboxTenantAccess(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Asaas:SandboxAllowedTenantId"] = (allowedTenant ?? schema.TenantA).ToString() }).Build());
        return await new BillingWebhookInboxProcessor(db, new BillingWebhookProcessor(db, Provider(http), access), clock).ProcessNextAsync(Ct);
    }

    private static AsaasSandboxClient Provider(HttpClient http) => new(http, new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["Asaas:Environment"] = "Sandbox", ["Asaas:SandboxApiKey"] = "fake-inbox-test-key" }).Build());
    private static HttpClient Http(PaymentTransport handler) => new(handler) { BaseAddress = new Uri(AsaasSandboxClient.SandboxBaseUrl) };

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }

    private sealed class PartialSettlement(ApplicationDbContext db, CancellationTokenSource? interrupt = null) : IBillingWebhookProcessor
    {
        public async Task<BillingWebhookOutcome> ProcessAsync(string eventId, string eventType, string paymentId, CancellationToken ct)
        {
            var charge = await db.BillingCharges.IgnoreQueryFilters().SingleAsync(ct);
            charge.AttachProviderReferences(paymentId, "sub_inbox", DateTimeOffset.UtcNow);
            charge.ConfirmPayment(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(ct);
            if (interrupt is not null) { await interrupt.CancelAsync(); ct.ThrowIfCancellationRequested(); }
            throw new HttpRequestException("Simulated failure after partial write.");
        }
    }

    private sealed class PausedProcessor : IBillingWebhookProcessor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<BillingWebhookOutcome> ProcessAsync(string eventId, string eventType, string paymentId, CancellationToken ct)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(ct);
            return BillingWebhookOutcome.Ignored;
        }
    }

    private sealed class PaymentTransport : HttpMessageHandler
    {
        private int calls;
        public int Calls => calls;
        public bool FailIfCalled { get; init; }
        public bool NetworkFailure { get; set; }
        public HttpStatusCode? Error { get; set; }
        public string Status { get; init; } = "RECEIVED";
        public string Method { get; init; } = "PIX";
        public string? Mismatch { get; init; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref calls);
            Assert.False(FailIfCalled);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://api-sandbox.asaas.com/v3/payments/pay_inbox", request.RequestUri!.AbsoluteUri);
            if (NetworkFailure) throw new HttpRequestException("Simulated offline provider.");
            if (Error is { } status) return Task.FromResult(new HttpResponseMessage(status));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new
            {
                id = Mismatch == "payment" ? "pay_other" : "pay_inbox", status = Status, customer = Mismatch == "customer" ? "cus_other" : "cus_inbox",
                subscription = Mismatch == "subscription" ? "sub_other" : "sub_inbox",
                value = Mismatch == "amount" ? 1m : 69.90m, billingType = Mismatch == "method" ? "BOLETO" : Method
            }) });
        }
    }
}
