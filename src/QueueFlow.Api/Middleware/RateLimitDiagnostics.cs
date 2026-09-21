namespace QueueFlow.Api.Middleware;

public static partial class RateLimitDiagnostics
{
    private static readonly object PolicyKey = new();

    // The endpoint limiter is acquired only after the global limiter succeeds.
    public static string Partition(HttpContext context, string policy, string key)
    {
        context.Items[PolicyKey] = policy;
        return key;
    }

    public static void Rejected(HttpContext context)
    {
        var policy = context.Items[PolicyKey]?.ToString() ?? "unknown";
        context.Response.Headers["X-RateLimit-Policy"] = policy;
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("QueueFlow.RateLimiting");
        LogRejected(logger, policy, context.TraceIdentifier);
    }

    [LoggerMessage(LogLevel.Warning, "Rate limit rejected request. Policy: {Policy}; CorrelationId: {CorrelationId}")]
    private static partial void LogRejected(ILogger logger, string policy, string correlationId);
}
