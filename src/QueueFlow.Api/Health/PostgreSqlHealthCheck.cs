using System.Data.Common;
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
        string? connectionString = null;
        try
        {
            connectionString = dbContext.Database.GetConnectionString();
            await dbContext.Database.OpenConnectionAsync(cancellationToken);
            await dbContext.Database.CloseConnectionAsync();
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
        {
            for (Exception? failure = exception; failure is not null; failure = failure.InnerException)
            {
                LogConnectionFailure(
                    logger,
                    failure.GetType().FullName ?? failure.GetType().Name,
                    RedactMessage(failure.Message, connectionString),
                    failure != exception);
            }

            // Do not attach the original exception: health check infrastructure may log it without redaction.
            return HealthCheckResult.Unhealthy("PostgreSQL health check failed. See sanitized logs for details.");
        }
    }

    [LoggerMessage(Level = LogLevel.Error,
        Message = "PostgreSQL health check failed. ExceptionType: {ExceptionType}; Message: {ErrorMessage}; IsInnerException: {IsInnerException}")]
    private static partial void LogConnectionFailure(
        ILogger logger, string exceptionType, string errorMessage, bool isInnerException);

    private static string RedactMessage(string message, string? connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
            return "Connection configuration is unavailable; exception message omitted.";

        try
        {
            var settings = new DbConnectionStringBuilder { ConnectionString = connectionString };
            var sanitized = message.Replace(connectionString, "[REDACTED]", StringComparison.OrdinalIgnoreCase);

            foreach (var value in settings.Values.Cast<object>()
                .Select(value => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture))
                .Where(value => !string.IsNullOrEmpty(value))
                .OrderByDescending(value => value!.Length))
            {
                sanitized = sanitized.Replace(value!, "[REDACTED]", StringComparison.OrdinalIgnoreCase);
            }

            return sanitized;
        }
        catch (ArgumentException)
        {
            return "Invalid connection configuration; exception message omitted to protect secrets.";
        }
    }
}
