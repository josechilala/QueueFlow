namespace QueueFlow.Application.Features.Billing;

/// <summary>
/// Billing periods supported by the Asaas subscription API.
/// Commercial prices are deliberately not hard-coded.
/// </summary>
public enum BillingCycle { Monthly, Yearly }

public enum BillingPaymentMethod { CreditCard, Pix }

public sealed record BillingPlan(string Code, string Name, decimal MonthlyPrice, decimal YearlyPrice)
{
    public BillingPlan
    {
        if (string.IsNullOrWhiteSpace(Code) || string.IsNullOrWhiteSpace(Name))
            throw new ArgumentException("Plan code and name are required.");
        if (MonthlyPrice <= 0 || YearlyPrice <= 0)
            throw new ArgumentOutOfRangeException(nameof(MonthlyPrice), "Plan prices must be positive.");
    }

    public decimal PriceFor(BillingCycle cycle) => cycle switch
    {
        BillingCycle.Monthly => MonthlyPrice,
        BillingCycle.Yearly => YearlyPrice,
        _ => throw new ArgumentOutOfRangeException(nameof(cycle))
    };

    public static string AsaasCycle(BillingCycle cycle) => cycle switch
    {
        BillingCycle.Monthly => "MONTHLY",
        BillingCycle.Yearly => "YEARLY",
        _ => throw new ArgumentOutOfRangeException(nameof(cycle))
    };

    public static string AsaasBillingType(BillingPaymentMethod method) => method switch
    {
        BillingPaymentMethod.CreditCard => "CREDIT_CARD",
        BillingPaymentMethod.Pix => "PIX",
        _ => throw new ArgumentOutOfRangeException(nameof(method))
    };
}
