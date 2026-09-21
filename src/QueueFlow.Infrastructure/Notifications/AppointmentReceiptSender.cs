using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using QueueFlow.Application.Features.Appointments;
using QueueFlow.Domain.Entities;

namespace QueueFlow.Infrastructure.Notifications;

internal sealed class AppointmentEmailOptions
{
    public string CustomerPublicUrl { get; set; } = string.Empty;
}

internal sealed record PreparedAppointmentReceipt(ResendEmail Email, DateTimeOffset PreparedAt);

internal sealed class AppointmentReceiptSender(ResendEmailTransport transport, IOptions<AppointmentEmailOptions> options, IHostEnvironment environment)
{
    // Preparation is committed before any HTTP request, preserving the body and retry deadline across restarts.
    public bool Prepare(OutboxMessage message, DateTimeOffset now)
    {
        using var document = JsonDocument.Parse(message.Payload);
        if (document.RootElement.TryGetProperty("PreparedAt", out _)) return false;
        if (!transport.IsConfigured) throw new InvalidOperationException("Email provider is not configured.");
        var raw = options.Value.CustomerPublicUrl.Trim();
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var origin) || !string.IsNullOrEmpty(origin.UserInfo) ||
            !string.IsNullOrEmpty(origin.Query) || !string.IsNullOrEmpty(origin.Fragment) || origin.AbsolutePath != "/" ||
            (origin.Scheme != "https" && !(origin.Scheme == "http" && origin.IsLoopback && (environment.IsDevelopment() || environment.IsEnvironment("Test")))))
            throw new InvalidOperationException("Appointment customer URL is not configured correctly.");
        var receipt = JsonSerializer.Deserialize<AppointmentReceipt>(message.Payload)!;
        var local = TimeZoneInfo.ConvertTime(receipt.ScheduledStart, TimeZoneInfo.FindSystemTimeZoneById(receipt.TimeZone));
        var link = new Uri(origin, $"/meu-agendamento/{Uri.EscapeDataString(receipt.PublicToken)}").AbsoluteUri;
        var text = $"Comprovante de agendamento\n\nCliente: {receipt.CustomerName}\nEmpresa: {receipt.OrganizationName}\nUnidade: {receipt.BranchName}\nServiço: {receipt.ServiceName}\nData/hora: {local.ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("pt-BR"))} ({receipt.TimeZone})\n\nConsultar/gerenciar seu agendamento:\n{link}\n\nAo chegar à unidade, informe à atendente que possui um agendamento para realizar o check-in.\n\nEste link é pessoal. Não o compartilhe.";
        var email = new ResendEmail(transport.From, [receipt.Email], "QueueFlow — Comprovante de agendamento", text);
        message.PrepareDelivery(JsonSerializer.Serialize(new PreparedAppointmentReceipt(email, now)), now);
        return true;
    }

    public async Task DeliverAsync(OutboxMessage message, DateTimeOffset now, CancellationToken ct)
    {
        if (message.ProcessedAt is not null) return;
        var prepared = JsonSerializer.Deserialize<PreparedAppointmentReceipt>(message.Payload)!;
        // Resend retains idempotency keys for 24h. Leave ambiguous deliveries visible for review, never resend outside that window.
        if (now >= prepared.PreparedAt.AddHours(23))
        {
            message.SuspendDelivery("Receipt delivery requires review: idempotency window expired.", now);
            return;
        }
        await transport.SendAsync(prepared.Email, $"appointment-receipt/{message.OrganizationId:N}/{message.Id:N}", ct);
        message.MarkProcessed(now);
    }
}
