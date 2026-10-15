using Microsoft.EntityFrameworkCore;
using QueueFlow.Application.Common;
using QueueFlow.Application.Features.Billing;
using QueueFlow.Infrastructure.Persistence;

namespace QueueFlow.Infrastructure.Billing;

public sealed class PostgresBillingWebhookInbox(ApplicationDbContext db, TimeProvider clock) : IBillingWebhookInboxStore
{
    public async Task PersistAsync(BillingWebhookEnvelope envelope, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(5));
        ct = budget.Token;
        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // MVCC read: a duplicate must not wait for a consumer holding this receipt's row lock.
        var existing = await db.BillingWebhookInbox.AsNoTracking().SingleOrDefaultAsync(x => x.ProviderEventId == envelope.EventId, ct);
        if (existing is not null)
        {
            EnsureSameEnvelope(existing.EventType, existing.PaymentId, envelope);
            await transaction.CommitAsync(ct);
            return;
        }
        // ON CONFLICT serializes concurrent deliveries by event ID. Never acknowledge before commit.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "BillingWebhookInbox" ("Id", "ProviderEventId", "EventType", "PaymentId", "Status", "Attempts", "ReceivedAt", "NextAttemptAt")
            VALUES ({Guid.NewGuid()}, {envelope.EventId}, {envelope.EventType}, {envelope.PaymentId}, 'Pending', 0, {now}, {now})
            ON CONFLICT ("ProviderEventId") DO NOTHING
            """, ct);
        existing = await db.BillingWebhookInbox.AsNoTracking().SingleAsync(x => x.ProviderEventId == envelope.EventId, ct);
        EnsureSameEnvelope(existing.EventType, existing.PaymentId, envelope);
        await transaction.CommitAsync(ct);
    }

    private static void EnsureSameEnvelope(string eventType, string? paymentId, BillingWebhookEnvelope envelope)
    {
        if (eventType != envelope.EventType || paymentId != envelope.PaymentId)
            throw new ConflictException("Event ID already belongs to a different webhook envelope.");
    }
}
