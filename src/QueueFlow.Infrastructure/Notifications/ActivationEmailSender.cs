using Microsoft.Extensions.Hosting;
using QueueFlow.Application.Abstractions.Notifications;

namespace QueueFlow.Infrastructure.Notifications;

internal sealed class ActivationEmailSender(IHostEnvironment environment, ResendEmailTransport transport) : IActivationEmailSender
{
    private bool Simulated => environment.IsDevelopment() || environment.IsEnvironment("Test");
    private bool HasResendConfiguration => transport.IsConfigured;

    public bool IsConfigured => Simulated || HasResendConfiguration;
    public bool SupportsInvitationDelivery => !Simulated && HasResendConfiguration;

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

    private Task SendAsync(string email, string subject, string text, CancellationToken cancellationToken) =>
        transport.SendAsync(new ResendEmail(transport.From, [email], subject, text), null, cancellationToken);
}
