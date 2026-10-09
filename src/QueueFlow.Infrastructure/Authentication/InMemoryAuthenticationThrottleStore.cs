using QueueFlow.Application.Abstractions.Authentication;

namespace QueueFlow.Infrastructure.Authentication;

// Development/Test only. Production must use shared Redis state.
internal sealed class InMemoryAuthenticationThrottleStore(TimeProvider time) : IAuthenticationThrottleStore
{
    private sealed class Entry
    {
        public int Failures;
        public DateTimeOffset Until;
        public string? Owner;
        public DateTimeOffset LeaseUntil;
    }
    private readonly Dictionary<string, Entry> entries = [];
    private readonly object gate = new();

    private Entry Get(string key, DateTimeOffset now)
    {
        if (entries.TryGetValue(key, out var entry)) return entry;
        if (entries.Count >= 10000)
        {
            foreach (var expired in entries.Where(x => x.Value.Until <= now && x.Value.LeaseUntil <= now).Select(x => x.Key).ToArray()) entries.Remove(expired);
            if (entries.Count >= 10000) throw new AuthenticationStateUnavailableException();
        }
        return entries[key] = new Entry();
    }

    public Task<LoginAdmissionResult> BeginLoginAsync(string key, string owner, int failureLimit, TimeSpan window, TimeSpan lease, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (gate)
        {
            var now = time.GetUtcNow();
            var entry = Get("login:" + key, now);
            if (entry.LeaseUntil > now) return Task.FromResult(new LoginAdmissionResult(LoginAdmission.Busy, entry.LeaseUntil - now, entry.Failures >= failureLimit));
            if (entry.Until <= now) entry.Failures = 0;
            if (entry.Failures >= failureLimit)
            {
                entry.Owner = owner; entry.LeaseUntil = now + lease;
                return Task.FromResult(new LoginAdmissionResult(LoginAdmission.Blocked, entry.Until - now, true));
            }
            entry.Owner = owner; entry.LeaseUntil = now + lease;
            return Task.FromResult(new LoginAdmissionResult(LoginAdmission.Allowed, TimeSpan.Zero));
        }
    }

    public Task<bool> FinishLoginAsync(string key, string owner, LoginAttemptOutcome outcome, TimeSpan window, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (gate)
        {
            var now = time.GetUtcNow(); key = "login:" + key;
            if (!entries.TryGetValue(key, out var entry) || entry.Owner != owner || entry.LeaseUntil <= now) return Task.FromResult(false);
            if (entry.Until <= now) entry.Failures = 0;
            if (outcome == LoginAttemptOutcome.Succeeded) entry.Failures = 0;
            if (outcome == LoginAttemptOutcome.Failed && entry.Failures++ == 0) entry.Until = now + window;
            entry.Owner = null;
            entry.LeaseUntil = outcome == LoginAttemptOutcome.LockedOut && entry.Failures > 0 && entry.Until > now ? now + window : default;
            if (entry.Failures == 0) entries.Remove(key);
            return Task.FromResult(true);
        }
    }

    public Task<TimeSpan> AcquireRefreshAsync(string key, int limit, TimeSpan window, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (gate)
        {
            var now = time.GetUtcNow(); var entry = Get("refresh:" + key, now);
            if (entry.Until <= now) { entry.Failures = 0; entry.Until = now + window; }
            if (entry.Failures >= limit) return Task.FromResult(entry.Until - now);
            entry.Failures++;
            return Task.FromResult(TimeSpan.Zero);
        }
    }
}
