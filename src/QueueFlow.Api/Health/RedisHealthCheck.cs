using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace QueueFlow.Api.Health;

internal sealed class RedisHealthCheck(IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var value = configuration["Redis:ConnectionString"] ?? "localhost:6379"; var parts = value.Split(':', 2);
            using var client = new TcpClient(); await client.ConnectAsync(parts[0], parts.Length == 2 ? int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 6379, cancellationToken);
            await using var stream = client.GetStream(); await stream.WriteAsync(Encoding.ASCII.GetBytes("*1\r\n$4\r\nPING\r\n"), cancellationToken);
            var buffer = new byte[16]; var read = await stream.ReadAsync(buffer, cancellationToken); var response = Encoding.ASCII.GetString(buffer, 0, read);
            return response.StartsWith("+PONG", StringComparison.Ordinal) ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("Redis returned an unexpected response.");
        }
        catch (Exception exception) when (exception is SocketException or IOException or FormatException) { return HealthCheckResult.Unhealthy("Redis is unavailable.", exception); }
    }
}
