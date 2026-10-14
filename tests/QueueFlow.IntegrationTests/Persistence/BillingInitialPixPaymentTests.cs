using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Common;
using QueueFlow.Infrastructure.Billing;

namespace QueueFlow.IntegrationTests.Persistence;

public sealed class BillingInitialPixPaymentTests
{
    private static BillingCharge Charge()
    {
        var charge = new BillingCharge(Guid.NewGuid(), Guid.NewGuid(), "professional", "Monthly", "Pix",
            69.90m, "initial-payment-test", DateTimeOffset.UtcNow);
        charge.AttachCustomer("cus_existing", DateTimeOffset.UtcNow);
        charge.AttachSubscription("sub_existing", DateTimeOffset.UtcNow);
        return charge;
    }

    private static AsaasSandboxClient Client(HttpClient http) => new(http, new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> {
            ["Asaas:Environment"] = "Sandbox", ["Asaas:SandboxApiKey"] = "fake-key"
        }).Build());

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyThenAvailablePaymentUsesOnlyExistingSubscriptionGets(bool uncertain)
    {
        var charge = Charge();
        if (uncertain) charge.MarkUncertain(DateTimeOffset.UtcNow);
        using var handler = new PaymentReads();
        using var http = new HttpClient(handler) { BaseAddress = new Uri(AsaasSandboxClient.SandboxBaseUrl) };
        var client = Client(http);
        var ct = TestContext.Current.CancellationToken;
        Assert.Null(await BillingCheckoutService.FindInitialSubscriptionPaymentAsync(client, charge, ct));
        Assert.Equal(uncertain ? "Uncertain" : "Pending", charge.Status);
        Assert.Null(charge.AsaasPaymentId);
        var payment = await BillingCheckoutService.FindInitialSubscriptionPaymentAsync(client, charge, ct);
        Assert.NotNull(payment);
        charge.AttachProviderReferences(payment.Id, payment.Subscription, DateTimeOffset.UtcNow);
        charge.ResolveUncertain(DateTimeOffset.UtcNow);
        Assert.Equal("Pending", charge.Status);
        Assert.Equal("pay_existing", charge.AsaasPaymentId);
        var qr = await client.GetPixQrCodeAsync(payment.Id, ct);
        Assert.Equal("copy-paste", qr.Payload);
        Assert.Equal(3, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ReadErrorDoesNotMeanAbsenceOrCreationRejection(HttpStatusCode status)
    {
        var charge = Charge();
        charge.MarkUncertain(DateTimeOffset.UtcNow);
        using var handler = new PaymentReads { Error = status };
        using var http = new HttpClient(handler) { BaseAddress = new Uri(AsaasSandboxClient.SandboxBaseUrl) };
        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            BillingCheckoutService.FindInitialSubscriptionPaymentAsync(Client(http), charge, TestContext.Current.CancellationToken));
        Assert.False(BillingCheckoutService.IsDefinitiveRejection(error, charge));
        Assert.Equal("Uncertain", charge.Status);
        Assert.Null(charge.AsaasPaymentId);
    }

    [Theory]
    [InlineData("partial")]
    [InlineData("multiple")]
    [InlineData("wrong-customer")]
    [InlineData("wrong-subscription")]
    [InlineData("wrong-amount")]
    [InlineData("wrong-method")]
    [InlineData("invalid-id")]
    [InlineData("missing-data")]
    [InlineData("network")]
    [InlineData("deleted")]
    [InlineData("timeout")]
    public async Task AmbiguousOrMismatchedReadNeverAttachesAPayment(string scenario)
    {
        var charge = Charge();
        using var handler = new PaymentReads { Scenario = scenario };
        using var http = new HttpClient(handler) { BaseAddress = new Uri(AsaasSandboxClient.SandboxBaseUrl) };
        var error = await Record.ExceptionAsync(() => BillingCheckoutService.FindInitialSubscriptionPaymentAsync(
            Client(http), charge, TestContext.Current.CancellationToken));
        Assert.NotNull(error);
        Assert.True(error is InvalidOperationException or DomainException or OperationCanceledException or HttpRequestException);
        Assert.Null(charge.AsaasPaymentId);
        Assert.Equal("Pending", charge.Status);
    }

    private sealed class PaymentReads : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public HttpStatusCode? Error { get; init; }
        public string? Scenario { get; init; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("api-sandbox.asaas.com", request.RequestUri!.Host);
            if (Scenario == "timeout") throw new TaskCanceledException();
            if (Scenario == "network") throw new HttpRequestException("Simulated connection failure.");
            if (Error is { } status) return Task.FromResult(new HttpResponseMessage(status));
            if (request.RequestUri.AbsolutePath.EndsWith("/pixQrCode", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { payload = "copy-paste", encodedImage = "dGVzdA==" }) });
            Assert.Equal("/v3/subscriptions/sub_existing/payments", request.RequestUri.AbsolutePath);
            Assert.Equal("?limit=100", request.RequestUri.Query);
            if (Scenario == "missing-data")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { hasMore = false }) });
            var payment = new { id = Scenario == "invalid-id" ? "pay/other" : "pay_existing",
                customer = Scenario == "wrong-customer" ? "cus_other" : "cus_existing",
                subscription = Scenario == "wrong-subscription" ? "sub_other" : "sub_existing",
                billingType = Scenario == "wrong-method" ? "BOLETO" : "PIX",
                value = Scenario == "wrong-amount" ? 1m : 69.90m, deleted = Scenario == "deleted" };
            var data = Scenario == "multiple" ? new[] { payment, payment } :
                Scenario is not null || Calls > 1 ? new[] { payment } : [];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = JsonContent.Create(new { data, hasMore = Scenario == "partial" })
            });
        }
    }
}
