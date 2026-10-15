using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using QueueFlow.Application.Features.Billing;
using QueueFlow.Application.Observability;
using QueueFlow.Domain.Common;
using QueueFlow.Domain.Entities;
using QueueFlow.Infrastructure.Persistence;

namespace QueueFlow.Infrastructure.Billing;

/// <summary>Row locks are released on crash. Settlement and completion share one PostgreSQL transaction.</summary>
public sealed partial class BillingWebhookInboxProcessor(ApplicationDbContext db, IBillingWebhookProcessor processor, TimeProvider clock,
    ILogger<BillingWebhookInboxProcessor>? logger = null)
    : IBillingWebhookDispatcher
{
    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        var stage = "Query";
        try { return await ProcessCoreAsync(value => stage = value, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested && stage != "Commit") { throw; }
        catch (Exception error)
        {
            ObserveFailure(stage, error);
            throw;
        }
    }

    private async Task<bool> ProcessCoreAsync(Action<string> setStage, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var entry = await db.BillingWebhookInbox.FromSqlInterpolated($"""
            SELECT * FROM "BillingWebhookInbox"
            WHERE "Status" = 'Pending' AND "NextAttemptAt" <= {now}
            ORDER BY "ReceivedAt", "Id" FOR UPDATE SKIP LOCKED LIMIT 1
            """).SingleOrDefaultAsync(ct);
        if (entry is null)
        {
            QueueFlowTelemetry.RecordBillingPollSuccess();
            return false;
        }
        setStage("Processing");
        // SaveChanges inside the financial processor accepts EF states before the outer commit.
        // Compare tracked ledger identities, not Processed status, to preserve BillingEvents semantics.
        var previousEvents = new HashSet<Guid>();
        QueueFlowTelemetry.ObserveBilling(() => previousEvents.UnionWith(db.ChangeTracker.Entries<BillingWebhookEvent>().Select(x => x.Entity.Id)));
        await transaction.CreateSavepointAsync("settlement", ct);
        try
        {
            var outcome = ReceiveBillingWebhook.Supports(entry.EventType)
                ? await processor.ProcessAsync(entry.ProviderEventId, entry.EventType, entry.PaymentId!, ct)
                : BillingWebhookOutcome.Ignored;
            if (outcome == BillingWebhookOutcome.Retry) entry.Retry("PaymentNotMatched", clock.GetUtcNow());
            else entry.Complete(outcome.ToString(), outcome == BillingWebhookOutcome.Review ? "CancelledPaymentFinancialReview" : null, clock.GetUtcNow());
        }
        catch (Exception error) when (!ct.IsCancellationRequested &&
            error is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or
                InvalidOperationException or DomainException or UnauthorizedAccessException or DbUpdateException)
        {
            // Roll back partial settlement AND detach mutated entities before persisting the retry/review.
            ObserveFailure("Processing", error);
            setStage("Recovery");
            await transaction.RollbackToSavepointAsync("settlement", ct);
            var id = entry.Id;
            db.ChangeTracker.Clear();
            entry = await db.BillingWebhookInbox.SingleAsync(x => x.Id == id, ct);
            if (IsPermanent(error)) entry.Complete("Review", PermanentCode(error), clock.GetUtcNow());
            else entry.Retry("TransientProcessingFailure", clock.GetUtcNow());
        }
        var newEvents = 0;
        QueueFlowTelemetry.ObserveBilling(() => newEvents = db.ChangeTracker.Entries<BillingWebhookEvent>()
            .Count(x => !previousEvents.Contains(x.Entity.Id)));
        setStage("Persistence");
        await db.SaveChangesAsync(ct);
        setStage("Commit");
        await transaction.CommitAsync(ct);
        setStage("Committed");
        QueueFlowTelemetry.RecordBillingPollSuccess();
        if (newEvents > 0) QueueFlowTelemetry.ObserveBilling(() => QueueFlowTelemetry.BillingEvents.Add(newEvents));
        QueueFlowTelemetry.ObserveBilling(() => QueueFlowTelemetry.BillingInboxDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds));
        if (entry.Status == "Pending")
        {
            QueueFlowTelemetry.ObserveBilling(() => QueueFlowTelemetry.BillingInboxRetries.Add(1));
            if (logger is not null) QueueFlowTelemetry.ObserveBilling(() => RetryCommitted(logger, entry.Id, entry.Attempts));
        }
        else
        {
            QueueFlowTelemetry.ObserveBilling(() => QueueFlowTelemetry.BillingInboxOutcomes.Add(1, new KeyValuePair<string, object?>("status", entry.Status)));
            if (logger is not null) QueueFlowTelemetry.ObserveBilling(() => OutcomeCommitted(logger, entry.Id, entry.Status, entry.Attempts));
        }
        return true;
    }

    private void ObserveFailure(string stage, Exception error)
    {
        var kind = error switch
        {
            OperationCanceledException => "Canceled",
            HttpRequestException => "ProviderCommunication",
            UnauthorizedAccessException => "TenantValidation",
            DomainException => "PaymentValidation",
            System.Data.Common.DbException or DbUpdateException => "Database",
            _ => "Unexpected"
        };
        QueueFlowTelemetry.ObserveBilling(() => QueueFlowTelemetry.BillingInboxFailures.Add(1,
            new KeyValuePair<string, object?>("stage", stage), new KeyValuePair<string, object?>("kind", kind)));
        var commitOutcome = stage switch { "Commit" => "Unknown", "Committed" => "Acknowledged", _ => "NotAttempted" };
        if (logger is not null) QueueFlowTelemetry.ObserveBilling(() => InfrastructureFailure(logger, stage, kind, commitOutcome));
    }

    [LoggerMessage(EventName = "BillingInboxOutcomeCommitted", Level = LogLevel.Information, Message = "BillingInboxOutcomeCommitted InboxId={InboxId} Status={Status} Attempt={Attempt}")]
    private static partial void OutcomeCommitted(ILogger logger, Guid inboxId, string status, int attempt);
    [LoggerMessage(EventName = "BillingInboxRetryCommitted", Level = LogLevel.Warning, Message = "BillingInboxRetryCommitted InboxId={InboxId} Attempt={Attempt}")]
    private static partial void RetryCommitted(ILogger logger, Guid inboxId, int attempt);
    [LoggerMessage(EventName = "BillingInboxInfrastructureFailure", Level = LogLevel.Error, Message = "BillingInboxInfrastructureFailure Stage={Stage} FailureKind={FailureKind} CommitOutcome={CommitOutcome}")]
    private static partial void InfrastructureFailure(ILogger logger, string stage, string failureKind, string commitOutcome);

    private static bool IsPermanent(Exception error) => error is DomainException or UnauthorizedAccessException ||
        error is HttpRequestException { StatusCode: { } status } &&
        (int)status is >= 400 and < 500 && status is not (System.Net.HttpStatusCode.RequestTimeout or System.Net.HttpStatusCode.TooManyRequests);

    private static string PermanentCode(Exception error) => error switch
    {
        UnauthorizedAccessException => "TenantNotAllowed",
        DomainException => "PaymentValidationFailed",
        _ => "ProviderRejectedRead"
    };
}
