using QueueFlow.Domain.Common;

namespace QueueFlow.Domain.ValueObjects;

public readonly record struct TenantId
{
    public TenantId(Guid value)
    {
        if (value == Guid.Empty) throw new DomainException("Tenant identifier cannot be empty.");
        Value = value;
    }
    public Guid Value { get; }
}

public sealed record TicketNumber
{
    public TicketNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new DomainException("Ticket number is required.");
        Value = value.Trim().ToUpperInvariant();
    }
    public string Value { get; }
    public override string ToString() => Value;
}

public readonly record struct EstimatedWaitTime
{
    public EstimatedWaitTime(int minutes)
    {
        if (minutes < 0) throw new DomainException("Estimated wait cannot be negative.");
        Minutes = minutes;
    }
    public int Minutes { get; }
}
