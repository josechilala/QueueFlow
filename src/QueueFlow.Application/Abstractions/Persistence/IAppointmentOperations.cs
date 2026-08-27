using QueueFlow.Domain.Entities;

namespace QueueFlow.Application.Abstractions.Persistence;

public sealed record CreateAppointmentData(string BranchPublicId, string ServicePublicId, DateTimeOffset ScheduledStart, string CustomerName, string? CustomerPhone, string? CustomerEmail);
public sealed record AppointmentCheckInData(Appointment Appointment, QueueTicket Ticket);

public interface IAppointmentOperations
{
    Task<Appointment?> CreateAsync(CreateAppointmentData request, CancellationToken cancellationToken);
    Task<Appointment?> CancelAsync(string publicToken, CancellationToken cancellationToken);
    Task<Appointment?> RescheduleAsync(string publicToken, DateTimeOffset scheduledStart, CancellationToken cancellationToken);
    Task<AppointmentCheckInData?> CheckInAsync(string publicToken, CancellationToken cancellationToken);
    Task<AppointmentCheckInData?> CheckInAsync(Guid appointmentId, CancellationToken cancellationToken);
}
