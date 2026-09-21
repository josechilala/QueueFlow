using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace QueueFlow.Infrastructure.Notifications;

internal sealed record ResendEmail(string From, string[] To, string Subject, string Text);

internal sealed class ResendEmailTransport(IOptions<ResendOptions> options, IHttpClientFactory clients)
{
    internal const string HttpClientName = "ResendActivation";
    private readonly string apiKey = options.Value.ApiKey?.Trim() ?? string.Empty;
    public string From => options.Value.From;
    public bool IsConfigured => apiKey.Length > 0 && !apiKey.Any(char.IsWhiteSpace) &&
        !string.IsNullOrWhiteSpace(From) && !From.Any(char.IsControl) && MailAddress.TryCreate(From, out _);

    public async Task SendAsync(ResendEmail email, string? idempotencyKey, CancellationToken ct)
    {
        if (!IsConfigured) throw new InvalidOperationException("Email provider is not configured.");
        using var client = clients.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        request.Content = JsonContent.Create(new { from = email.From, to = email.To, subject = email.Subject, text = email.Text });
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            // Never expose provider bodies, recipients, tokens or credentials in logs/errors.
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Email delivery failed.");
        }
        catch (HttpRequestException) { throw new InvalidOperationException("Email delivery failed."); }
    }
}
