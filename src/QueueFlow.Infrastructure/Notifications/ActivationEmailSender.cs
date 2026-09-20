using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QueueFlow.Application.Abstractions.Notifications;

namespace QueueFlow.Infrastructure.Notifications;

internal sealed partial class ActivationEmailSender(IHostEnvironment environment, IOptions<ResendOptions> options, IHttpClientFactory clients) : IActivationEmailSender
{
    internal const string HttpClientName = "ResendActivation";
    private bool Simulated => environment.IsDevelopment() || environment.IsEnvironment("Test");
    private bool ApiKeyPresent => !string.IsNullOrWhiteSpace(options.Value.ApiKey);
    private bool ApiKeyValidFormat => ApiKeyPresent && !options.Value.ApiKey.Any(char.IsWhiteSpace);
    private bool FromPresent => !string.IsNullOrWhiteSpace(options.Value.From);
    private bool FromValidFormat => FromPresent && !options.Value.From.Any(char.IsControl) && MailAddress.TryCreate(options.Value.From, out _);
    private bool HasResendConfiguration => ApiKeyValidFormat && FromValidFormat;

    public bool IsConfigured => Simulated || HasResendConfiguration;
    public bool SupportsInvitationDelivery => !Simulated && HasResendConfiguration;

    // Temporary startup diagnostic. Pass only booleans, never options or provider values.
    internal void LogConfiguration(ILogger<ActivationEmailSender> logger) =>
        LogConfigurationFlags(logger, ApiKeyPresent, ApiKeyValidFormat, FromPresent, FromValidFormat, IsConfigured);

    [LoggerMessage(Level = LogLevel.Information, Message = "ApiKeyPresent={ApiKeyPresent} ApiKeyValidFormat={ApiKeyValidFormat} FromPresent={FromPresent} FromValidFormat={FromValidFormat} IsConfigured={IsConfigured}")]
    private static partial void LogConfigurationFlags(ILogger logger, bool apiKeyPresent, bool apiKeyValidFormat, bool fromPresent, bool fromValidFormat, bool isConfigured);

    public Task SendVerificationCodeAsync(string email, string code, CancellationToken cancellationToken)
    {
        if (!IsConfigured) throw new InvalidOperationException("Activation email provider is not configured.");
        if (Simulated) return Task.CompletedTask;
        return SendAsync(email, "QueueFlow — Código de verificação",
            $"Seu código de verificação do QueueFlow é: {code}\n\nSe você não solicitou este código, ignore este e-mail.", cancellationToken);
    }

    public Task SendInvitationAsync(string email, string activationUrl, CancellationToken cancellationToken)
    {
        if (!SupportsInvitationDelivery) throw new InvalidOperationException("Invitation delivery is not configured.");
        return SendAsync(email, "QueueFlow — Convite de ativação",
            $"Você recebeu um convite para o QueueFlow. Acesse o link para ativar sua conta:\n\n{activationUrl}\n\nSe você não esperava este convite, ignore este e-mail.", cancellationToken);
    }

    private async Task SendAsync(string email, string subject, string text, CancellationToken cancellationToken)
    {
        using var client = clients.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.ApiKey);
        request.Content = JsonContent.Create(new { from = options.Value.From, to = new[] { email }, subject, text });
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            // Provider bodies can echo recipients or secrets. Never log or propagate them.
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Activation email delivery failed.");
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("Activation email delivery failed.");
        }
    }
}
