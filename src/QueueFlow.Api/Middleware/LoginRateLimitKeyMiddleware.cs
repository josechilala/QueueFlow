using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Controllers;
using QueueFlow.Api.Controllers;

namespace QueueFlow.Api.Middleware;

public sealed class LoginRateLimitKeyMiddleware(RequestDelegate next)
{
    private const int MaxBodyBytes = 16 * 1024;
    private static readonly object EmailHashKey = new();
    private static readonly object RefreshHashKey = new();

    public static string RefreshPartitionKey(HttpContext context) => context.Items.TryGetValue(RefreshHashKey, out var hash)
        ? $"refresh:{hash}" : $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

    public static string PartitionKey(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return context.Items.TryGetValue(EmailHashKey, out var hash) ? $"login:{ip}:{hash}" : $"ip:{ip}";
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var action = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        if (HttpMethods.IsPost(context.Request.Method) && action?.ActionName is "Login" or "Refresh" &&
            (action.ControllerTypeInfo.AsType() == typeof(AuthController) || action.ControllerTypeInfo.AsType() == typeof(PlatformAuthController)) &&
            context.Request.HasJsonContentType())
        {
            // Read only a bounded login body, then rewind for MVC. Never log credentials.
            context.Request.EnableBuffering(bufferThreshold: MaxBodyBytes + 1);
            var bytes = new byte[MaxBodyBytes + 1];
            var count = 0;
            try
            {
                while (count < bytes.Length)
                {
                    var read = await context.Request.Body.ReadAsync(bytes.AsMemory(count), context.RequestAborted);
                    if (read == 0) break;
                    count += read;
                }
                if (count > MaxBodyBytes)
                {
                    context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                    return;
                }
                var charset = context.Request.GetTypedHeaders().ContentType?.Charset.Value?.Trim('"');
                var encoding = string.Equals(charset, "utf-16", StringComparison.OrdinalIgnoreCase) ? Encoding.Unicode : Encoding.UTF8;
                using var reader = new StreamReader(new MemoryStream(bytes, 0, count), encoding, detectEncodingFromByteOrderMarks: true);
                using var document = JsonDocument.Parse(await reader.ReadToEndAsync(context.RequestAborted));
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    // Match MVC's case-insensitive, last-property-wins JSON binding.
                    var isRefresh = action.ActionName == "Refresh";
                    JsonElement email = default;
                    foreach (var property in document.RootElement.EnumerateObject())
                        if (property.Name.Equals(isRefresh ? "refreshToken" : "email", StringComparison.OrdinalIgnoreCase)) email = property.Value;
                    if (email.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(email.GetString()))
                    {
                        var normalized = isRefresh ? email.GetString()! : email.GetString()!.Trim().ToLowerInvariant();
                        context.Items[isRefresh ? RefreshHashKey : EmailHashKey] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
                    }
                }
            }
            catch (JsonException)
            {
                // Malformed requests retain the IP-only quota; MVC returns the validation error.
            }
            finally
            {
                context.Request.Body.Position = 0;
            }
        }
        await next(context);
    }
}
