using QueueFlow.Application.Features.Billing;

namespace QueueFlow.Application.Tests.Billing;

public sealed class BillingPlansTests
{
    [Fact]
    public void CalculatesApprovedPrices()
    {
        Assert.Equal(69.90m, BillingPlans.Professional.PriceFor(BillingCycle.Monthly));
        Assert.Equal(796.86m, BillingPlans.Professional.PriceFor(BillingCycle.Yearly));
        Assert.Equal(179.90m, BillingPlans.Enterprise.PriceFor(BillingCycle.Monthly));
        Assert.Equal(2050.86m, BillingPlans.Enterprise.PriceFor(BillingCycle.Yearly));
    }

    [Theory]
    [InlineData(BillingCycle.Monthly, BillingPaymentMethod.CreditCard, BillingCollectionMode.RecurringSubscription)]
    [InlineData(BillingCycle.Monthly, BillingPaymentMethod.Pix, BillingCollectionMode.RecurringSubscription)]
    [InlineData(BillingCycle.Yearly, BillingPaymentMethod.CreditCard, BillingCollectionMode.RecurringSubscription)]
    [InlineData(BillingCycle.Yearly, BillingPaymentMethod.Pix, BillingCollectionMode.OneTimeUpfront)]
    [InlineData(BillingCycle.Monthly, BillingPaymentMethod.DebitCard, BillingCollectionMode.OneTimeUpfront)]
    [InlineData(BillingCycle.Yearly, BillingPaymentMethod.DebitCard, BillingCollectionMode.OneTimeUpfront)]
    public void SelectsApprovedCollectionMode(BillingCycle cycle, BillingPaymentMethod method, BillingCollectionMode expected)
    {
        Assert.Equal(expected, BillingPlans.CollectionMode(cycle, method));
    }

    [Fact]
    public void RejectsDebitAsRecurringSubscription()
    {
        Assert.Throws<ArgumentException>(() => BillingPlans.AsaasRecurringBillingType(BillingPaymentMethod.DebitCard));
    }

    [Fact]
    public void RejectsUnknownPlan()
    {
        Assert.Throws<ArgumentException>(() => BillingPlans.Get("unknown"));
    }
}
