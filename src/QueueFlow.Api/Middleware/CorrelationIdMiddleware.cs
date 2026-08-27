namespace QueueFlow.Api.Middleware;

using Serilog.Context;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var candidate = context.Request.Headers.TryGetValue("X-Correlation-ID", out var supplied) ? supplied.ToString() : string.Empty;
        var correlationId = candidate.Length is > 0 and <= 64 && candidate.All(value => char.IsLetterOrDigit(value) || value is '-' or '_') ? candidate : Guid.NewGuid().ToString("N");
        context.TraceIdentifier = correlationId;
        context.Response.Headers["X-Correlation-ID"] = correlationId;
        using (LogContext.PushProperty("CorrelationId", correlationId)) await next(context);
    }
}
