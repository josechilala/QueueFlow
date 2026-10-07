using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace QueueFlow.Api.Health;

public sealed class RedisHealthCheck(IConnectionMultiplexer? redis = null) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (redis is null) return HealthCheckResult.Healthy("Redis is not configured for this environment.");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await redis.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy();
        }
        catch (RedisException)
        {
            // Do not surface Redis exception text: connection details can contain credentials.
            return HealthCheckResult.Unhealthy("Redis is unavailable.");
        }
    }
}
