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
        var acquired = false;
        var finished = false;
        try
        {
            while (true)
            {
                var admission = await store.BeginLoginAsync(key, owner, options.FailureLimit, options.FailureWindow, options.Lease, ct);
                if (admission.Admission == LoginAdmission.Blocked)
                    return new(Result.Failure<TokenPair>(new("auth.login_limited", "Too many failed authentication attempts. Try again later.")), admission.RetryAfter);
                if (admission.Admission == LoginAdmission.Allowed) { acquired = true; break; }
                if (Stopwatch.GetElapsedTime(started) >= options.QueueWait) return Unavailable();
                await Task.Delay(TimeSpan.FromMilliseconds(50), ct);
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

    private static ProtectedLoginResult Unavailable() => new(
        Result.Failure<TokenPair>(new("auth.temporarily_unavailable", "Authentication is temporarily unavailable.")), TimeSpan.FromSeconds(5));
}
