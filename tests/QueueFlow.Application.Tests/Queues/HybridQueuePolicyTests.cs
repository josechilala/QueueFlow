using QueueFlow.Application.Features.Queues;

namespace QueueFlow.Application.Tests.Queues;

public sealed class HybridQueuePolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 10, 0, 0, TimeSpan.Zero);
    private static QueueCallCandidate WalkIn(long sequence, int minutesAgo = 5) => new(Guid.NewGuid(), sequence, Now.AddMinutes(-minutesAgo), 1, null, null, 0);
    private static QueueCallCandidate Appointment(long sequence, int scheduledOffsetMinutes, int checkedInMinutesAgo = 10, int tolerance = 10) => new(Guid.NewGuid(), sequence, Now.AddMinutes(-checkedInMinutesAgo), 1, Now.AddMinutes(scheduledOffsetMinutes), Now.AddMinutes(-checkedInMinutesAgo), tolerance);

    [Fact] public void AppointmentBeforeScheduledTimeIsNotCallable() { var walkIn = WalkIn(1); Assert.Equal(walkIn.TicketId, HybridQueuePolicy.SelectNext([Appointment(2, 5), walkIn], Now, 30, 1, 0)!.TicketId); }
    [Fact] public void EligibleAppointmentHasTemporalPriority() { var appointment = Appointment(2, 0); Assert.Equal(appointment.TicketId, HybridQueuePolicy.SelectNext([WalkIn(1), appointment], Now, 30, 1, 0)!.TicketId); }
    [Fact] public void MultipleAppointmentsUseScheduleCheckInAndSequence() { var later = Appointment(1, 0, 20); var earlier = Appointment(2, -5, 5); Assert.Equal(earlier.TicketId, HybridQueuePolicy.SelectNext([later, earlier], Now, 30, 2, 0)!.TicketId); }
    [Fact] public void WalkInsRemainFifoWithoutEligibleAppointment() { var first = WalkIn(1, 10); Assert.Equal(first.TicketId, HybridQueuePolicy.SelectNext([WalkIn(2, 5), first], Now, 30, 1, 0)!.TicketId); }
    [Fact] public void LateAppointmentReturnsToNormalOrdering() { var walkIn = WalkIn(1, 15); Assert.Equal(walkIn.TicketId, HybridQueuePolicy.SelectNext([Appointment(2, -20, tolerance: 10), walkIn], Now, 30, 1, 0)!.TicketId); }
    [Fact] public void ReservedCapacityPreventsAnotherAppointmentPriority() { var walkIn = WalkIn(1, 15); Assert.Equal(walkIn.TicketId, HybridQueuePolicy.SelectNext([Appointment(2, 0), walkIn], Now, 30, 1, 1)!.TicketId); }
    [Fact] public void AgingPreventsWalkInStarvation() { var starved = WalkIn(1, 31); Assert.Equal(starved.TicketId, HybridQueuePolicy.SelectNext([Appointment(2, 0), starved], Now, 30, 10, 0)!.TicketId); }

    [Fact]
    public void ContinuousAppointmentsDoNotStarveWalkIn()
    {
        var start = Now;
        var walkIn = new QueueCallCandidate(Guid.NewGuid(), 1, start, 1, null, null, 0);
        var waiting = new List<QueueCallCandidate> { walkIn };
        waiting.AddRange(Enumerable.Range(0, 61).Select(index =>
            new QueueCallCandidate(Guid.NewGuid(), index + 2, start.AddMinutes(index - 10), 1, start.AddMinutes(index), start.AddMinutes(index - 10), 60)));

        DateTimeOffset? walkInCalledAt = null;
        for (var minute = 0; minute <= 60 && waiting.Count > 0; minute++)
        {
            var callTime = start.AddMinutes(minute);
            var selected = HybridQueuePolicy.SelectNext(waiting, callTime, 30, 10, 0);
            Assert.NotNull(selected);
            waiting.Remove(selected);
            if (selected.TicketId == walkIn.TicketId) { walkInCalledAt = callTime; break; }
        }

        Assert.Equal(start.AddMinutes(30), walkInCalledAt);
    }
    [Fact] public void QueueOnlyRegressionKeepsPriorityThenFifo() { var priority = new QueueCallCandidate(Guid.NewGuid(), 2, Now.AddMinutes(-1), 2, null, null, 0); Assert.Equal(priority.TicketId, HybridQueuePolicy.SelectNext([WalkIn(1), priority], Now, 30, 1, 0)!.TicketId); }

    [Fact]
    public void SameScheduledTimeUsesCheckedInAt()
    {
        var checkedInFirst = Appointment(2, 0, checkedInMinutesAgo: 20);
        var checkedInSecond = Appointment(1, 0, checkedInMinutesAgo: 10);

        Assert.Equal(checkedInFirst.TicketId, HybridQueuePolicy.SelectNext([checkedInSecond, checkedInFirst], Now, 30, 2, 0)!.TicketId);
    }

    [Fact]
    public void SameScheduledTimeAndCheckInUsesSequenceNumber()
    {
        var firstSequence = Appointment(1, 0);
        var secondSequence = Appointment(2, 0);

        Assert.Equal(firstSequence.TicketId, HybridQueuePolicy.SelectNext([secondSequence, firstSequence], Now, 30, 2, 0)!.TicketId);
    }

    [Fact]
    public void LateAppointmentWithinToleranceRemainsEligible()
    {
        var appointment = Appointment(2, -8, checkedInMinutesAgo: -8, tolerance: 10);

        Assert.Equal(appointment.TicketId, HybridQueuePolicy.SelectNext([WalkIn(1, 5), appointment], Now, 30, 1, 0)!.TicketId);
    }

    [Fact]
    public void LateAppointmentOutsideToleranceLosesPriority()
    {
        var walkIn = WalkIn(1, 15);
        var appointment = Appointment(2, -15, checkedInMinutesAgo: -15, tolerance: 10);

        Assert.Equal(walkIn.TicketId, HybridQueuePolicy.SelectNext([appointment, walkIn], Now, 30, 1, 0)!.TicketId);
    }

    [Fact]
    public void EligibilityChangesExactlyAtScheduledStart()
    {
        var appointment = Appointment(2, 0);
        var walkIn = WalkIn(1, 5);

        Assert.Equal(walkIn.TicketId, HybridQueuePolicy.SelectNext([appointment, walkIn], Now.AddSeconds(-1), 30, 1, 0)!.TicketId);
        Assert.Equal(appointment.TicketId, HybridQueuePolicy.SelectNext([appointment, walkIn], Now, 30, 1, 0)!.TicketId);
    }

    [Fact]
    public void EquivalentInstantsWithDifferentOffsetsUseUtcTimeline()
    {
        var scheduledInSaoPaulo = Now.ToOffset(TimeSpan.FromHours(-3));
        var appointment = new QueueCallCandidate(Guid.NewGuid(), 2, Now.AddMinutes(-10), 1, scheduledInSaoPaulo, Now.AddMinutes(-10), 10);

        Assert.Equal(appointment.TicketId, HybridQueuePolicy.SelectNext([WalkIn(1), appointment], Now, 30, 1, 0)!.TicketId);
    }

    [Fact]
    public void ReservedScheduledCapacityFallsBackToWalkIn()
    {
        var walkIn = WalkIn(1, 15);

        Assert.Equal(walkIn.TicketId, HybridQueuePolicy.SelectNext([Appointment(2, 0), walkIn], Now, 30, 2, 2)!.TicketId);
    }
}
