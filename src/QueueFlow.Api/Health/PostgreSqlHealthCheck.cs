using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using QueueFlow.Infrastructure.Persistence;

namespace QueueFlow.Api.Health;

internal sealed partial class PostgreSqlHealthCheck(
    ApplicationDbContext dbContext,
    ILogger<PostgreSqlHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await dbContext.Database.OpenConnectionAsync(budget.Token);
            await dbContext.Database.CloseConnectionAsync();
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogCheckCanceled(logger);
            throw;
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested)
        {
            LogCheckTimeout(logger);
            return HealthCheckResult.Unhealthy("PostgreSQL readiness check timed out.");
        }
        catch (Exception exception)
        {
            // Never log provider messages or attach exceptions: both can contain credentials.
            LogConnectionFailure(logger, exception.GetType().Name);
            return HealthCheckResult.Unhealthy("PostgreSQL is unavailable.");
        }
    }

    [LoggerMessage(Level = LogLevel.Error,
        Message = "PostgreSQL readiness dependency unavailable. ExceptionType: {ExceptionType}")]
    private static partial void LogConnectionFailure(ILogger logger, string exceptionType);
    [LoggerMessage(LogLevel.Warning, "PostgreSQL readiness internal timeout exceeded.")]
    private static partial void LogCheckTimeout(ILogger logger);
    [LoggerMessage(LogLevel.Information, "PostgreSQL readiness canceled by caller.")]
    private static partial void LogCheckCanceled(ILogger logger);
}
