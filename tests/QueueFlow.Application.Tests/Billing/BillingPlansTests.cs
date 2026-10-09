using QueueFlow.Application.Features.Billing;

namespace QueueFlow.Application.Tests.Billing;

public sealed class BillingPlansTests
{
    [Theory]
    [InlineData("professional", 69.90, 796.86)]
    [InlineData("enterprise", 179.90, 2050.86)]
    public void CalculatesApprovedPrices(string code, decimal monthly, decimal annual)
    {
        var plan = BillingPlans.Get(code);
        Assert.Equal(monthly, plan.PriceFor(BillingCycle.Monthly));
        Assert.Equal(annual, plan.PriceFor(BillingCycle.Yearly));
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
