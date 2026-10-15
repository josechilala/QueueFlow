using Microsoft.AspNetCore.Mvc;
using QueueFlow.Domain.Common;

namespace QueueFlow.Api.Middleware;

public sealed partial class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            LogRequestAborted(logger, context.TraceIdentifier);
            if (!context.Response.HasStarted) context.Response.StatusCode = 499;
        }
        catch (Exception exception) when (context.Response.HasStarted)
        {
            LogStartedResponseFailure(logger, exception.GetType().Name, context.TraceIdentifier);
            throw;
        }
        catch (QueueFlow.Application.Common.ConflictException exception) { await WriteAsync(context, 409, "Conflict", exception.Message); }
        catch (DomainException exception) { await WriteAsync(context, 400, "Business rule violation", exception.Message); }
        catch (UnauthorizedAccessException) { await WriteAsync(context, 401, "Unauthorized", "Authentication and tenant context are required."); }
        catch (Exception exception) { LogUnhandled(logger, exception.GetType().Name, context.TraceIdentifier, (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched"); await WriteAsync(context, 500, "Unexpected error", "An unexpected error occurred."); }
    }
    private static async Task WriteAsync(HttpContext context, int status, string title, string detail)
    { context.Response.StatusCode = status; await context.Response.WriteAsJsonAsync(new ProblemDetails { Status = status, Title = title, Detail = detail, Extensions = { ["correlationId"] = context.TraceIdentifier } }); }
    [LoggerMessage(LogLevel.Error, "Unhandled request error {ExceptionType}. CorrelationId: {CorrelationId} Endpoint: {Endpoint}")]
    private static partial void LogUnhandled(ILogger logger, string exceptionType, string correlationId, string endpoint);
    [LoggerMessage(LogLevel.Information, "Request canceled by client. CorrelationId: {CorrelationId}")]
    private static partial void LogRequestAborted(ILogger logger, string correlationId);
    [LoggerMessage(LogLevel.Error, "Request failed after response started. ExceptionType: {ExceptionType}; CorrelationId: {CorrelationId}")]
    private static partial void LogStartedResponseFailure(ILogger logger, string exceptionType, string correlationId);
}
