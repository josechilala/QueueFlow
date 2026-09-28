using QueueFlow.Application.Abstractions.Authentication;
using StackExchange.Redis;

namespace QueueFlow.Infrastructure.Authentication;

internal sealed class RedisAuthenticationThrottleStore(IConnectionMultiplexer redis) : IAuthenticationThrottleStore
{
    private const string Begin = """
        local t = redis.call('TIME')
        local now = t[1] * 1000 + math.floor(t[2] / 1000)
        local deadline = tonumber(redis.call('HGET', KEYS[1], 'until') or '0')
        local failures = tonumber(redis.call('HGET', KEYS[1], 'failures') or '0')
        if deadline <= now then failures = 0; redis.call('HDEL', KEYS[1], 'failures', 'until') end
        if failures >= tonumber(ARGV[2]) then return {2, deadline - now} end
        local lease = tonumber(redis.call('HGET', KEYS[1], 'leaseUntil') or '0')
        if lease > now then return {1, lease - now} end
        redis.call('HSET', KEYS[1], 'owner', ARGV[1], 'leaseUntil', now + tonumber(ARGV[3]))
        redis.call('PEXPIRE', KEYS[1], math.max(tonumber(ARGV[3]), deadline - now))
        return {0, 0}
        """;
    private const string Finish = """
        local t = redis.call('TIME')
        local now = t[1] * 1000 + math.floor(t[2] / 1000)
        if redis.call('HGET', KEYS[1], 'owner') ~= ARGV[1] then return 0 end
        if tonumber(redis.call('HGET', KEYS[1], 'leaseUntil') or '0') <= now then return 0 end
        local deadline = tonumber(redis.call('HGET', KEYS[1], 'until') or '0')
        local failures = tonumber(redis.call('HGET', KEYS[1], 'failures') or '0')
        if deadline <= now then failures = 0 end
        if ARGV[2] == 'Succeeded' then failures = 0 end
        if ARGV[2] == 'Failed' then
            if failures == 0 then deadline = now + tonumber(ARGV[3]) end
            failures = failures + 1
        end
        if failures == 0 then redis.call('DEL', KEYS[1])
        else
            redis.call('HDEL', KEYS[1], 'owner', 'leaseUntil')
            redis.call('HSET', KEYS[1], 'failures', failures, 'until', deadline)
            redis.call('PEXPIRE', KEYS[1], math.max(1, deadline - now))
        end
        return 1
        """;
    private const string Refresh = """
        local count = tonumber(redis.call('GET', KEYS[1]) or '0')
        if count >= tonumber(ARGV[1]) then return math.max(1, redis.call('PTTL', KEYS[1])) end
        if redis.call('INCR', KEYS[1]) == 1 then redis.call('PEXPIRE', KEYS[1], ARGV[2]) end
        return 0
        """;

    public async Task<LoginAdmissionResult> BeginLoginAsync(string key, string owner, int failureLimit, TimeSpan window, TimeSpan lease, CancellationToken ct)
    {
        var result = (RedisResult[])(await EvaluateAsync(Begin, "login:" + key, [owner, failureLimit, (long)lease.TotalMilliseconds], ct))!;
        return new((LoginAdmission)(int)result[0], TimeSpan.FromMilliseconds((long)result[1]));
    }
    public async Task<bool> FinishLoginAsync(string key, string owner, LoginAttemptOutcome outcome, TimeSpan window, CancellationToken ct) =>
        (int)await EvaluateAsync(Finish, "login:" + key, [owner, outcome.ToString(), (long)window.TotalMilliseconds], ct) == 1;
    public async Task<TimeSpan> AcquireRefreshAsync(string key, int limit, TimeSpan window, CancellationToken ct) =>
        TimeSpan.FromMilliseconds((long)await EvaluateAsync(Refresh, "refresh:" + key, [limit, (long)window.TotalMilliseconds], ct));

    private async Task<RedisResult> EvaluateAsync(string script, string key, RedisValue[] values, CancellationToken ct)
    {
        try { return await redis.GetDatabase().ScriptEvaluateAsync(script, ["queueflow:authentication:v1:" + key], values).WaitAsync(ct); }
        catch (RedisException) { throw new AuthenticationStateUnavailableException(); }
    }
}
