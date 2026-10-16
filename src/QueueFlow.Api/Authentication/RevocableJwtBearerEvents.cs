using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using QueueFlow.Application.Abstractions.Authentication;

namespace QueueFlow.Api.Authentication;

public sealed partial class RevocableJwtBearerEvents(IAccessTokenValidator validator, ILogger<RevocableJwtBearerEvents> logger) : JwtBearerEvents
{
    public override async Task TokenValidated(TokenValidatedContext context)
    {
        var principal = context.Principal!;
        string? SingleClaim(string type)
        {
            var claims = principal.FindAll(type).Take(2).ToArray();
            return claims.Length == 1 ? claims[0].Value : null;
        }
        var type = SingleClaim("identity_type");
        var role = SingleClaim(ClaimTypes.Role);
        // The existing issuer emits both sub and NameIdentifier, which inbound mapping merges.
        var subjects = principal.FindAll(ClaimTypes.NameIdentifier).Select(x => x.Value).Distinct(StringComparer.Ordinal).Take(2).ToArray();
        var organizations = principal.FindAll("organization_id").ToArray();
        Guid? organizationId = null;
        if (subjects.Length != 1 || !Guid.TryParse(subjects[0], out var userId) ||
            !long.TryParse(SingleClaim("access_version"), NumberStyles.None, CultureInfo.InvariantCulture, out var accessVersion) ||
            role is null || type is not ("tenant" or "platform") ||
            (type == "tenant" && (organizations.Length != 1 || !Guid.TryParse(organizations[0].Value, out var parsed) || (organizationId = parsed) == Guid.Empty)) ||
            (type == "platform" && organizations.Length != 0))
        {
            context.Fail("Invalid access identity.");
            return;
        }
        try
        {
            if (!await validator.ValidateAsync(new(userId, organizationId, type, role, accessVersion), context.HttpContext.RequestAborted))
                context.Fail("Access identity is no longer valid.");
        }
        catch (OperationCanceledException) when (context.HttpContext.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // Do not expose provider exceptions, queries, credentials, or identity data.
            LogValidationUnavailable(logger);
            context.Fail("Access identity could not be verified.");
        }
    }

    [LoggerMessage(LogLevel.Warning, "AccessTokenValidationUnavailable")]
    private static partial void LogValidationUnavailable(ILogger logger);
}
