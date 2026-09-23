using Microsoft.Extensions.Logging;
using Npgsql;
using QueueFlow.Domain.Entities;

namespace QueueFlow.Infrastructure.Persistence;

// Only allowlisted metadata is logged: exception messages can contain customer data or SQL values.
internal sealed partial class AppointmentOperationDiagnostics(ILogger logger, string operation)
{
    public string Stage { get; set; } = "validate_public_id";
    public Guid? OrganizationId { get; set; }
    public Guid? ServiceId { get; set; }
    public Guid? AppointmentId { get; set; }

    public async Task<Appointment?> RunAsync(Func<Task<Appointment?>> action)
    {
        try { return await action(); }
        catch (Exception error)
        {
            var cause = error.GetBaseException();
            LogFailure(logger, operation, Stage, OrganizationId, ServiceId, AppointmentId,
                error.GetType().Name, cause.GetType().Name, (cause as PostgresException)?.SqlState);
            throw;
        }
    }

    public void Committed()
    {
        Stage = "committed";
        LogCommitted(logger, operation, OrganizationId, ServiceId, AppointmentId);
    }

    [LoggerMessage(LogLevel.Error, "Appointment operation failed. Operation: {Operation} Stage: {Stage} OrganizationId: {OrganizationId} ServiceId: {ServiceId} AppointmentId: {AppointmentId} ExceptionType: {ExceptionType} CauseType: {CauseType} SqlState: {SqlState}")]
    private static partial void LogFailure(ILogger logger, string operation, string stage, Guid? organizationId,
        Guid? serviceId, Guid? appointmentId, string exceptionType, string causeType, string? sqlState);

    [LoggerMessage(LogLevel.Information, "Appointment transaction committed. Operation: {Operation} OrganizationId: {OrganizationId} ServiceId: {ServiceId} AppointmentId: {AppointmentId}")]
    private static partial void LogCommitted(ILogger logger, string operation, Guid? organizationId, Guid? serviceId, Guid? appointmentId);
}
