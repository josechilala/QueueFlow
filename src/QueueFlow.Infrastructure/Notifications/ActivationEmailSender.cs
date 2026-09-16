using Microsoft.Extensions.Hosting;
using QueueFlow.Application.Abstractions.Notifications;

namespace QueueFlow.Infrastructure.Notifications;

internal sealed class ActivationEmailSender(IHostEnvironment environment) : IActivationEmailSender
{
    public bool IsConfigured => environment.IsDevelopment() || environment.IsEnvironment("Test");
    public Task SendVerificationCodeAsync(string email, string code, CancellationToken cancellationToken)
    {
        if (!IsConfigured) throw new InvalidOperationException("Activation email provider is not configured.");
        return Task.CompletedTask;
    }
}
