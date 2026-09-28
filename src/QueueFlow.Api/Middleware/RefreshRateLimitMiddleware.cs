using System.Globalization;
using QueueFlow.Application.Abstractions.Authentication;

namespace QueueFlow.Api.Middleware;

public sealed class RefreshRateLimitMiddleware(RequestDelegate next, IConfiguration configuration)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!LoginRateLimitKeyMiddleware.IsRefresh(context)) { await next(context); return; }
        var store = context.RequestServices.GetRequiredService<IAuthenticationThrottleStore>();
        var key = LoginRateLimitKeyMiddleware.RefreshPartitionKey(context);
        try
        {
            var delay = await store.AcquireRefreshAsync(key, configuration.GetValue("RateLimiting:RefreshPermitLimit", 30), TimeSpan.FromMinutes(1), context.RequestAborted);
            if (delay > TimeSpan.Zero)
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(delay.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                RateLimitDiagnostics.Partition(context, "refresh", key);
                RateLimitDiagnostics.Rejected(context);
                return;
            }
        }
        catch (AuthenticationStateUnavailableException)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers.RetryAfter = "5";
            return;
        }
        await next(context);
    }
}
