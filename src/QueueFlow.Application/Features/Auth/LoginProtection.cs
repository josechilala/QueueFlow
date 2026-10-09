using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Common;
using QueueFlow.Domain.Enums;

namespace QueueFlow.Application.Features.Auth;

public sealed class LoginProtectionOptions
{
    public int FailureLimit { get; init; } = 10;
    public TimeSpan FailureWindow { get; init; } = TimeSpan.FromMinutes(1);
    public TimeSpan Lease { get; init; } = TimeSpan.FromSeconds(90);
    public TimeSpan QueueWait { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan BlockedAttemptCooldown { get; init; } = TimeSpan.FromSeconds(5);
}

public sealed record ProtectedLoginResult(Result<TokenPair> Result, TimeSpan? RetryAfter = null);

public sealed class LoginProtection(IAuthenticationThrottleStore store, LoginProtectionOptions options)
{
    public static string PartitionKey(IdentityType identity, string email) =>
        $"{identity}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant())))}";

    public async Task<ProtectedLoginResult> ExecuteAsync(IdentityType identity, string email,
        Func<CancellationToken, Task<Result<TokenPair>>> authenticate, CancellationToken ct)
    {
        var key = PartitionKey(identity, email);
        var owner = Guid.NewGuid().ToString("N");
        var started = Stopwatch.GetTimestamp();
        long? lockoutStarted = null;
        var acquired = false;
        var finished = false;
        try
        {
            while (true)
            {
                var admission = await store.BeginLoginAsync(key, owner, options.FailureLimit, options.FailureWindow, options.Lease, ct);
                if (admission.Admission == LoginAdmission.Blocked)
                {
                    // A lockout must slow credential guessing without denying the
                    // account owner who supplies the correct password. The store
                    // grants a bounded verification lease without extending failures.
                    acquired = true;
                    var blockedAttempt = await authenticate(ct);
                    var blockedOutcome = blockedAttempt.IsSuccess ? LoginAttemptOutcome.Succeeded
                        : blockedAttempt.Error.Code is "auth.invalid_credentials" or "platform.invalid_credentials"
                            ? LoginAttemptOutcome.LockedOut : LoginAttemptOutcome.Ignored;
                    var finishedBlockedAttempt = await store.FinishLoginAsync(key, owner,
                        blockedOutcome, blockedOutcome == LoginAttemptOutcome.LockedOut ? options.BlockedAttemptCooldown : options.FailureWindow, ct);
                    finished = true;
                    if (!finishedBlockedAttempt) return Unavailable();
                    if (blockedAttempt.IsSuccess) return new(blockedAttempt);
                    if (blockedOutcome == LoginAttemptOutcome.LockedOut)
                        return LoginLimited(admission.RetryAfter);
                    return new(blockedAttempt);
                }
                if (admission.Admission == LoginAdmission.Allowed) { acquired = true; break; }
                var waitStarted = started;
                var queueWait = options.QueueWait;
                if (admission.LockoutActive)
                {
                    lockoutStarted ??= Stopwatch.GetTimestamp();
                    waitStarted = lockoutStarted.Value;
                    queueWait += options.BlockedAttemptCooldown;
                }
                if (Stopwatch.GetElapsedTime(waitStarted) >= queueWait) return Unavailable();
                var pollDelay = admission.RetryAfter > TimeSpan.Zero
                    ? TimeSpan.FromMilliseconds(Math.Min(50, admission.RetryAfter.TotalMilliseconds))
                    : TimeSpan.FromMilliseconds(50);
                await Task.Delay(pollDelay, ct);
            }
            var result = await authenticate(ct);
            var outcome = result.IsSuccess ? LoginAttemptOutcome.Succeeded
                : result.Error.Code is "auth.invalid_credentials" or "platform.invalid_credentials" ? LoginAttemptOutcome.Failed
                : LoginAttemptOutcome.Ignored;
            finished = await store.FinishLoginAsync(key, owner, outcome, options.FailureWindow, ct);
            return finished ? new(result) : Unavailable();
        }
        catch (AuthenticationStateUnavailableException) { return Unavailable(); }
        finally
        {
            if (acquired && !finished)
            {
                try { await store.FinishLoginAsync(key, owner, LoginAttemptOutcome.Ignored, options.FailureWindow, CancellationToken.None); }
                catch (AuthenticationStateUnavailableException) { /* The bounded lease expires without counting a failure. */ }
            }
        }
    }

    private static ProtectedLoginResult LoginLimited(TimeSpan retryAfter) => new(
        Result.Failure<TokenPair>(new("auth.login_limited", "Too many failed authentication attempts. Try again later.")), retryAfter);

    private static ProtectedLoginResult Unavailable() => new(
        Result.Failure<TokenPair>(new("auth.temporarily_unavailable", "Authentication is temporarily unavailable.")), TimeSpan.FromSeconds(5));
}
