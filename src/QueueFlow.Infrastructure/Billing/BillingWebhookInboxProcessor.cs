using Microsoft.EntityFrameworkCore;
using QueueFlow.Application.Features.Billing;
using QueueFlow.Domain.Common;
using QueueFlow.Infrastructure.Persistence;

namespace QueueFlow.Infrastructure.Billing;

/// <summary>Row locks are released on crash. Settlement and completion share one PostgreSQL transaction.</summary>
public sealed class BillingWebhookInboxProcessor(ApplicationDbContext db, IBillingWebhookProcessor processor, TimeProvider clock)
    : IBillingWebhookDispatcher
{
    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var entry = await db.BillingWebhookInbox.FromSqlInterpolated($"""
            SELECT * FROM "BillingWebhookInbox"
            WHERE "Status" = 'Pending' AND "NextAttemptAt" <= {now}
            ORDER BY "ReceivedAt", "Id" FOR UPDATE SKIP LOCKED LIMIT 1
            """).SingleOrDefaultAsync(ct);
        if (entry is null) return false;
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
            await transaction.RollbackToSavepointAsync("settlement", ct);
            var id = entry.Id;
            db.ChangeTracker.Clear();
            entry = await db.BillingWebhookInbox.SingleAsync(x => x.Id == id, ct);
            if (IsPermanent(error)) entry.Complete("Review", PermanentCode(error), clock.GetUtcNow());
            else entry.Retry("TransientProcessingFailure", clock.GetUtcNow());
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

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
