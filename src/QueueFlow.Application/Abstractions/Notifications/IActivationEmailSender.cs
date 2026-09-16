namespace QueueFlow.Application.Abstractions.Notifications;

public interface IActivationEmailSender
{
    bool IsConfigured { get; }
    bool SupportsInvitationDelivery => false;
    Task SendVerificationCodeAsync(string email, string code, CancellationToken cancellationToken);
    Task SendInvitationAsync(string email, string activationUrl, CancellationToken cancellationToken) => throw new InvalidOperationException("Invitation delivery is not configured.");
}
