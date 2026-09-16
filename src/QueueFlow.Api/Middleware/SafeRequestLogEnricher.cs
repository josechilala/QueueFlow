using Serilog.Core;
using Serilog.Events;

namespace QueueFlow.Api.Middleware;

// Framework scopes can contain raw paths even when the request completion event uses a route template.
public sealed class SafeRequestLogEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var name in logEvent.Properties.Keys.ToArray())
        {
            if (name.Contains("token", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("authorization", StringComparison.OrdinalIgnoreCase) ||
                (name.Equals("VerificationCode", StringComparison.OrdinalIgnoreCase) || name.Equals("Code", StringComparison.OrdinalIgnoreCase)) ||
                name.Contains("key", StringComparison.OrdinalIgnoreCase) ||
                name is "RequestPath" or "QueryString" or "Path" or "Uri" or "Url")
                logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty(name, "[REDACTED]"));
        }
    }
}
