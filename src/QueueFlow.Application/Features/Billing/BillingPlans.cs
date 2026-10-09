namespace QueueFlow.Application.Features.Billing;

/// <summary>Commercial catalog for sandbox checkout; amounts are in BRL.</summary>
public enum BillingCycle { Monthly, Yearly }
public enum BillingPaymentMethod { CreditCard, DebitCard, Pix }
public enum BillingCollectionMode { RecurringSubscription, OneTimeUpfront }

public sealed record BillingPlan(string Code, string Name, decimal MonthlyPrice, int? IncludedAttendants)
{
    public decimal AnnualPrice => decimal.Round(MonthlyPrice * 12m * 0.95m, 2, MidpointRounding.AwayFromZero);

    public decimal PriceFor(BillingCycle cycle) => cycle switch
    {
        BillingCycle.Monthly => MonthlyPrice,
        BillingCycle.Yearly => AnnualPrice,
        _ => throw new ArgumentOutOfRangeException(nameof(cycle))
    };
}

public static class BillingPlans
{
    public const decimal AnnualDiscount = 0.05m;
    public static readonly BillingPlan Professional = new("professional", "Profissional", 69.90m, 4);
    // Enterprise capacity can be expanded; specific seat pricing remains to be defined.
    public static readonly BillingPlan Enterprise = new("enterprise", "Empresarial", 179.90m, null);

    public static BillingPlan Get(string code) => code?.Trim().ToLowerInvariant() switch
    {
        "professional" => Professional,
        "enterprise" => Enterprise,
        _ => throw new ArgumentException("Unknown billing plan.", nameof(code))
    };

    public static BillingCollectionMode CollectionMode(BillingCycle cycle, BillingPaymentMethod method) =>
        (cycle, method) switch
        {
            (BillingCycle.Monthly, BillingPaymentMethod.CreditCard) => BillingCollectionMode.RecurringSubscription,
            (BillingCycle.Monthly, BillingPaymentMethod.Pix) => BillingCollectionMode.RecurringSubscription,
            (BillingCycle.Yearly, BillingPaymentMethod.CreditCard) => BillingCollectionMode.RecurringSubscription,
            (BillingCycle.Monthly or BillingCycle.Yearly, BillingPaymentMethod.DebitCard) => BillingCollectionMode.OneTimeUpfront,
            (BillingCycle.Yearly, BillingPaymentMethod.Pix) => BillingCollectionMode.OneTimeUpfront,
            _ => throw new ArgumentOutOfRangeException(nameof(method))
        };

    public static string AsaasCycle(BillingCycle cycle) => cycle switch
    {
        BillingCycle.Monthly => "MONTHLY",
        BillingCycle.Yearly => "YEARLY",
        _ => throw new ArgumentOutOfRangeException(nameof(cycle))
    };

    public static string AsaasRecurringBillingType(BillingPaymentMethod method) => method switch
    {
        BillingPaymentMethod.CreditCard => "CREDIT_CARD",
        BillingPaymentMethod.Pix => "PIX",
        _ => throw new ArgumentException("Debit card is not a recurring Asaas subscription billing type.", nameof(method))
    };
}
