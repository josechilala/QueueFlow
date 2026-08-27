namespace QueueFlow.Application.Features.Queues;

public sealed record QueueCallCandidate(Guid TicketId, long SequenceNumber, DateTimeOffset IssuedAt, int Priority, DateTimeOffset? ScheduledStart, DateTimeOffset? CheckedInAt, int LateToleranceMinutes);

public static class HybridQueuePolicy
{
    public static QueueCallCandidate? SelectNext(IReadOnlyCollection<QueueCallCandidate> candidates, DateTimeOffset now, int slotDurationMinutes, int capacityPerSlot, int activeScheduledAttendances)
    {
        var callable = candidates.Where(candidate => candidate.ScheduledStart is null || candidate.ScheduledStart <= now).ToArray();
        if (callable.Length == 0) return null;
        var starvationThreshold = now.AddMinutes(-Math.Max(5, slotDurationMinutes));
        var starvedWalkIn = callable.Where(candidate => candidate.ScheduledStart is null && candidate.IssuedAt <= starvationThreshold)
            .OrderByDescending(candidate => candidate.Priority).ThenBy(candidate => candidate.IssuedAt).ThenBy(candidate => candidate.SequenceNumber).FirstOrDefault();
        if (starvedWalkIn is not null) return starvedWalkIn;
        if (activeScheduledAttendances < Math.Max(1, capacityPerSlot))
        {
            var eligibleAppointment = callable.Where(candidate => candidate.ScheduledStart is not null && now <= candidate.ScheduledStart.Value.AddMinutes(candidate.LateToleranceMinutes))
                .OrderBy(candidate => candidate.ScheduledStart).ThenBy(candidate => candidate.CheckedInAt).ThenBy(candidate => candidate.SequenceNumber).FirstOrDefault();
            if (eligibleAppointment is not null) return eligibleAppointment;
        }
        return callable.OrderByDescending(candidate => candidate.Priority).ThenBy(candidate => candidate.IssuedAt).ThenBy(candidate => candidate.SequenceNumber).First();
    }
}
