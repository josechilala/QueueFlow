using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Controllers;
using QueueFlow.Api.Controllers;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Domain.Enums;
using QueueFlow.Application.Features.Auth;

namespace QueueFlow.Api.Middleware;

public sealed class LoginRateLimitKeyMiddleware(RequestDelegate next)
{
    private const int MaxBodyBytes = 16 * 1024;
    private static readonly object EmailHashKey = new();
    private static readonly object LoginCredentialsKey = new();

    public static bool IsAuthenticationEndpoint(HttpContext context) => IsAction(context, "Login") || IsAction(context, "Refresh");
    public static bool IsRefresh(HttpContext context) => IsAction(context, "Refresh");
    public static bool HasLoginCredentials(HttpContext context) => context.Items.ContainsKey(LoginCredentialsKey);
    private static bool IsAction(HttpContext context, string name)
    {
        var action = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        return HttpMethods.IsPost(context.Request.Method) && action?.ActionName == name &&
            (action.ControllerTypeInfo.AsType() == typeof(AuthController) || action.ControllerTypeInfo.AsType() == typeof(PlatformAuthController));
    }
    private static readonly object RefreshIdentityKey = new();

    public static string GlobalPartitionKey(HttpContext context) =>
        context.Items.TryGetValue(RefreshIdentityKey, out var identity) ? $"refresh-user:{identity}" :
        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    public static string RefreshPartitionKey(HttpContext context) => context.Items.TryGetValue(RefreshIdentityKey, out var identity)
        ? $"refresh-user:{identity}" : $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

    public static string PartitionKey(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return context.Items.TryGetValue(EmailHashKey, out var hash) ? $"{hash}" : $"ip:{ip}";
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
                    JsonElement password = default;
                    foreach (var property in document.RootElement.EnumerateObject())
                    {
                        if (property.Name.Equals(isRefresh ? "refreshToken" : "email", StringComparison.OrdinalIgnoreCase)) email = property.Value;
                        if (property.Name.Equals("password", StringComparison.OrdinalIgnoreCase)) password = property.Value;
                    }
                    if (!isRefresh && email.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(email.GetString()) &&
                        password.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(password.GetString())) context.Items[LoginCredentialsKey] = true;
                    if (email.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(email.GetString()))
                    {
                        var normalized = isRefresh ? email.GetString()! : email.GetString()!.Trim().ToLowerInvariant();
                        var kind = action.ControllerTypeInfo.AsType() == typeof(PlatformAuthController) ? IdentityType.Platform : IdentityType.Tenant;
                        if (!isRefresh) context.Items[EmailHashKey] = LoginProtection.PartitionKey(kind, normalized);
                        if (isRefresh)
                        {
                            var userId = context.RequestServices?.GetService<ITokenService>()?.GetRefreshRateLimitIdentity(normalized, kind);
                            if (userId is not null) context.Items[RefreshIdentityKey] = $"{kind}:{userId:N}";
                        }
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
