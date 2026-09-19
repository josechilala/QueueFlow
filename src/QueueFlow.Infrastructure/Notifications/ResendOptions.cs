namespace QueueFlow.Infrastructure.Notifications;

internal sealed class ResendOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
}
