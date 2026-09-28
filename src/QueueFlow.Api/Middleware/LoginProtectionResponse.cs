using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using QueueFlow.Application.Features.Auth;

namespace QueueFlow.Api.Middleware;

internal static class LoginProtectionResponse
{
    public static IActionResult LoginResponse(this ControllerBase controller, ProtectedLoginResult login)
    {
        if (login.Result.IsSuccess) return controller.Ok(login.Result.Value);
        if (login.RetryAfter is TimeSpan delay)
            controller.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(delay.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        var status = login.Result.Error.Code switch
        {
            "auth.login_limited" => StatusCodes.Status429TooManyRequests,
            "auth.temporarily_unavailable" => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status401Unauthorized,
        };
        if (status == 429)
        {
            RateLimitDiagnostics.Partition(controller.HttpContext, "login", LoginRateLimitKeyMiddleware.PartitionKey(controller.HttpContext));
            RateLimitDiagnostics.Rejected(controller.HttpContext);
        }
        return controller.Problem(login.Result.Error.Description, statusCode: status);
    }
}
