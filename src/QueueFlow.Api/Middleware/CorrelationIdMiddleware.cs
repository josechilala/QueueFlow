namespace QueueFlow.Api.Middleware;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue("X-Correlation-ID", out var supplied) && !string.IsNullOrWhiteSpace(supplied) ? supplied.ToString() : Guid.NewGuid().ToString("N");
        context.TraceIdentifier = correlationId; context.Response.Headers["X-Correlation-ID"] = correlationId; await next(context);
    }
}
