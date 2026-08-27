namespace QueueFlow.Application.Features.Queues;

public static class WaitTimeEstimator
{
    public static int Calculate(int ticketsAhead, int averageServiceMinutes, int activeAttendants)
    {
        if (ticketsAhead <= 0 || averageServiceMinutes <= 0) return 0;
        return (int)Math.Ceiling((double)ticketsAhead * averageServiceMinutes / Math.Max(1, activeAttendants));
    }
}
