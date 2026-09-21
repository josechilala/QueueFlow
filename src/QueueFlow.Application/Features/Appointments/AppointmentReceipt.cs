using System.Net.Mail;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QueueFlow.Application.Abstractions.Persistence;
using QueueFlow.Domain.Entities;
using QueueFlow.Domain.Enums;

namespace QueueFlow.Application.Features.Appointments;

public sealed record AppointmentReceipt(Guid AppointmentId, string Email, string CustomerName, string OrganizationName,
    string BranchName, string ServiceName, DateTimeOffset ScheduledStart, string TimeZone, string PublicToken)
{
    public const string OutboxType = "appointment.receipt";

    public static bool IsValidEmail(string? email)
    {
        var value = email?.Trim();
        return !string.IsNullOrWhiteSpace(value) && value.Length <= 320 &&
            !value.Any(char.IsWhiteSpace) && !value.Any(char.IsControl) &&
            MailAddress.TryCreate(value, out var address) && address.Address == value &&
            address.Host.Contains('.') && !address.Host.StartsWith('.') && !address.Host.EndsWith('.');
    }

    public static async Task EnqueueAsync(IApplicationDbContext db, Appointment appointment, DateTimeOffset now, CancellationToken ct)
    {
        // Legacy appointments without an address cannot receive a receipt. Never change their status.
        if (appointment.Status != AppointmentStatus.Confirmed || appointment.CreatedByUserId is not null || !IsValidEmail(appointment.CustomerEmail)) return;
        if (await db.OutboxMessages.IgnoreQueryFilters().AnyAsync(x => x.Id == appointment.Id, ct)) return;
        var tenant = appointment.OrganizationId;
        var organization = await db.Organizations.AsNoTracking().Where(x => x.Id == tenant).Select(x => x.Name).SingleAsync(ct);
        var branch = await db.Branches.IgnoreQueryFilters().AsNoTracking().Where(x => x.Id == appointment.BranchId && x.OrganizationId == tenant).Select(x => x.Name).SingleAsync(ct);
        var service = await db.Services.IgnoreQueryFilters().AsNoTracking().Where(x => x.Id == appointment.ServiceId && x.OrganizationId == tenant).Select(x => x.Name).SingleAsync(ct);
        var receipt = new AppointmentReceipt(appointment.Id, appointment.CustomerEmail!, appointment.CustomerName,
            organization, branch, service, appointment.ScheduledStart, appointment.TimeZone, appointment.PublicToken);
        // One durable message per appointment, committed atomically with the booking/confirmation.
        db.OutboxMessages.Add(new OutboxMessage(appointment.Id, tenant, OutboxType, JsonSerializer.Serialize(receipt), now));
    }
}
