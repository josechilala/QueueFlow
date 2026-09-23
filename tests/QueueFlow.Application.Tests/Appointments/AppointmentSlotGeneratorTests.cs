using QueueFlow.Application.Features.Appointments;

namespace QueueFlow.Application.Tests.Appointments;

public sealed class AppointmentSlotGeneratorTests
{
    private static readonly DateOnly Date = new(2026, 8, 26);

    [Fact]
    public void RepeatedSchedulesProduceOneSlotPerInstantWithoutMultiplyingCapacity()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        var start = new DateTimeOffset(2026, 8, 26, 16, 30, 0, TimeSpan.Zero);
        var slots = AppointmentSlotGenerator.Generate(Date, zone,
            [(new(13, 30), new(14, 30)), (new(13, 30), new(14, 30)), (new(13, 30), new(14, 30))],
            30, 2, DateTimeOffset.MinValue, DateTimeOffset.MaxValue, [], [new(start, start.AddMinutes(30))]);

        Assert.Collection(slots,
            first => { Assert.Equal(start, first.StartAt); Assert.Equal(start.AddMinutes(30), first.EndAt); Assert.Equal(1, first.RemainingCapacity); },
            second => { Assert.Equal(start.AddMinutes(30), second.StartAt); Assert.Equal(2, second.RemainingCapacity); });
        Assert.NotNull(slots.SingleOrDefault(x => x.StartAt == start));
    }

    [Fact]
    public void OverlappingSchedulesPreserveTheirGridsBlocksAndAdvanceWindowWithoutDuplicateSlots()
    {
        var start = new DateTimeOffset(2026, 8, 26, 9, 0, 0, TimeSpan.Zero);
        var slots = AppointmentSlotGenerator.Generate(Date, TimeZoneInfo.Utc,
            [(new(9, 0), new(11, 0)), (new(9, 30), new(11, 30)), (new(9, 15), new(10, 15))],
            30, 1, start.AddMinutes(15), start.AddMinutes(90),
            [new(start.AddHours(1), start.AddMinutes(90))], []);

        Assert.Equal(new[] { start.AddMinutes(15), start.AddMinutes(30), start.AddMinutes(90) }, slots.Select(x => x.StartAt));
        Assert.All(slots, slot => Assert.Equal(1, slot.RemainingCapacity));
    }

    [Fact]
    public void GeneratesSlotsInsideScheduleAndRespectsCapacity()
    {
        var slots = AppointmentSlotGenerator.Generate(Date, TimeZoneInfo.Utc, [(new(9, 0), new(10, 0))], 30, 2,
            new DateTimeOffset(2026, 8, 26, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 27, 0, 0, 0, TimeSpan.Zero), [],
            [new(new DateTimeOffset(2026, 8, 26, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 26, 9, 30, 0, TimeSpan.Zero))]);

        Assert.Collection(slots,
            first => { Assert.Equal(9, first.StartAt.Hour); Assert.Equal(1, first.RemainingCapacity); },
            second => { Assert.Equal(9, second.StartAt.Hour); Assert.Equal(30, second.StartAt.Minute); Assert.Equal(2, second.RemainingCapacity); });
    }

    [Fact]
    public void RemovesBlockedFullAndTooEarlySlots()
    {
        var slots = AppointmentSlotGenerator.Generate(Date, TimeZoneInfo.Utc, [(new(9, 0), new(11, 0))], 30, 1,
            new DateTimeOffset(2026, 8, 26, 9, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 26, 11, 0, 0, TimeSpan.Zero),
            [new(new DateTimeOffset(2026, 8, 26, 10, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 26, 10, 30, 0, TimeSpan.Zero))],
            [new(new DateTimeOffset(2026, 8, 26, 9, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 26, 10, 0, 0, TimeSpan.Zero))]);

        var slot = Assert.Single(slots);
        Assert.Equal(new DateTimeOffset(2026, 8, 26, 10, 30, 0, TimeSpan.Zero), slot.StartAt);
    }

    [Fact]
    public void ConvertsLocalScheduleToUtc()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        var slot = Assert.Single(AppointmentSlotGenerator.Generate(Date, zone, [(new(9, 0), new(9, 30))], 30, 1,
            DateTimeOffset.MinValue, DateTimeOffset.MaxValue, [], []));
        Assert.Equal(new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero), slot.StartAt);
    }

    [Fact]
    public void ReturnsNoSlotsForClosedDay()
    {
        var slots = AppointmentSlotGenerator.Generate(Date, TimeZoneInfo.Utc, [], 30, 1, DateTimeOffset.MinValue, DateTimeOffset.MaxValue, [], []);
        Assert.Empty(slots);
    }

    [Fact]
    public void ReturnsNoSlotsWhenCapacityIsFull()
    {
        var occupied = new SlotWindow(new DateTimeOffset(2026, 8, 26, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 26, 9, 30, 0, TimeSpan.Zero));
        var slots = AppointmentSlotGenerator.Generate(Date, TimeZoneInfo.Utc, [(new(9, 0), new(9, 30))], 30, 1, DateTimeOffset.MinValue, DateTimeOffset.MaxValue, [], [occupied]);
        Assert.Empty(slots);
    }

    [Fact]
    public void ReturnsNoSlotsOutsideAdvanceWindow()
    {
        var slots = AppointmentSlotGenerator.Generate(Date, TimeZoneInfo.Utc, [(new(9, 0), new(10, 0))], 30, 1,
            new DateTimeOffset(2026, 8, 27, 0, 0, 0, TimeSpan.Zero), DateTimeOffset.MaxValue, [], []);
        Assert.Empty(slots);
    }
}
