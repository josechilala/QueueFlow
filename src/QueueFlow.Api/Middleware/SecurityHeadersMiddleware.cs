namespace QueueFlow.Api.Middleware;

public sealed class SecurityHeadersMiddleware(RequestDelegate next, IWebHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        if (!environment.IsDevelopment()) context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
        if (context.Request.Path.StartsWithSegments("/api/v1/auth") || context.Request.Path.StartsWithSegments("/api/v1/public/tickets") || context.Request.Path.StartsWithSegments("/api/v1/public/appointments")) context.Response.Headers.CacheControl = "no-store";
        await next(context);
    }
}
