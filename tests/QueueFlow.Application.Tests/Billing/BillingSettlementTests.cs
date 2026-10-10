using QueueFlow.Domain.Common;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;

namespace QueueFlow.Application.Tests.Billing;

public sealed class BillingSettlementTests
{
    private static BillingCharge Charge() => new(Guid.NewGuid(), Guid.NewGuid(), "professional", "Monthly", "Pix", 69.90m, "request-key", DateTimeOffset.UtcNow);

    [Theory]
    [InlineData("Monthly", "DebitCard")]
    [InlineData("Weekly", "Pix")]
    [InlineData("Monthly", "BOLETO")]
    public void UnsupportedSelectionsCannotBePersisted(string cycle, string method) =>
        Assert.Throws<DomainException>(() => new BillingCharge(Guid.NewGuid(), Guid.NewGuid(), "professional", cycle, method, 69.90m, "key", DateTimeOffset.UtcNow));

    [Fact]
    public void FractionalCentsCannotBeRoundedSilentlyByDatabase() =>
        Assert.Throws<DomainException>(() => new BillingCharge(Guid.NewGuid(), Guid.NewGuid(), "professional", "Monthly", "Pix", 69.901m, "key", DateTimeOffset.UtcNow));

    [Fact]
    public void DelayedPaidEventCannotResurrectCancelledCharge()
    {
        var charge = Charge();
        charge.AttachProviderReferences("pay_1", null, DateTimeOffset.UtcNow);
        charge.Cancel(DateTimeOffset.UtcNow);
        Assert.Throws<DomainException>(() => charge.ConfirmPayment(DateTimeOffset.UtcNow));
        Assert.Null(charge.PaidAt);
    }

    [Fact]
    public void AmbiguousCreationCanOnlyBeSettledWithAProviderReference()
    {
        var charge = Charge();
        charge.MarkUncertain(DateTimeOffset.UtcNow);
        Assert.Throws<DomainException>(() => charge.ConfirmPayment(DateTimeOffset.UtcNow));
        charge.AttachProviderReferences("pay_1", null, DateTimeOffset.UtcNow);
        charge.ConfirmPayment(DateTimeOffset.UtcNow);
        charge.MarkUncertain(DateTimeOffset.UtcNow);
        Assert.Equal("Paid", charge.Status);
    }

    [Fact]
    public void RefundIsIdempotentAndCannotBeReversedByDelayedConfirmation()
    {
        var charge = Charge();
        charge.AttachProviderReferences("pay_1", null, DateTimeOffset.UtcNow);
        charge.ConfirmPayment(DateTimeOffset.UtcNow);
        charge.Refund(DateTimeOffset.UtcNow);
        charge.Refund(DateTimeOffset.UtcNow);
        Assert.Throws<DomainException>(() => charge.ConfirmPayment(DateTimeOffset.UtcNow));
        Assert.Equal("Refunded", charge.Status);
    }

    [Fact]
    public void ActivationPreservesOriginalTrialDeadline()
    {
        var now = DateTimeOffset.UtcNow;
        var trialEnd = now.AddDays(14);
        var subscription = new Subscription(Guid.NewGuid(), Guid.NewGuid(), "Trial", trialEnd, now);
        subscription.ActivateFromConfirmedBilling("professional", now);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal(trialEnd, subscription.TrialEndsAt);
        Assert.Equal("professional", subscription.Plan);
    }

    [Fact]
    public void CancelledSubscriptionIsNotReactivatedByDelayedPayment()
    {
        var now = DateTimeOffset.UtcNow;
        var subscription = new Subscription(Guid.NewGuid(), Guid.NewGuid(), "Trial", now.AddDays(14), now);
        subscription.Cancel(now);
        subscription.ActivateFromConfirmedBilling("professional", now.AddMinutes(1));
        Assert.Equal(SubscriptionStatus.Cancelled, subscription.Status);
        Assert.Equal("Trial", subscription.Plan);
    }
}
