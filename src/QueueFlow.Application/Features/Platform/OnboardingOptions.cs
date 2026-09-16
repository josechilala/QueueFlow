namespace QueueFlow.Application.Features.Platform;

public sealed class OnboardingOptions
{
    public int InvitationExpirationHours { get; init; } = 24;
    public int VerificationCodeExpirationMinutes { get; init; } = 10;
    public int VerificationCodeCooldownSeconds { get; init; } = 60;
    public int VerificationCodeMaxAttempts { get; init; } = 5;
    public string? VerificationCodePepper { get; init; }
    public bool AllowLocalPublicUrl { get; set; }
    public string? PublicUrl { get; init; }
    public bool ExposeVerificationCodeForDevelopment { get; set; }
}
