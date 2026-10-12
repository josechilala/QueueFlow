using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Features.Billing;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;
using QueueFlow.Infrastructure.Billing;
using QueueFlow.Infrastructure.Persistence;

namespace QueueFlow.IntegrationTests.Persistence;

public sealed class BillingPostgresTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static BillingCheckoutRequest Request(string key = "concurrent-key") =>
        new("professional", "Yearly", "Pix", key, "Sandbox owner", "owner@example.test", "12345678901");

    [Theory]
    [InlineData("pay_test")]
    [InlineData("pay_unregistered")]
    public async Task WebhookCannotSettleChargeForAnotherTenant(string paymentId)
    {
        await using var schema = await TestSchema.CreateAsync();
        using var handler = new ProviderStub();
        using var http = new HttpClient(handler) { BaseAddress = new Uri(AsaasSandboxClient.SandboxBaseUrl) };
        var provider = Provider(http);
        await using (var owner = schema.Context(schema.TenantA))
            await new BillingCheckoutService(owner, new Identity(schema.TenantA), provider, Access(schema.TenantA)).StartAsync(Request(), Ct);
        handler.Status = "RECEIVED";
        await using (var webhook = schema.Context(null))
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new BillingWebhookProcessor(webhook, provider, Access(schema.TenantB))
                .ProcessAsync("event-disallowed", "PAYMENT_RECEIVED", paymentId, Ct));
        await using var check = schema.Context(schema.TenantA);
        Assert.Equal("Pending", (await check.BillingCharges.SingleAsync(Ct)).Status);
        Assert.Equal(SubscriptionStatus.Trial, (await check.Subscriptions.SingleAsync(Ct)).Status);
        Assert.Empty(await check.BillingWebhookEvents.IgnoreQueryFilters().ToListAsync(Ct));
    }

    [Fact]
    public async Task ConcurrentSameKeyCreatesOnlyOneProviderPayment()
    {
        await using var schema = await TestSchema.CreateAsync();
        using var handler = new ProviderStub();
        using var http = new HttpClient(handler) { BaseAddress = new Uri(AsaasSandboxClient.SandboxBaseUrl) };
        var provider = Provider(http);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var db = schema.Context(schema.TenantA);
            return await new BillingCheckoutService(db, new Identity(schema.TenantA), provider, Access(schema.TenantA)).StartAsync(Request(), Ct);
        }));
        Assert.Single(results.Select(x => x.Id).Distinct());
        Assert.Equal(1, handler.PaymentCreates);
        Assert.Equal(1, handler.CustomerCreates);
        await using var check = schema.Context(schema.TenantA);
        Assert.Equal(1, await check.BillingCharges.CountAsync(Ct));
    }

    [Fact]
    public async Task AmbiguousProviderFailureDoesNotCreateAgainOnRetry()
    {
        await using var schema = await TestSchema.CreateAsync();
        using var handler = new ProviderStub { LoseCreateResponse = true };
        using var http = new HttpClient(handler) { BaseAddress = new Uri(AsaasSandboxClient.SandboxBaseUrl) };
        await using var db = schema.Context(schema.TenantA);
        var service = new BillingCheckoutService(db, new Identity(schema.TenantA), Provider(http), Access(schema.TenantA));
        var first = await service.StartAsync(Request(), Ct);
        var retry = await service.StartAsync(Request(), Ct);
        Assert.Equal("Uncertain", first.Status);
        Assert.Equal(first.Id, retry.Id);
        Assert.Equal(1, handler.PaymentCreates);
    }

    [Fact]
    public async Task DatabaseRejectsDuplicateKeysAndReadsRemainTenantScoped()
    {
        await using var schema = await TestSchema.CreateAsync();
        await using var a = schema.Context(schema.TenantA);
        await using var b = schema.Context(schema.TenantB);
        a.BillingCharges.Add(new BillingCharge(Guid.NewGuid(), schema.TenantA, "professional", "Yearly", "Pix", 796.86m, "same-key", DateTimeOffset.UtcNow));
        b.BillingCharges.Add(new BillingCharge(Guid.NewGuid(), schema.TenantB, "professional", "Yearly", "Pix", 796.86m, "same-key", DateTimeOffset.UtcNow));
        await a.SaveChangesAsync(Ct); await b.SaveChangesAsync(Ct);
        Assert.Single(await a.BillingCharges.ToListAsync(Ct));
        Assert.All(await a.BillingCharges.ToListAsync(Ct), x => Assert.Equal(schema.TenantA, x.OrganizationId));
        a.BillingCharges.Add(new BillingCharge(Guid.NewGuid(), schema.TenantA, "professional", "Yearly", "Pix", 796.86m, "same-key", DateTimeOffset.UtcNow));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => a.SaveChangesAsync(Ct));
        Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal("23505", ((PostgresException)error.InnerException!).SqlState);
    }

    [Fact]
    public async Task DuplicateAndOutOfOrderWebhooksPreserveTrialAndRefundState()
    {
        await using var schema = await TestSchema.CreateAsync();
        using var handler = new ProviderStub();
        using var http = new HttpClient(handler) { BaseAddress = new Uri(AsaasSandboxClient.SandboxBaseUrl) };
        var provider = Provider(http);
        await using (var owner = schema.Context(schema.TenantA))
            await new BillingCheckoutService(owner, new Identity(schema.TenantA), provider, Access(schema.TenantA)).StartAsync(Request(), Ct);
        handler.Status = "RECEIVED";
        await using (var webhook = schema.Context(null))
        {
            var processor = new BillingWebhookProcessor(webhook, provider, Access(schema.TenantA));
            await processor.ProcessAsync("event-received", "PAYMENT_RECEIVED", "pay_test", Ct);
            await processor.ProcessAsync("event-received", "PAYMENT_RECEIVED", "pay_test", Ct);
            // An older pending notification is reconciled against current confirmed state.
            await processor.ProcessAsync("event-old-created", "PAYMENT_CREATED", "pay_test", Ct);
        }
        await using (var check = schema.Context(schema.TenantA))
        {
            Assert.Equal("Paid", (await check.BillingCharges.SingleAsync(Ct)).Status);
            var subscription = await check.Subscriptions.SingleAsync(Ct);
            Assert.Equal(SubscriptionStatus.Active, subscription.Status);
            Assert.Equal(schema.TrialEnd, subscription.TrialEndsAt);
            Assert.Equal(2, await check.BillingWebhookEvents.CountAsync(Ct));
        }
        handler.Status = "REFUNDED";
        await using (var webhook = schema.Context(null))
        {
            var processor = new BillingWebhookProcessor(webhook, provider, Access(schema.TenantA));
            await processor.ProcessAsync("event-refunded", "PAYMENT_REFUNDED", "pay_test", Ct);
            await processor.ProcessAsync("event-delayed-confirmed", "PAYMENT_CONFIRMED", "pay_test", Ct);
        }
        await using var final = schema.Context(schema.TenantA);
        Assert.Equal("Refunded", (await final.BillingCharges.SingleAsync(Ct)).Status);
        Assert.Equal(SubscriptionStatus.PastDue, (await final.Subscriptions.SingleAsync(Ct)).Status);
    }

    private static AsaasSandboxClient Provider(HttpClient http) => new(http, new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Asaas:Environment"] = "Sandbox", ["Asaas:SandboxApiKey"] = "fake-key-never-sent" }).Build());

    private sealed class ProviderStub : HttpMessageHandler
    {
        private int _customers, _payments;
        private string? _reference;
        public int CustomerCreates => _customers;
        public int PaymentCreates => _payments;
        public string Status { get; set; } = "PENDING";
        public bool LoseCreateResponse { get; init; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path.EndsWith("/customers", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _customers);
                return Reply(new { id = "cus_test" });
            }
            if (request.Method == HttpMethod.Post)
            {
                Interlocked.Increment(ref _payments);
                var body = await request.Content!.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct);
                _reference = body.GetProperty("externalReference").GetString();
                if (LoseCreateResponse) throw new HttpRequestException("Simulated response loss after provider acceptance.");
            }
            if (path.EndsWith("/pixQrCode", StringComparison.Ordinal)) return Reply(new { payload = "test-pix", encodedImage = "" });
            return Reply(new { id = "pay_test", status = Status, customer = "cus_test", value = 796.86m,
                billingType = "PIX", externalReference = _reference, invoiceUrl = "https://sandbox.asaas.com/i/test" });
        }
        private static HttpResponseMessage Reply(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
    }

    private static AsaasSandboxTenantAccess Access(Guid tenant) => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Asaas:SandboxAllowedTenantId"] = tenant.ToString() }).Build());

    private sealed class Identity(Guid? tenant) : ICurrentUser
    {
        public Guid? UserId => tenant.HasValue ? tenant : null;
        public Guid? OrganizationId => tenant;
        public UserRole? Role => tenant.HasValue ? UserRole.Owner : null;
        public IdentityType? IdentityType => tenant.HasValue ? QueueFlow.Domain.Enums.IdentityType.Tenant : null;
        public bool IsAuthenticated => tenant.HasValue;
    }

    private sealed class TestSchema(string connection, string schema) : IAsyncDisposable
    {
        public Guid TenantA { get; } = Guid.NewGuid();
        public Guid TenantB { get; } = Guid.NewGuid();
        public DateTimeOffset TrialEnd { get; } = new(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);
        public ApplicationDbContext Context(Guid? tenant) => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).Options, new Identity(tenant));
        public static async Task<TestSchema> CreateAsync()
        {
            var configured = Environment.GetEnvironmentVariable("QUEUEFLOW_BILLING_TEST_DATABASE");
            if (string.IsNullOrWhiteSpace(configured)) Assert.Skip("Set QUEUEFLOW_BILLING_TEST_DATABASE to an explicitly disposable local billing database.");
            var builder = new NpgsqlConnectionStringBuilder(configured);
            Assert.Equal("127.0.0.1", builder.Host);
            Assert.StartsWith("queueflow_billing_test", builder.Database);
            var schema = $"billing_test_{Guid.NewGuid():N}";
            builder.SearchPath = schema;
            var result = new TestSchema(builder.ConnectionString, schema);
            try
            {
                await using var setup = new NpgsqlConnection(builder.ConnectionString);
                await setup.OpenAsync(Ct);
                await using (var command = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", setup)) await command.ExecuteNonQueryAsync(Ct);
                await using var db = result.Context(null);
                await db.Database.MigrateAsync(Ct);
                var now = DateTimeOffset.UtcNow;
                db.AddRange(new Organization(result.TenantA, "A", $"a-{result.TenantA:N}", "UTC", now),
                    new Organization(result.TenantB, "B", $"b-{result.TenantB:N}", "UTC", now),
                    new Subscription(Guid.NewGuid(), result.TenantA, "Trial", result.TrialEnd, now));
                await db.SaveChangesAsync(Ct);
                return result;
            }
            catch { await result.DisposeAsync(); throw; }
        }
        public async ValueTask DisposeAsync()
        {
            // Only the randomly generated schema owned by this test is removed.
            await using var cleanup = new NpgsqlConnection(connection);
            await cleanup.OpenAsync(CancellationToken.None);
            await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", cleanup);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
