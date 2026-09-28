# Authentication throttling

The previous pre-authentication fixed window counted every login request, including
successful logins. The global anonymous-IP bucket also combined BFF traffic. Neither
counter was reset by success, and each API process maintained independent state.

## Policies

- Login: `RateLimiting:AuthPermitLimit` (unchanged default 10) counts only
  `auth.invalid_credentials` / `platform.invalid_credentials`. The window expires
  one minute after its first failure. A successful login deletes the failure state.
  Ten failures block subsequent attempts until expiry, including correct credentials;
  this intentional temporary protection prevents brute force. Other service errors,
  malformed bodies and canceled authentication do not increment this counter.
- Partition: identity realm (tenant/platform) plus SHA-256 of trimmed, lowercase
  email, independent of source IP. Unknown emails use the same state and responses.
  Password verification also runs against a substitute hash for missing users;
  inactive users do not skip verification. A bounded lease serializes authentication for an identity; concurrent requests
  cannot bypass the threshold. Expired owners cannot overwrite newer state. Waiting
  longer than five seconds returns retryable 503, not a credential failure.
- Refresh: `RateLimiting:RefreshPermitLimit` (unchanged default 30/minute), independent
  from login. The signed token identity selects a stable bucket across rotations;
  unverifiable tokens share a source-IP bucket so changing random tokens cannot
  bypass throttling. Successful login does not reset refresh usage.
- Login/refresh do not also acquire the global anonymous-IP budget. Other endpoints
  retain their existing limits. Malformed login input has a separate `login-input`
  IP budget; registration retains `auth`. Rate-limit responses include Retry-After.

## Distributed state

Production and staging require the existing `Redis:ConnectionString`
(`Redis__ConnectionString` environment variable). This is independent from
`Redis:UseBackplane`. All API instances must use the same Redis database and namespace.
`RateLimiting:UseRedis=false` is rejected outside Development/Test. Local environments
can opt into Redis with `RateLimiting__UseRedis=true`; otherwise they use bounded
in-memory state for development only. Redis Lua operations are atomic and use Redis
server time, with expiring keys prefixed `queueflow:authentication:v1:`. No password,
raw email, access token or refresh token is stored in the throttle.

Redis failure returns 503 with Retry-After, without falling back to unprotected or
per-process authentication. Redis should use a non-evicting policy and capacity
monitoring; clearing or evicting these keys clears protection state. No migration is
needed. No production infrastructure was provisioned or modified by this change.

## Refresh concurrency

The BFF already shares in-flight refreshes by token and browser proof. The Admin
client additionally shares recovery requests within the tab, including component
remounts without Web Locks. Cross-tab Web Locks and bounded retry/cooldown remain.
The API's PostgreSQL row lock and single-use rotation protect across BFF/API instances:
one winner receives credentials, stale concurrent requests receive 409 without
credentials. A conflict does not trigger an automatic rotation loop or cookie deletion.

## Validation

`AuthenticationThrottleStoreTests` exercises memory and two independent Redis
connections: successes, consecutive failures, reset, concurrency, expiry, fencing,
separate refresh quotas and unavailable Redis. `LoginRateLimitLifecycleTests` uses
two API hosts with real PostgreSQL and Redis for both authentication realms, including
refresh concurrency and generic errors. It requires an already migrated disposable
local database; it does not apply migrations. Configure `QUEUEFLOW_PLATFORM_TEST_DATABASE`
(host 127.0.0.1, database name starting queueflow_platform_test) and
`QUEUEFLOW_AUTH_TEST_REDIS` (127.0.0.1 endpoint). Frontend tests cover duplicate login,
BFF single-flight, cooldown and recovery concurrency.
