using System.Runtime.CompilerServices;
using QueueFlow.Domain.Enums;

namespace QueueFlow.Infrastructure.Persistence;

internal static class PostgresQueueOrdering
{
    private const string Candidates = """
        FROM "QueueTickets" ticket
        LEFT JOIN "Appointments" appointment ON appointment."QueueTicketId" = ticket."Id" AND appointment."OrganizationId" = ticket."OrganizationId"
        LEFT JOIN "ServiceSchedulingSettings" settings ON settings."ServiceId" = ticket."ServiceId" AND settings."OrganizationId" = ticket."OrganizationId"
        WHERE ticket."OrganizationId" = {0} AND ticket."QueueId" = {1} AND ticket."Status" = {3}
        """;

    private const string ScheduledPriority = """
        appointment."Id" IS NOT NULL
        AND {2} <= appointment."ScheduledStart" + make_interval(mins => COALESCE(settings."LateToleranceMinutes", 0))
        AND (SELECT COUNT(*) FROM "QueueTickets" active_ticket
             JOIN "Appointments" active_appointment ON active_appointment."QueueTicketId" = active_ticket."Id"
             WHERE active_ticket."OrganizationId" = ticket."OrganizationId"
               AND active_ticket."QueueId" = ticket."QueueId"
               AND active_ticket."Status" IN ({4}, {5})) < GREATEST(1, COALESCE(settings."CapacityPerSlot", 1))
        """;

    // Future appointments remain behind callable tickets; Next excludes them until ScheduledStart.
    private static readonly string Order = $$"""
        CASE WHEN appointment."ScheduledStart" > {2} THEN 1 ELSE 0 END,
        CASE
            WHEN appointment."Id" IS NULL AND ticket."IssuedAt" <= {2} - make_interval(mins => GREATEST(5, COALESCE(settings."SlotDurationMinutes", 30))) THEN 0
            WHEN {{ScheduledPriority}} THEN 1
            ELSE 2
        END,
        CASE WHEN {{ScheduledPriority}} THEN appointment."ScheduledStart" END,
        CASE WHEN {{ScheduledPriority}} THEN appointment."CheckedInAt" END,
        ticket."Priority" DESC, ticket."IssuedAt", ticket."SequenceNumber"
        """;

    public static FormattableString Next(Guid organizationId, Guid queueId, DateTimeOffset now) =>
        Query($$"""
            SELECT ticket.*, ticket.xmin {{Candidates}}
            AND (appointment."Id" IS NULL OR appointment."ScheduledStart" <= {2})
            ORDER BY {{Order}}
            FOR UPDATE OF ticket SKIP LOCKED LIMIT 1
            """, organizationId, queueId, now);

    public static FormattableString TicketsAhead(Guid organizationId, Guid queueId, Guid ticketId, DateTimeOffset now) =>
        Query($$"""
            SELECT CAST(ranked."Position" - 1 AS integer) AS "Value"
            FROM (SELECT ticket."Id", ROW_NUMBER() OVER (ORDER BY {{Order}}) AS "Position" {{Candidates}}) ranked
            WHERE ranked."Id" = {6}
            """, organizationId, queueId, now, ticketId);

    // Only fixed SQL fragments are composed; all values remain database parameters.
    private static FormattableString Query(string sql, Guid organizationId, Guid queueId, DateTimeOffset now, Guid ticketId = default) =>
        FormattableStringFactory.Create(sql, organizationId, queueId, now, (int)TicketStatus.Waiting, (int)TicketStatus.Called, (int)TicketStatus.InService, ticketId);
}
