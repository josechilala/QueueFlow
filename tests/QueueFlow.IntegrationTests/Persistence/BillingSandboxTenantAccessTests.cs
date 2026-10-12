using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Features.Billing;
using QueueFlow.Domain.Enums;
using QueueFlow.Infrastructure.Billing;
using QueueFlow.Infrastructure.Persistence;

namespace QueueFlow.IntegrationTests.Persistence;

public sealed class BillingSandboxTenantAccessTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("11111111-1111-1111-1111-111111111111")]
    public async Task ServicesRejectDisallowedTenantBeforeDatabaseOrProviderAccess(string? configuredTenant)
    {
        var tenant = Guid.Parse("e98e5f2c-2c52-43de-8c78-f67c30e73588");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Asaas:SandboxAllowedTenantId"] = configuredTenant
        }).Build();
        var access = new AsaasSandboxTenantAccess(configuration);
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=offline;Username=offline;Password=offline;Timeout=1").Options, new Owner(tenant));
        using var handler = new NoProviderCalls();
        using var http = new HttpClient(handler);
        var provider = new AsaasSandboxClient(http, configuration);
        var checkout = new BillingCheckoutService(db, new Owner(tenant), provider, access);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => checkout.StartAsync(
            new BillingCheckoutRequest("professional", "Yearly", "Pix", "isolation-key", "Owner", "owner@example.test", "12345678901"), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => checkout.GetAsync(Guid.NewGuid(), TestContext.Current.CancellationToken));
        if (access.AllowedTenantId is null)
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new BillingWebhookProcessor(db, provider, access)
                .ProcessAsync("evt_test", "PAYMENT_RECEIVED", "pay_test", TestContext.Current.CancellationToken));
        Assert.Equal(0, handler.Calls);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    private sealed class NoProviderCalls : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("No provider request is permitted.");
        }
    }

    private sealed class Owner(Guid tenant) : ICurrentUser
    {
        public Guid? UserId => tenant;
        public Guid? OrganizationId => tenant;
        public UserRole? Role => UserRole.Owner;
        public IdentityType? IdentityType => QueueFlow.Domain.Enums.IdentityType.Tenant;
        public bool IsAuthenticated => true;
    }
}
