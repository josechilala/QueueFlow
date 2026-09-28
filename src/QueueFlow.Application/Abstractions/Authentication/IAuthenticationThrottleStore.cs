namespace QueueFlow.Application.Abstractions.Authentication;

public enum LoginAdmission { Allowed, Busy, Blocked }
public enum LoginAttemptOutcome { Ignored, Failed, Succeeded }
public sealed record LoginAdmissionResult(LoginAdmission Admission, TimeSpan RetryAfter);

public interface IAuthenticationThrottleStore
{
    Task<LoginAdmissionResult> BeginLoginAsync(string key, string owner, int failureLimit, TimeSpan window, TimeSpan lease, CancellationToken ct);
    Task<bool> FinishLoginAsync(string key, string owner, LoginAttemptOutcome outcome, TimeSpan window, CancellationToken ct);
    Task<TimeSpan> AcquireRefreshAsync(string key, int limit, TimeSpan window, CancellationToken ct);
}

public sealed class AuthenticationStateUnavailableException : Exception
{
    public AuthenticationStateUnavailableException() : base("Authentication state is temporarily unavailable.") { }
}
