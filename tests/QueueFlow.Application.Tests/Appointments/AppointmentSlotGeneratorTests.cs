using QueueFlow.Application.Features.Appointments;

namespace QueueFlow.Application.Tests.Appointments;

public sealed class AppointmentSlotGeneratorTests
{
    private static readonly DateOnly Date = new(2026, 8, 26);

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
