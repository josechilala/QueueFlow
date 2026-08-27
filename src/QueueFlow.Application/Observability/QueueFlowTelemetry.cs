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
