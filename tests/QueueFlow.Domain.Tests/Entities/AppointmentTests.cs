using QueueFlow.Domain.Common;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;

namespace QueueFlow.Domain.Tests.Entities;

public sealed class AppointmentTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 27, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AppointmentWithoutRequiredConfirmationStartsConfirmed()
    {
        var appointment = Create(requireConfirmation: false);
        Assert.Equal(AppointmentStatus.Confirmed, appointment.Status);
        Assert.Equal(32, appointment.PublicToken.Length);
    }

    [Fact]
    public void RequiredConfirmationFollowsValidLifecycle()
    {
        var appointment = Create(requireConfirmation: true);
        appointment.Confirm(Start.AddMinutes(-30));
        appointment.CheckIn(Guid.NewGuid(), Start.AddMinutes(-10));
        appointment.Complete(Start.AddMinutes(30));
        Assert.Equal(AppointmentStatus.Completed, appointment.Status);
    }

    [Fact]
    public void CheckInCanCreateOnlyOneTicket()
    {
        var appointment = Create();
        appointment.CheckIn(Guid.NewGuid(), Start);
        Assert.Throws<DomainException>(() => appointment.CheckIn(Guid.NewGuid(), Start));
    }

    [Theory]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.NoShow)]
    public void TerminalStatesRejectCommonTransitions(AppointmentStatus target)
    {
        var appointment = Create();
        if (target == AppointmentStatus.Completed) { appointment.CheckIn(Guid.NewGuid(), Start); appointment.Complete(Start); }
        if (target == AppointmentStatus.Cancelled) appointment.Cancel(Start);
        if (target == AppointmentStatus.NoShow) appointment.MarkNoShow(Start);
        Assert.Throws<DomainException>(() => appointment.Cancel(Start));
    }

    [Fact]
    public void ScheduleRequiresPositiveRange()
    {
        Assert.Throws<DomainException>(() => new ServiceSchedule(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DayOfWeek.Monday, new TimeOnly(10, 0), new TimeOnly(9, 0), Start));
    }

    [Fact]
    public void SchedulingSettingsRejectInvalidCapacity()
    {
        var settings = new ServiceSchedulingSettings(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Start);
        Assert.Throws<DomainException>(() => settings.Configure(20, 0, 60, 30, 10, 60, 30, true, true, false, true, Start));
    }

    private static Appointment Create(bool requireConfirmation = false) => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Cliente", null, "cliente@example.com", Start, Start.AddMinutes(20), "America/Sao_Paulo", requireConfirmation, Start.AddDays(-1));
}
