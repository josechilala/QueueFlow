using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QueueFlow.Application.Features.Billing;

namespace QueueFlow.Infrastructure.Billing;

internal sealed partial class BillingWebhookWorker(IServiceScopeFactory scopes, ILogger<BillingWebhookWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                for (var index = 0; index < 10; index++)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    if (!await scope.ServiceProvider.GetRequiredService<IBillingWebhookDispatcher>().ProcessNextAsync(stoppingToken)) break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception) { ProcessingFailed(logger); }
        }
    }

    [LoggerMessage(LogLevel.Error, "Billing inbox dispatch failed; pending receipts remain durable for retry.")]
    private static partial void ProcessingFailed(ILogger logger);
}
