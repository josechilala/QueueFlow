using Microsoft.EntityFrameworkCore;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;
using QueueFlow.Infrastructure.Persistence;

namespace QueueFlow.IntegrationTests.Persistence;

public sealed class BillingPersistenceTests
{
    private static ApplicationDbContext Context(Guid tenant) => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql("Host=127.0.0.1;Port=1;Database=model_only;Username=model_only;Password=model_only;Timeout=1").Options,
        new TenantIdentity(tenant));

    [Fact]
    public void DatabaseEnforcesTenantIdempotencyProviderUniquenessAndReferentialIntegrity()
    {
        using var db = Context(Guid.NewGuid());
        var model = db.Model.FindEntityType(typeof(BillingCharge))!;
        Assert.Contains(model.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual(["OrganizationId", "IdempotencyKey"]));
        Assert.Contains(model.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual(["AsaasPaymentId"]));
        Assert.Contains(model.GetForeignKeys(), x => x.PrincipalEntityType.ClrType == typeof(Organization) && x.DeleteBehavior == DeleteBehavior.Restrict);
        Assert.True(model.FindProperty("xmin")!.IsConcurrencyToken);
        Assert.Equal(18, model.FindProperty("Amount")!.GetPrecision());
        Assert.Equal(2, model.FindProperty("Amount")!.GetScale());
        Assert.Contains(db.Model.FindEntityType(typeof(BillingWebhookEvent))!.GetIndexes(), x => x.IsUnique && x.Properties.Single().Name == "ProviderEventId");
    }

    [Fact]
    public void ExistingMigrationSnapshotMatchesRuntimeModel()
    {
        using var db = Context(Guid.NewGuid());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public void ReadFiltersAreParameterizedByAuthenticatedTenant()
    {
        var tenantA = Guid.NewGuid(); var tenantB = Guid.NewGuid();
        using var a = Context(tenantA); using var b = Context(tenantB);
        Assert.Contains(tenantA.ToString(), a.BillingCharges.ToQueryString(), StringComparison.Ordinal);
        Assert.Contains(tenantB.ToString(), b.BillingCharges.ToQueryString(), StringComparison.Ordinal);
        Assert.DoesNotContain(tenantB.ToString(), a.BillingCharges.ToQueryString(), StringComparison.Ordinal);
        Assert.Contains(tenantA.ToString(), a.BillingWebhookEvents.ToQueryString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CrossTenantWritesAreRejectedBeforeDatabaseAccess()
    {
        using var db = Context(Guid.NewGuid());
        db.BillingCharges.Add(new BillingCharge(Guid.NewGuid(), Guid.NewGuid(), "professional", "Monthly", "Pix", 69.90m, "key", DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    private sealed class TenantIdentity(Guid tenant) : ICurrentUser
    {
        public Guid? UserId => Guid.NewGuid();
        public Guid? OrganizationId => tenant;
        public UserRole? Role => UserRole.Owner;
        public IdentityType? IdentityType => QueueFlow.Domain.Enums.IdentityType.Tenant;
        public bool IsAuthenticated => true;
    }
}
