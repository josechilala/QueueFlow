using System.Diagnostics;
using System.Data.Common;
using Microsoft.AspNetCore.Mvc;

namespace QueueFlow.Api.Middleware;

internal static class AuthOperationDiagnostics
{
    private static readonly Action<ILogger, string, string, int, double, string, string, Exception?> Completed =
        LoggerMessage.Define<string, string, int, double, string, string>(LogLevel.Information,
            new EventId(1, "AuthOperationCompleted"),
            "Authentication operation {Operation} completed with {Outcome}. Status: {StatusCode}; DurationMs: {DurationMs}; CorrelationId: {CorrelationId}; Source: {Source}");

    public static void Log(ILogger logger, HttpContext context, string operation, string outcome, int statusCode, long startedAt, string source)
    {
        Completed(logger, operation, outcome, statusCode, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
            context.TraceIdentifier, source, null);
    }

    public static int Status(IActionResult result) => result is ObjectResult objectResult
        ? objectResult.StatusCode ?? StatusCodes.Status200OK
        : StatusCodes.Status200OK;

    public static void LogFailure(ILogger logger, HttpContext context, string operation, long startedAt, Exception exception) =>
        Log(logger, context, operation, exception is DbException ? "database_error" : "unexpected_error",
            StatusCodes.Status500InternalServerError, startedAt, exception is DbException ? "database" : "api_operation");
}
