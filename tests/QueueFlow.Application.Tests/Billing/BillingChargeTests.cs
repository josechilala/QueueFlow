using QueueFlow.Domain.Common;
using QueueFlow.Domain.Entities;
using Xunit;

namespace QueueFlow.Application.Tests.Billing;

public sealed class BillingChargeTests
{
    private static BillingCharge Create() => new(Guid.NewGuid(), Guid.NewGuid(), "professional",
        "Monthly", "Pix", 69.90m, "checkout-request-1", DateTimeOffset.UtcNow);

    [Fact]
    public void NewChargeStartsPendingWithoutProviderReferences()
    {
        var charge = Create();
        Assert.Equal("Pending", charge.Status);
        Assert.Null(charge.AsaasPaymentId);
        Assert.Null(charge.PaidAt);
        Assert.Equal("BRL", charge.Currency);
    }

    [Fact]
    public void PaymentCannotBeConfirmedWithoutProviderReference()
    {
        var charge = Create();
        Assert.Throws<DomainException>(() => charge.ConfirmPayment(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ProviderReferenceCannotBeReplaced()
    {
        var charge = Create();
        charge.AttachProviderReferences("pay_123", null, DateTimeOffset.UtcNow);
        Assert.Throws<DomainException>(() => charge.AttachProviderReferences("pay_456", null, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void PaymentConfirmationIsIdempotent()
    {
        var charge = Create();
        var now = DateTimeOffset.UtcNow;
        charge.AttachProviderReferences("pay_123", null, now);
        charge.ConfirmPayment(now);
        charge.ConfirmPayment(now.AddMinutes(1));
        Assert.Equal("Paid", charge.Status);
        Assert.Equal(now, charge.PaidAt);
    }

    [Fact]
    public void ChargeRejectsInvalidTenantAndAmount()
    {
        Assert.Throws<DomainException>(() => new BillingCharge(Guid.NewGuid(), Guid.Empty, "professional", "Monthly", "Pix", 69.90m, "key", DateTimeOffset.UtcNow));
        Assert.Throws<DomainException>(() => new BillingCharge(Guid.NewGuid(), Guid.NewGuid(), "professional", "Monthly", "Pix", 0m, "key", DateTimeOffset.UtcNow));
    }
}
