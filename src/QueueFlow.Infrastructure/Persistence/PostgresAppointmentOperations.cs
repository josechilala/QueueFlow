using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QueueFlow.Application.Abstractions.Clock;
using QueueFlow.Application.Abstractions.Persistence;
using QueueFlow.Application.Features.Appointments;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;
using System.Security.Cryptography;
using System.Text.Json;

namespace QueueFlow.Infrastructure.Persistence;

internal sealed class PostgresAppointmentOperations(ApplicationDbContext db, IClock clock, ILogger<PostgresAppointmentOperations> logger) : IAppointmentOperations
{
    public Task<Appointment?> CreateAsync(CreateAppointmentData request, CancellationToken cancellationToken)
    {
        var diagnostics = new AppointmentOperationDiagnostics(logger, "create");
        return diagnostics.RunAsync(() => CreateCoreAsync(request, diagnostics, cancellationToken));
    }

    private async Task<Appointment?> CreateCoreAsync(CreateAppointmentData request, AppointmentOperationDiagnostics diagnostics, CancellationToken cancellationToken)
    {
        if (!ValidPublicId(request.BranchPublicId) || !ValidPublicId(request.ServicePublicId)) return null;
        diagnostics.Stage = "begin_transaction";
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        diagnostics.Stage = "load_branch";
        var branch = await db.Branches.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.PublicId == request.BranchPublicId && x.IsActive, cancellationToken);
        if (branch is null) return null;
        diagnostics.OrganizationId = branch.OrganizationId;
        diagnostics.Stage = "load_organization";
        if (!await db.Organizations.AnyAsync(x => x.Id == branch.OrganizationId && x.IsActive, cancellationToken)) return null;
        diagnostics.Stage = "load_service";
        var service = await db.Services.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.OrganizationId == branch.OrganizationId && x.BranchId == branch.Id && x.PublicId == request.ServicePublicId && x.IsActive, cancellationToken);
        if (service is null || service.AttendanceMode == ServiceAttendanceMode.QueueOnly) return null;
        diagnostics.ServiceId = service.Id;
        diagnostics.Stage = "lock_settings";
        var settings = await LockSettingsAsync(branch.OrganizationId, service.Id, cancellationToken);
        if (settings is null || !settings.IsActive || settings.BranchId != branch.Id) return null;
        var appointment = await BookSlotAsync(branch, service, settings, request.ScheduledStart, request.CustomerName, request.CustomerPhone, request.CustomerEmail, null, null, diagnostics, cancellationToken);
        if (appointment is null) return null;
        diagnostics.AppointmentId = appointment.Id;
        diagnostics.Stage = "prepare_events";
        AddRealtime(appointment, "appointment.created");
        diagnostics.Stage = "prepare_receipt";
        await AppointmentReceipt.EnqueueAsync(db, appointment, clock.UtcNow, cancellationToken);
        diagnostics.Stage = "save_changes";
        await db.SaveChangesAsync(cancellationToken);
        diagnostics.Stage = "commit_transaction";
        await transaction.CommitAsync(cancellationToken);
        diagnostics.Committed();
        return appointment;
    }

    public async Task<Appointment?> CancelAsync(string publicToken, CancellationToken cancellationToken)
    {
        if (!ValidPublicId(publicToken)) return null;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var appointment = await db.Appointments.FromSqlInterpolated($"SELECT *, xmin FROM \"Appointments\" WHERE \"PublicToken\" = {publicToken} FOR UPDATE").IgnoreQueryFilters().SingleOrDefaultAsync(cancellationToken);
        if (appointment is null) return null;
        var settings = await LockSettingsAsync(appointment.OrganizationId, appointment.ServiceId, cancellationToken);
        if (settings is null || !settings.AllowCustomerCancellation || clock.UtcNow > appointment.ScheduledStart.AddMinutes(-settings.CancellationDeadlineMinutes)) return null;
        var previous = appointment.Status; appointment.Cancel(clock.UtcNow); AddHistory(appointment, previous, "Cancelado pelo cliente");
        AddRealtime(appointment, "appointment.cancelled");
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return appointment;
    }

    public Task<Appointment?> RescheduleAsync(string publicToken, DateTimeOffset scheduledStart, CancellationToken cancellationToken)
    {
        var diagnostics = new AppointmentOperationDiagnostics(logger, "reschedule");
        return diagnostics.RunAsync(() => RescheduleCoreAsync(publicToken, scheduledStart, diagnostics, cancellationToken));
    }

    private async Task<Appointment?> RescheduleCoreAsync(string publicToken, DateTimeOffset scheduledStart, AppointmentOperationDiagnostics diagnostics, CancellationToken cancellationToken)
    {
        if (!ValidPublicId(publicToken)) return null;
        diagnostics.Stage = "begin_transaction";
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        diagnostics.Stage = "lock_appointment";
        var original = await db.Appointments.FromSqlInterpolated($"SELECT *, xmin FROM \"Appointments\" WHERE \"PublicToken\" = {publicToken} FOR UPDATE").IgnoreQueryFilters().SingleOrDefaultAsync(cancellationToken);
        if (original is null) return null;
        diagnostics.OrganizationId = original.OrganizationId;
        diagnostics.ServiceId = original.ServiceId;
        diagnostics.AppointmentId = original.Id;
        diagnostics.Stage = "lock_settings";
        var settings = await LockSettingsAsync(original.OrganizationId, original.ServiceId, cancellationToken);
        if (settings is null || !settings.AllowCustomerReschedule || clock.UtcNow > original.ScheduledStart.AddMinutes(-settings.CancellationDeadlineMinutes)) return null;
        diagnostics.Stage = "load_branch";
        var branch = await db.Branches.IgnoreQueryFilters().SingleAsync(x => x.OrganizationId == original.OrganizationId && x.Id == original.BranchId && x.IsActive, cancellationToken);
        diagnostics.OrganizationId = branch.OrganizationId;
        diagnostics.Stage = "load_service";
        var service = await db.Services.IgnoreQueryFilters().SingleAsync(x => x.OrganizationId == original.OrganizationId && x.Id == original.ServiceId && x.IsActive, cancellationToken);
        var replacement = await BookSlotAsync(branch, service, settings, scheduledStart, original.CustomerName, original.CustomerPhone, original.CustomerEmail, original.Id, original.Id, diagnostics, cancellationToken);
        if (replacement is null) return null;
        diagnostics.AppointmentId = replacement.Id;
        diagnostics.Stage = "prepare_events";
        var previous = original.Status; original.MarkRescheduled(clock.UtcNow); AddHistory(original, previous, "Reagendado pelo cliente");
        AddRealtime(original, "appointment.rescheduled"); AddRealtime(replacement, "appointment.created");
        diagnostics.Stage = "prepare_receipt";
        await AppointmentReceipt.EnqueueAsync(db, replacement, clock.UtcNow, cancellationToken);
        diagnostics.Stage = "save_changes";
        await db.SaveChangesAsync(cancellationToken);
        diagnostics.Stage = "commit_transaction";
        await transaction.CommitAsync(cancellationToken);
        diagnostics.Committed();
        return replacement;
    }

    public Task<AppointmentCheckInData?> CheckInAsync(string publicToken, CancellationToken cancellationToken) => CheckInCoreAsync(publicToken, null, cancellationToken);
    public Task<AppointmentCheckInData?> CheckInAsync(Guid appointmentId, CancellationToken cancellationToken) => CheckInCoreAsync(null, appointmentId, cancellationToken);

    private async Task<AppointmentCheckInData?> CheckInCoreAsync(string? publicToken, Guid? appointmentId, CancellationToken cancellationToken)
    {
        if (publicToken is not null && !ValidPublicId(publicToken)) return null;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var appointment = publicToken is not null
            ? await db.Appointments.FromSqlInterpolated($"SELECT *, xmin FROM \"Appointments\" WHERE \"PublicToken\" = {publicToken} FOR UPDATE").IgnoreQueryFilters().SingleOrDefaultAsync(cancellationToken)
            : await db.Appointments.FromSqlInterpolated($"SELECT *, xmin FROM \"Appointments\" WHERE \"Id\" = {appointmentId} FOR UPDATE").IgnoreQueryFilters().SingleOrDefaultAsync(cancellationToken);
        if (appointment is null || appointment.Status != AppointmentStatus.Confirmed || appointment.QueueTicketId is not null) return null;
        var settings = await LockSettingsAsync(appointment.OrganizationId, appointment.ServiceId, cancellationToken);
        if (settings is null || clock.UtcNow < appointment.ScheduledStart.AddMinutes(-settings.CheckInAdvanceMinutes) || clock.UtcNow > appointment.ScheduledStart.AddMinutes(settings.LateToleranceMinutes)) return null;
        var queue = await db.Queues.FromSqlInterpolated($"SELECT * FROM \"Queues\" WHERE \"OrganizationId\" = {appointment.OrganizationId} AND \"BranchId\" = {appointment.BranchId} AND \"ServiceId\" = {appointment.ServiceId} AND \"IsActive\" = TRUE AND \"Status\" = {(int)QueueStatus.Open} ORDER BY \"CreatedAt\" FOR UPDATE LIMIT 1").IgnoreQueryFilters().SingleOrDefaultAsync(cancellationToken);
        if (queue is null) return null;
        if (queue.Capacity is not null && await db.QueueTickets.IgnoreQueryFilters().CountAsync(x => x.OrganizationId == appointment.OrganizationId && x.QueueId == queue.Id && x.Status == TicketStatus.Waiting, cancellationToken) >= queue.Capacity) return null;
        var service = await db.Services.IgnoreQueryFilters().SingleAsync(x => x.OrganizationId == appointment.OrganizationId && x.Id == appointment.ServiceId, cancellationToken);
        var sequence = queue.ReserveSequence(); var ticket = new QueueTicket(Guid.NewGuid(), appointment.OrganizationId, appointment.BranchId, queue.Id, appointment.ServiceId, $"{service.Prefix}-{sequence:000}", sequence, TicketPriority.Normal, Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(), clock.UtcNow);
        db.QueueTickets.Add(ticket); db.TicketEvents.Add(new(Guid.NewGuid(), appointment.OrganizationId, ticket.Id, TicketStatus.Waiting, "Check-in de agendamento", clock.UtcNow));
        var previous = appointment.Status; appointment.CheckIn(ticket.Id, clock.UtcNow); AddHistory(appointment, previous, "Check-in realizado");
        AddRealtime(appointment, "appointment.checked-in");
        db.OutboxMessages.Add(new OutboxMessage(Guid.NewGuid(), appointment.OrganizationId, "ticket.realtime", JsonSerializer.Serialize(new { eventName = "ticket.issued", ticketToken = ticket.CustomerPublicToken, ticket.Id, ticket.QueueId, ticket.TicketNumber, status = ticket.Status.ToString() }), clock.UtcNow));
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return new(appointment, ticket);
    }

    private async Task<Appointment?> BookSlotAsync(Branch branch, Service service, ServiceSchedulingSettings settings, DateTimeOffset requestedStart, string name, string? phone, string? email, Guid? excludedAppointmentId, Guid? rescheduledFromId, AppointmentOperationDiagnostics diagnostics, CancellationToken cancellationToken)
    {
        diagnostics.Stage = "resolve_slot_time";
        TimeZoneInfo zone; try { zone = TimeZoneInfo.FindSystemTimeZoneById(branch.TimeZone); } catch (TimeZoneNotFoundException) { return null; } catch (InvalidTimeZoneException) { return null; }
        var localStart = TimeZoneInfo.ConvertTime(requestedStart, zone); var requestedDate = DateOnly.FromDateTime(localStart.DateTime); var dayStart = requestedStart.AddDays(-1); var dayEnd = requestedStart.AddDays(1);
        diagnostics.Stage = "load_schedules";
        var schedules = await db.ServiceSchedules.IgnoreQueryFilters().AsNoTracking().Where(x => x.OrganizationId == branch.OrganizationId && x.BranchId == branch.Id && x.ServiceId == service.Id && x.IsActive && x.DayOfWeek == requestedDate.DayOfWeek).Select(x => new { x.StartTime, x.EndTime }).ToListAsync(cancellationToken);
        diagnostics.Stage = "load_blocks";
        var blocks = await db.ScheduleBlocks.IgnoreQueryFilters().AsNoTracking().Where(x => x.OrganizationId == branch.OrganizationId && x.BranchId == branch.Id && (x.ServiceId == null || x.ServiceId == service.Id) && x.StartAt < dayEnd && x.EndAt > dayStart).Select(x => new SlotWindow(x.StartAt, x.EndAt)).ToListAsync(cancellationToken);
        var statuses = new[] { AppointmentStatus.Scheduled, AppointmentStatus.Confirmed, AppointmentStatus.CheckedIn };
        diagnostics.Stage = "load_reservations";
        var reservations = await db.Appointments.IgnoreQueryFilters().AsNoTracking().Where(x => x.OrganizationId == branch.OrganizationId && x.BranchId == branch.Id && x.ServiceId == service.Id && x.Id != excludedAppointmentId && statuses.Contains(x.Status) && x.ScheduledStart < dayEnd && x.ScheduledEnd > dayStart).Select(x => new SlotWindow(x.ScheduledStart, x.ScheduledEnd)).ToListAsync(cancellationToken);
        diagnostics.Stage = "generate_slot";
        var slot = AppointmentSlotGenerator.Generate(requestedDate, zone, schedules.Select(x => (x.StartTime, x.EndTime)).ToArray(), settings.SlotDurationMinutes, settings.CapacityPerSlot, clock.UtcNow.AddMinutes(settings.MinimumAdvanceMinutes), clock.UtcNow.AddDays(settings.MaximumAdvanceDays), blocks, reservations).SingleOrDefault(x => x.StartAt == requestedStart.ToUniversalTime());
        if (slot is null) return null;
        diagnostics.Stage = "construct_appointment";
        var appointment = new Appointment(Guid.NewGuid(), branch.OrganizationId, branch.Id, service.Id, name, phone, email, slot.StartAt, slot.EndAt, branch.TimeZone, settings.RequireConfirmation, clock.UtcNow, rescheduledFromAppointmentId: rescheduledFromId); db.Appointments.Add(appointment); return appointment;
    }

    private Task<ServiceSchedulingSettings?> LockSettingsAsync(Guid organizationId, Guid serviceId, CancellationToken cancellationToken) => db.ServiceSchedulingSettings.FromSqlInterpolated($"SELECT * FROM \"ServiceSchedulingSettings\" WHERE \"OrganizationId\" = {organizationId} AND \"ServiceId\" = {serviceId} FOR UPDATE").IgnoreQueryFilters().SingleOrDefaultAsync(cancellationToken);
    private void AddHistory(Appointment appointment, AppointmentStatus previous, string reason) => db.AppointmentStatusHistory.Add(new(Guid.NewGuid(), appointment.OrganizationId, appointment.Id, previous, appointment.Status, null, reason, clock.UtcNow));
    private void AddRealtime(Appointment appointment, string eventName) => db.OutboxMessages.Add(new OutboxMessage(Guid.NewGuid(), appointment.OrganizationId, "appointment.realtime", JsonSerializer.Serialize(new { eventName, appointmentToken = appointment.PublicToken, appointmentId = appointment.Id, appointment.ServiceId, status = appointment.Status.ToString(), appointment.ScheduledStart }), clock.UtcNow));
    private static bool ValidPublicId(string value) => value.Length == 32 && value.All(Uri.IsHexDigit);
}
