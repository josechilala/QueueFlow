using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace QueueFlow.Application.Observability;

public static class QueueFlowTelemetry
{
    public const string Name = "QueueFlow";
    public static readonly ActivitySource ActivitySource = new(Name);
    public static readonly Meter Meter = new(Name);
    public static readonly Counter<long> Calls = Meter.CreateCounter<long>("queueflow.calls", description: "Tickets called for service.");
    public static readonly Counter<long> NotificationFailures = Meter.CreateCounter<long>("queueflow.notification.failures", description: "Notification delivery failures.");
    public static readonly Counter<long> BillingReservations = Meter.CreateCounter<long>("queueflow.billing.reservations", description: "Durably reserved sandbox checkouts.");
    public static readonly Counter<long> BillingUncertain = Meter.CreateCounter<long>("queueflow.billing.uncertain", description: "Ambiguous sandbox checkout outcomes requiring reconciliation.");
    public static readonly Counter<long> BillingEvents = Meter.CreateCounter<long>("queueflow.billing.events", description: "Durably processed sandbox payment events.");
    public static readonly Counter<long> BillingInboxOutcomes = Meter.CreateCounter<long>("queueflow.billing.inbox.outcomes", description: "Inbox outcomes acknowledged after commit; not payment confirmations.");
    public static readonly Counter<long> BillingInboxRetries = Meter.CreateCounter<long>("queueflow.billing.inbox.retries", description: "Durably scheduled inbox retries.");
    public static readonly Counter<long> BillingInboxFailures = Meter.CreateCounter<long>("queueflow.billing.inbox.failures", description: "Sanitized inbox failures by stage and kind.");
    public static readonly Histogram<double> BillingInboxDuration = Meter.CreateHistogram<double>("queueflow.billing.inbox.duration", unit: "ms", description: "Committed inbox attempt duration.");

    // Listener/logger failures must never affect a transaction or prevent the next poll.
    public static void ObserveBilling(Action observation)
    {
        try { observation(); }
        catch (Exception) { /* Best-effort telemetry is not a financial dependency. */ }
    }

    private const string BillingInboxJob = "billing_inbox";

    public static void RecordBillingPollSuccess() => ObserveBilling(() => RecordJobSuccess(BillingInboxJob));

    public static DateTimeOffset? GetBillingLastSuccessfulPollAtUtc() =>
        JobLastSuccess.TryGetValue(BillingInboxJob, out var timestamp)
            ? DateTimeOffset.FromUnixTimeSeconds((long)timestamp)
            : null;

    private static long _activeQueues;
    private static long _waitingTickets;
    private static readonly ConcurrentDictionary<string, double> JobLastSuccess = new(StringComparer.Ordinal);

    static QueueFlowTelemetry()
    {
        Meter.CreateObservableGauge("queueflow.queues.active", () => Interlocked.Read(ref _activeQueues), description: "Active queues observed by the worker.");
        Meter.CreateObservableGauge("queueflow.tickets.waiting", () => Interlocked.Read(ref _waitingTickets), description: "Tickets currently waiting.");
        Meter.CreateObservableGauge("queueflow.job.last_success", () => JobLastSuccess.Select(item => new Measurement<double>(item.Value, new KeyValuePair<string, object?>("job_name", item.Key))), unit: "s", description: "Unix timestamp of the last successful job cycle.");
    }

    public static void ObserveQueues(long activeQueues, long waitingTickets)
    {
        Interlocked.Exchange(ref _activeQueues, activeQueues);
        Interlocked.Exchange(ref _waitingTickets, waitingTickets);
    }

    public static void RecordJobSuccess(string job) => JobLastSuccess[job] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
