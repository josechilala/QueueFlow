namespace QueueFlow.Application.Features.Appointments;

public sealed record SlotWindow(DateTimeOffset StartAt, DateTimeOffset EndAt);
public sealed record AvailableSlotDto(DateTimeOffset StartAt, DateTimeOffset EndAt, int RemainingCapacity);

public static class AppointmentSlotGenerator
{
    public static IReadOnlyList<AvailableSlotDto> Generate(
        DateOnly date,
        TimeZoneInfo timeZone,
        IReadOnlyCollection<(TimeOnly Start, TimeOnly End)> schedules,
        int durationMinutes,
        int capacity,
        DateTimeOffset earliestStart,
        DateTimeOffset latestStart,
        IReadOnlyCollection<SlotWindow> blocks,
        IReadOnlyCollection<SlotWindow> reservations)
    {
        if (durationMinutes <= 0 || capacity <= 0) throw new ArgumentOutOfRangeException(nameof(durationMinutes));
        var duration = TimeSpan.FromMinutes(durationMinutes);
        var result = new List<AvailableSlotDto>();
        foreach (var schedule in schedules.OrderBy(x => x.Start))
        {
            for (var localStart = date.ToDateTime(schedule.Start); localStart.Add(duration) <= date.ToDateTime(schedule.End); localStart = localStart.Add(duration))
            {
                if (timeZone.IsInvalidTime(localStart)) continue;
                var localEnd = localStart.Add(duration);
                var start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localStart, DateTimeKind.Unspecified), timeZone), TimeSpan.Zero);
                var end = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localEnd, DateTimeKind.Unspecified), timeZone), TimeSpan.Zero);
                if (start < earliestStart || start > latestStart || blocks.Any(block => Overlaps(start, end, block))) continue;
                var used = reservations.Count(reservation => Overlaps(start, end, reservation));
                if (used < capacity) result.Add(new(start, end, capacity - used));
            }
        }
        // Overlapping schedules describe availability, not additional capacity for the same instant.
        return result.DistinctBy(x => x.StartAt).OrderBy(x => x.StartAt).ToArray();
    }

    private static bool Overlaps(DateTimeOffset start, DateTimeOffset end, SlotWindow other) => start < other.EndAt && end > other.StartAt;
}
