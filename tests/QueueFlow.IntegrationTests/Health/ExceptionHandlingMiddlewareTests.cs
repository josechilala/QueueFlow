using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using QueueFlow.Api.Middleware;
using QueueFlow.Application.Common;
using QueueFlow.Domain.Common;

namespace QueueFlow.IntegrationTests.Health;

public sealed class ExceptionHandlingMiddlewareTests
{
    [Fact]
    public async Task CancellationDuringRequestExecutionDoesNotBecome500()
    {
        using var cancellation = new CancellationTokenSource();
        var context = Context(false);
        context.RequestAborted = cancellation.Token;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var logs = new HealthLogCapture();
        using var factory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Debug).AddProvider(logs));
        var middleware = new ExceptionHandlingMiddleware(async request =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, request.RequestAborted);
        }, factory.CreateLogger<ExceptionHandlingMiddleware>());
        var processing = middleware.InvokeAsync(context);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        await processing;
        Assert.Equal(499, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Body.Length);
        Assert.DoesNotContain(logs.Entries, x => x.Level >= LogLevel.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClientCancellationDoesNotWriteAnErrorOrReplaceStartedResponse(bool started)
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var context = Context(started);
        context.RequestAborted = cancellation.Token;
        var logs = new HealthLogCapture();
        using var factory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Debug).AddProvider(logs));
        var middleware = new ExceptionHandlingMiddleware(_ => throw new OperationCanceledException(cancellation.Token),
            factory.CreateLogger<ExceptionHandlingMiddleware>());

        await middleware.InvokeAsync(context);

        Assert.Equal(started ? 202 : 499, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Body.Length);
        Assert.Contains(logs.Entries, x => x.Message.Contains("canceled by client", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Entries, x => x.Level >= LogLevel.Error);
    }

    [Theory]
    [InlineData("unexpected", 500)]
    [InlineData("internal-cancellation", 500)]
    [InlineData("conflict", 409)]
    [InlineData("domain", 400)]
    [InlineData("unauthorized", 401)]
    public async Task ExistingMappingsArePreserved(string kind, int status)
    {
        var context = Context(false);
        var logs = new HealthLogCapture();
        using var factory = LoggerFactory.Create(b => b.AddProvider(logs));
        var middleware = new ExceptionHandlingMiddleware(_ => throw Failure(kind), factory.CreateLogger<ExceptionHandlingMiddleware>());

        await middleware.InvokeAsync(context);

        Assert.Equal(status, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync(TestContext.Current.CancellationToken);
        Assert.Contains("correlationId", body, StringComparison.Ordinal);
        if (status == 500) Assert.DoesNotContain("private-detail", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("unexpected")]
    [InlineData("internal-cancellation")]
    [InlineData("conflict")]
    [InlineData("domain")]
    [InlineData("unauthorized")]
    public async Task StartedResponsesAreNeverRewrittenAndOriginalExceptionIsRethrown(string kind)
    {
        var context = Context(true);
        var failure = Failure(kind);
        var logs = new HealthLogCapture();
        using var factory = LoggerFactory.Create(b => b.AddProvider(logs));
        var middleware = new ExceptionHandlingMiddleware(_ => throw failure, factory.CreateLogger<ExceptionHandlingMiddleware>());

        var actual = await Assert.ThrowsAnyAsync<Exception>(() => middleware.InvokeAsync(context));

        Assert.Same(failure, actual);
        Assert.Equal(202, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Body.Length);
        Assert.DoesNotContain(logs.Entries, x => x.Message.Contains("private-detail", StringComparison.Ordinal) || x.Exception is not null);
    }

    private static DefaultHttpContext Context(bool started)
    {
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpResponseFeature>(new ResponseFeature(started));
        context.Response.Body = new MemoryStream();
        context.Response.StatusCode = 202;
        return context;
    }

    private static Exception Failure(string kind) => kind switch
    {
        "conflict" => new ConflictException("private-detail"),
        "domain" => new DomainException("private-detail"),
        "unauthorized" => new UnauthorizedAccessException("private-detail"),
        "internal-cancellation" => new OperationCanceledException("private-detail"),
        _ => new InvalidOperationException("private-detail")
    };

    private sealed class ResponseFeature(bool started) : HttpResponseFeature
    {
        public override bool HasStarted => started;
    }
}

internal sealed class HealthLogCapture : ILoggerProvider
{
    public System.Collections.Concurrent.ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();
    public ILogger CreateLogger(string categoryName) => new CaptureLogger(this);
    public void Dispose() { }

    private sealed class CaptureLogger(HealthLogCapture capture) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            capture.Entries.Enqueue((logLevel, formatter(state, exception), exception));
    }
}
