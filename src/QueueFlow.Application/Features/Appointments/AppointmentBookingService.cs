using QueueFlow.Application.Abstractions.Persistence;
using QueueFlow.Application.Common;
using QueueFlow.Domain.Common;
using QueueFlow.Domain.Enums;

namespace QueueFlow.Application.Features.Appointments;

public sealed record CreatePublicAppointment(string BranchPublicId, string ServicePublicId, DateTimeOffset ScheduledStart, string CustomerName, string? CustomerPhone, string? CustomerEmail);
public sealed record CreatedAppointmentDto(string PublicToken, string ConfirmationCode, AppointmentStatus Status, DateTimeOffset ScheduledStart, DateTimeOffset ScheduledEnd);

public sealed class AppointmentBookingService(IAppointmentOperations operations)
{
    public async Task<Result<CreatedAppointmentDto>> CreateAsync(CreatePublicAppointment request, CancellationToken cancellationToken)
    {
        if (!AppointmentReceipt.IsValidEmail(request.CustomerEmail))
            return Result.Failure<CreatedAppointmentDto>(new("appointments.invalid_email", "Informe um e-mail válido para receber o comprovante."));
        if (string.IsNullOrWhiteSpace(request.CustomerName) || request.CustomerName.Trim().Length > 200 || request.CustomerPhone?.Trim().Length > 30)
            return Result.Failure<CreatedAppointmentDto>(new("appointments.invalid_customer", "Confira os dados informados para o agendamento."));
        try
        {
            var appointment = await operations.CreateAsync(new(request.BranchPublicId, request.ServicePublicId, request.ScheduledStart, request.CustomerName, request.CustomerPhone, request.CustomerEmail), cancellationToken);
            return appointment is null
                ? Result.Failure<CreatedAppointmentDto>(new("appointments.slot_unavailable", "Este horário não está mais disponível."))
                : Result.Success(new CreatedAppointmentDto(appointment.PublicToken, appointment.ConfirmationCode, appointment.Status, appointment.ScheduledStart, appointment.ScheduledEnd));
        }
        catch (DomainException)
        {
            return Result.Failure<CreatedAppointmentDto>(new("appointments.invalid_request", "Não foi possível criar o agendamento com os dados informados."));
        }
    }
}
