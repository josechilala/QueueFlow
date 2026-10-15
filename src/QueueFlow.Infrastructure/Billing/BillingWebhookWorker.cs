using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QueueFlow.Application.Features.Billing;
using QueueFlow.Application.Observability;

namespace QueueFlow.Infrastructure.Billing;

internal sealed partial class BillingWebhookWorker(IServiceScopeFactory scopes, ILogger<BillingWebhookWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        QueueFlowTelemetry.ObserveBilling(() => Started(logger));
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    for (var index = 0; index < 10; index++)
                    {
                        await using var scope = scopes.CreateAsyncScope();
                        var processed = await scope.ServiceProvider.GetRequiredService<IBillingWebhookDispatcher>().ProcessNextAsync(stoppingToken);
                        QueueFlowTelemetry.RecordBillingPollSuccess();
                        if (!processed) break;
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception) { QueueFlowTelemetry.ObserveBilling(() => ProcessingFailed(logger, "Dispatch", "DispatcherFailure", "Unknown")); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { QueueFlowTelemetry.ObserveBilling(() => Stopping(logger)); }
    }

    [LoggerMessage(EventName = "BillingInboxInfrastructureFailure", Level = LogLevel.Error,
        Message = "BillingInboxInfrastructureFailure Stage={Stage} FailureKind={FailureKind} CommitOutcome={CommitOutcome}")]
    private static partial void ProcessingFailed(ILogger logger, string stage, string failureKind, string commitOutcome);
    [LoggerMessage(EventName = "BillingInboxWorkerStarted", Level = LogLevel.Information, Message = "BillingInboxWorkerStarted")]
    private static partial void Started(ILogger logger);
    [LoggerMessage(EventName = "BillingInboxWorkerStopping", Level = LogLevel.Information, Message = "BillingInboxWorkerStopping")]
    private static partial void Stopping(ILogger logger);
}

internal sealed partial class BillingInboxWorkerDisabledReporter(ILogger<BillingInboxWorkerDisabledReporter> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        QueueFlowTelemetry.ObserveBilling(() => Disabled(logger));
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(EventName = "BillingInboxWorkerDisabled", Level = LogLevel.Information, Message = "BillingInboxWorkerDisabled Reason=SandboxCheckoutNotEnabled")]
    private static partial void Disabled(ILogger logger);
}
