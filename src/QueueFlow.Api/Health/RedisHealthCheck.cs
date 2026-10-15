using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace QueueFlow.Api.Health;

public sealed partial class RedisHealthCheck(IConnectionMultiplexer? redis = null, ILogger<RedisHealthCheck>? logger = null) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (redis is null) return HealthCheckResult.Healthy("Redis is not configured for this environment.");
            // StackExchange.Redis does not accept a CancellationToken for PingAsync.
            await redis.GetDatabase().PingAsync().WaitAsync(budget.Token);
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (logger is not null) LogCheckCanceled(logger);
            throw;
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested)
        {
            if (logger is not null) LogCheckTimeout(logger);
            return HealthCheckResult.Unhealthy("Redis readiness check timed out.");
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            // Do not surface Redis exception text: connection details can contain credentials.
            if (logger is not null) LogUnavailable(logger, exception.GetType().Name);
            return HealthCheckResult.Unhealthy("Redis is unavailable.");
        }
    }
    [LoggerMessage(LogLevel.Error, "Redis readiness dependency unavailable. ExceptionType: {ExceptionType}")]
    private static partial void LogUnavailable(ILogger logger, string exceptionType);
    [LoggerMessage(LogLevel.Warning, "Redis readiness internal timeout exceeded.")]
    private static partial void LogCheckTimeout(ILogger logger);
    [LoggerMessage(LogLevel.Information, "Redis readiness canceled by caller.")]
    private static partial void LogCheckCanceled(ILogger logger);
}
