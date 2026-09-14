# Public service catalog

The unit QR remains `/unidade/{branchPublicId}`. The public branch endpoint previously listed queues plus a separate appointment collection, hiding queue-only services without an active queue and duplicating hybrid services.

## Domain and migration

`Queue.ServiceId` remains authoritative. At most one active queue is allowed for `(OrganizationId, BranchId, ServiceId)`, including draft and paused queues. Closed queues are inactive and remain as history. The domain does not allow reopening closed queues; create a new queue after closure.

Migration: `20260914061709_EnforceSingleActiveQueuePerService`.
Index: `UX_Queues_ActiveService`, unique, filtered by `"IsActive" = true`.
Application validation rejects duplicates before insertion; the PostgreSQL constraint handles concurrent requests, translated into HTTP 400 with the same user-facing message. Admin already renders ProblemDetails and needs no change.

No composite foreign keys were added: the current persistence model uses scalar identifiers without these relationships. Introducing alternate keys and retrofitting historical tables would expand this change considerably. Queue creation/transitions and public ticket issuance explicitly validate organization/branch/service ownership, and public catalog queries scope every related collection. The unique index guarantees cardinality, not referential ownership for manual SQL writes.

## API

`GET /api/v1/public/branches/{publicId}` retains existing branch fields and legacy `queues` / `appointmentServices` for compatibility, adding `services`:

```json
{
  "publicId": "branch-public-id",
  "organizationName": "Company",
  "name": "Salon",
  "services": [{
    "publicId": "service-public-id",
    "name": "Haircut",
    "description": null,
    "attendanceMode": "QueueOnly",
    "queue": {
      "publicId": "queue-public-id",
      "name": "Haircut queue",
      "serviceName": "Haircut",
      "status": "Open",
      "waitingCount": 2,
      "estimatedWaitMinutes": 20,
      "acceptsNewTickets": true
    },
    "canJoinQueue": true,
    "canSchedule": false
  }]
}
```

`PublicBranchDto.Services` uses `PublicBranchCatalogServiceDto`, reusing `PublicBranchQueueDto`. A service without an active queue has `queue: null` and `canJoinQueue: false`. Capacity, queue status, service mode and active scheduling settings determine actions; availability of a specific appointment time is checked by the existing booking flow.

Public organization/service landing queries also use the unique ID relationship, eliminating first-by-name selection. Direct queue links remain compatible, with stronger ownership and attendance-mode checks. Ticket creation preserves all four ownership IDs. The unit page subscribes to its service queues, and the existing ticket page retains SignalR subscriptions and backend refetches.

## Validation and rollout

Run `scripts/check-active-queue-duplicates.sql` against the target database before deployment. It lists IDs, names, ownership and status of every duplicate active queue. Local Docker blueprint check returned zero rows. Production was not queried or migrated.

The migration aborts explicitly if duplicate groups exist and never picks a survivor or deletes history. Review duplicates with the operator before closing any queue with active tickets. PostgreSQL index creation can block writes; schedule an appropriate deployment window. Apply the migration before serving the updated API, then deploy Customer together with the API.

Validation uses a disposable PostgreSQL database (port 55439), not the running local application database. Run .NET integration tests with `ConnectionStrings__QueueFlowDatabase` pointing at a migrated test database. `PublicBranchCatalogTests` covers modes, inactive/foreign services, no-queue services, closed/paused queues, ticket IDs, unique constraint and historical queues.

`node scripts/test-public-catalog.mjs` uses `QUEUEFLOW_TEST_API_URL` (default localhost:5288), optionally `QUEUEFLOW_TEST_CUSTOMER_URL` for rendered HTML links. It creates test organizations and must run against a disposable database. It tests the three-service scenario, concurrent queue creation and cross-tenant/branch rejection.

`node scripts/test-queue-realtime.mjs` validates scoped queue/ticket events, call/position/status, resubscription and appointment check-in without Redis. No Redis deployment configuration changed.

Results: final solution build passed with zero warnings/errors; 38 Domain, 33 Application, 2 Architecture and 25 Integration tests passed across runs. The first integration run had connection timeouts; rerun with a longer test-only connection timeout passed, with the initially skipped golden-path test subsequently passing independently. All 17 web tests and all four Next.js production builds passed. HTTP catalog/concurrent-creation/rendered-link tests and real SignalR integration passed. The duplicate preflight was also exercised with a temporary table and rolled back after the expected exception. Temporary API, Customer server and PostgreSQL were stopped after verification. No deployment or production migration was performed.
