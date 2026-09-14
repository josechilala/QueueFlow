# Queue realtime delivery

The API exposes `/hubs/queue`. `JoinQueueGroup(queuePublicId)` resolves the public capability to the internal group `queue:{queueId}`. `JoinTicketGroup(publicToken)` joins both the private ticket group and its queue. Internal IDs alone are not accepted as public subscription credentials. Existing appointment service subscriptions remain supported.

## Events

- `QueueUpdated`: `{ queueId, waitingCount, timestamp }`. The count comes from committed database state. Consumers refetch their server-rendered state; they do not derive positions from event order or increment counters locally.
- `TicketCalled`: `{ queueId, ticketId, ticketNumber, counterId, counterName, calledAt }`. Used for immediate display updates and recalls. `calledAt` and ticket IDs support ordering and deduplication.
- Existing dotted events remain available for compatibility. The legacy global `queue.updated` carries only the queue public ID and source event; the new events are scoped to queue groups. Private ticket tokens are excluded from queue payloads.

Issuance, call, recall, start, completion, cancellation, no-show and queue open/pause/close notify subscribers. Check-in and expired-ticket cancellation reach the API through `ticket.realtime` outbox messages. Calls are delivered immediately and also retain their existing outbox retry path. Consumers must tolerate duplicates. A delayed call for a completed/cancelled ticket is not replayed as a new call.

The API drains ticket realtime outbox messages; the Worker drains the other message types. This keeps ticket delivery on the process with hub connections even without Redis. No Redis setting is changed. Multiple API instances still require the existing Redis backplane configuration for cross-instance broadcasts.

## Clients

Customer ticket subscriptions include the queue, so another customer's arrival/call/cancellation invalidates the displayed position. Attendant subscribes to queues in its authorized operation context (including closed queues, so reopening is observed). Display subscribes to its queue or the existing set of queues in a branch.

All clients use automatic reconnection, retry an initial failed connection, rejoin groups and refetch after reconnecting. Retries reconnect the transport, not periodic application-data polling. Refetches are coalesced. Display history is restored from API snapshots after a missed event or a new connection. `router.refresh()` requests updated server components without reloading the document.

## Environment

- `QUEUEFLOW_API_URL`: server-side API origin at runtime.
- `NEXT_PUBLIC_QUEUEFLOW_API_URL`: browser-accessible API origin, supplied before the Next build (or dev startup).
- API `Cors:Origins` must include each frontend origin. The CORS header allowlist includes `X-Requested-With`, required by the browser SignalR negotiation.
- Existing `Redis:UseBackplane` and `Redis:ConnectionString` continue to apply; no new configuration is required.

## Integration check

Run an updated local API on port 5288 against a dedicated test database with all migrations applied and development secrets, `Redis__UseBackplane=false`, and increased test rate limits (`RateLimiting__GlobalPermitLimit`, `RateLimiting__PublicPermitLimit`, `RateLimiting__AuthPermitLimit`, e.g. 10000). Then run:

```powershell
$env:QUEUEFLOW_TEST_API_URL = 'http://localhost:5288'
node scripts/test-queue-realtime.mjs
```

The script creates a uniquely named test organization (prints its ID), checks real SignalR subscribers, counts, event payloads, position refetch, start/completion, cancellation, no-show, resubscription, history recovery and isolation from another queue. Test records remain in that local database for inspection.

For browser coverage, start Customer, Display and Attendant on 3201, 3202 and 3203 with both API URL variables pointing to 5288, local insecure cookies, and the matching `QUEUEFLOW_PUBLIC_URL` for Attendant. Add these origins to the test API CORS configuration. Set `QUEUEFLOW_PLAYWRIGHT_MODULE` to the file URL of an installed Playwright `index.mjs`; the same test opens all three pages and checks changes without F5, including reconnection. Playwright is an optional test tool, not a runtime dependency.

## Validation performed

- .NET solution build: zero warnings/errors.
- Existing .NET suites plus the new SignalR CORS preflight regression: 97 passing tests, including all 24 integration tests against a fresh temporary PostgreSQL database.
- Web tests: 17 passing tests; all four Next applications built successfully.
- Real SignalR integration and three simultaneous Chromium pages passed arrival/count, call/counter/history, customer position/status, start/completion, cancellation/no-show, actual disconnect/reconnect, queue isolation and check-in delivery with Redis disabled.

The test database/container and temporary API/frontend processes were stopped after validation. Existing Docker services were not rebuilt or redeployed.
