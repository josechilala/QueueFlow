using QueueFlow.Application.Abstractions.Persistence;
using QueueFlow.Application.Features.Appointments;
using QueueFlow.Domain.Entities;

namespace QueueFlow.Application.Tests.Appointments;

public sealed class AppointmentEmailValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-an-email")]
    [InlineData("a@localhost")]
    [InlineData("Name <a@example.com>")]
    [InlineData("a@example.com,b@example.com")]
    [InlineData("a\r\n@example.com")]
    [InlineData("a b@example.com")]
    public async Task InvalidEmailNeverCreatesAnAppointment(string? email)
    {
        var service = new AppointmentBookingService(new NoBooking());
        var result = await service.CreateAsync(new("branch", "service", DateTimeOffset.UtcNow, "Cliente", null, email), TestContext.Current.CancellationToken);
        Assert.False(result.IsSuccess);
        Assert.Equal("appointments.invalid_email", result.Error.Code);
    }

    [Theory]
    [InlineData("cliente@example.com")]
    [InlineData("  Cliente+agenda@Example.com  ")]
    public void AcceptsValidMailbox(string email) => Assert.True(AppointmentReceipt.IsValidEmail(email));

    private sealed class NoBooking : IAppointmentOperations
    {
        public Task<Appointment?> CreateAsync(CreateAppointmentData request, CancellationToken ct) => throw new InvalidOperationException("Must not book invalid input.");
        public Task<Appointment?> CancelAsync(string token, CancellationToken ct) => throw new NotSupportedException();
        public Task<Appointment?> RescheduleAsync(string token, DateTimeOffset start, CancellationToken ct) => throw new NotSupportedException();
        public Task<AppointmentCheckInData?> CheckInAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    }
}
