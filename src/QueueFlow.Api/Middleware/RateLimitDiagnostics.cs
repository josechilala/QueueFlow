using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Routing;

namespace QueueFlow.Api.Middleware;

public static partial class RateLimitDiagnostics
{
    private static readonly object PolicyKey = new();
    private static readonly object PartitionKey = new();
    private static readonly object PeerKey = new();
    private static readonly object ForwardedKey = new();
    private static readonly byte[] DiagnosticKey = RandomNumberGenerator.GetBytes(32);
    private static readonly string Instance = Guid.NewGuid().ToString("N");

    // Capture before ForwardedHeaders consumes trusted hops. Never retain raw headers.
    public static void CaptureForwarding(HttpContext context)
    {
        context.Items[PeerKey] = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var raw = context.Request.Headers["X-Forwarded-For"].ToString();
        context.Items[ForwardedKey] = raw.Length > 2048 ? "oversized" : string.Join(",", raw.Split(',').TakeLast(32)
            .Select(value => IPAddress.TryParse(value.Trim(), out var ip) ? ip.ToString() : "invalid"));
    }

    // The endpoint limiter is acquired only after the global limiter succeeds.
    public static string Partition(HttpContext context, string policy, string key)
    {
        context.Items[PolicyKey] = policy;
        context.Items[PartitionKey] = key;
        return key;
    }

    public static void Rejected(HttpContext context)
    {
        var policy = context.Items[PolicyKey]?.ToString() ?? "unknown";
        context.Response.Headers["X-RateLimit-Policy"] = policy;
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("QueueFlow.RateLimiting");
        var key = context.Items[PartitionKey]?.ToString() ?? "unknown";
        // Process-local HMAC allows bucket correlation without exposing account/token hashes.
        var partition = Convert.ToHexString(HMACSHA256.HashData(DiagnosticKey, Encoding.UTF8.GetBytes(key)));
        var endpoint = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched";
        LogRejected(logger, policy, endpoint, partition, Instance,
            context.Response.Headers.RetryAfter.ToString(), context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            context.Items[PeerKey]?.ToString() ?? "unknown", context.Items[ForwardedKey]?.ToString() ?? "missing", context.TraceIdentifier);
    }

    [LoggerMessage(LogLevel.Warning, "Rate limit rejected request. Policy: {Policy}; Endpoint: {Endpoint}; Partition: {Partition}; Instance: {Instance}; RetryAfter: {RetryAfter}; ResolvedIp: {ResolvedIp}; PeerIp: {PeerIp}; ForwardedFor: {ForwardedFor}; CorrelationId: {CorrelationId}")]
    private static partial void LogRejected(ILogger logger, string policy, string endpoint, string partition, string instance,
        string retryAfter, string resolvedIp, string peerIp, string forwardedFor, string correlationId);
}
