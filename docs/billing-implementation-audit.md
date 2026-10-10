# Billing Sandbox audit — 2026-10-10

## Repository protection

Work remains on `feature/asaas-sandbox-billing`. The existing stash, unrelated untracked file beginning with `ntent`, and the existing migration were preserved. No reset, clean, stash pop, commit, push, merge, deployment, provider mutation or database migration was executed during implementation.

## Database configuration finding

The API's UserSecretsId is declared in its project file. Its database secret is a normal ADO.NET connection string, with Host, Port, Database, Username and Password assignments. The Host is loopback; this alone does not establish a dedicated development database. No host, database name, credentials or secret values are included in this report.

The earlier PowerShell parser result was an adapter problem: `$builder.ConnectionString = $value` on `System.Data.Common.DbConnectionStringBuilder` inserted a dictionary key named `ConnectionString`. Use `$builder.set_ConnectionString($value)` to invoke the CLR property setter. There is no nested connection wrapper to fix in User Secrets.

`WebApplication.CreateBuilder` supplies the normal configuration precedence: appsettings, environment-specific appsettings, Development User Secrets, environment variables, command-line arguments. In the audited shell neither environment selector nor database environment override was present. Launch profiles can select Development separately; therefore this audit of the stored secret does not claim to identify every possible runtime connection. Infrastructure reads `GetConnectionString("QueueFlowDatabase")`. The API applies migrations only through its explicit `--migrate-only` path.

Both current API launch profiles select Development and supply no database override. `scripts/audit-billing-connection.ps1` reproduces the secret-format inspection without disclosing any values. It intentionally reports development isolation as unproven.

An offline `ApplicationDbContextFactory` now supplies model-only options at loopback port 1. EF tooling no longer needs to initialize API services or load application secrets to generate SQL/model artifacts. Actual application of SQL still requires an explicitly selected and verified development connection.

## Separate development database

`docker-compose.billing-dev.yml` defines a separate PostgreSQL service, database, volume and loopback-only port 55432. Set `BILLING_DEV_POSTGRES_PASSWORD` to a development-only password before starting it. Docker access was not available inside this session's sandbox; no container was started.

Configure the development API connection through secret management or a process-scoped override:

```text
ConnectionStrings__QueueFlowDatabase=Host=127.0.0.1;Port=55432;Database=queueflow_billing_dev;Username=queueflow_billing_dev;Password=<development-only password>
Asaas__Environment=Sandbox
Asaas__SandboxApiKey=<sandbox key>
Asaas__WebhookToken=<independent random token, at least 32 characters>
Asaas__CheckoutEnabled=false
```

Keep checkout disabled until migrations and PostgreSQL/provider smoke tests have passed on this dedicated environment. Do not use production credentials, switch to the production API or apply the migration to the previously configured database merely because its Host is loopback.

## Persistence stage

`BillingCharge` validates supported selections and exact currency precision. Tenant filters and write validation remain in effect. The existing migration was extended with a restricted foreign key to Organizations, customer references and the minimal `BillingWebhookEvents` receipt table. The existing migration ID is retained; no duplicate migration was generated. The snapshot/designer include PostgreSQL's system `xmin` concurrency token, which needs no user column.

The database enforces tenant/idempotency uniqueness and global payment/event ID uniqueness. Provider references cannot be reassigned; cancelled/refunded charges cannot become paid through a delayed callback. Subscription activation leaves TrialEndsAt intact and does not reactivate cancelled subscriptions.

## Checkout and webhooks stage

Owner-only endpoints derive tenant identity exclusively from authentication. The server calculates prices. A PostgreSQL transaction/advisory lock reserves a charge before any provider mutation. Repeated keys return the same charge and conflicting selections return 409. An outstanding checkout or existing recurring subscription blocks another independent checkout.

Monthly Pix and monthly/yearly credit create Asaas subscriptions; annual Pix creates one upfront payment. Credit card data is entered on the provider's hosted invoice page, never in QueueFlow. Debit is excluded. The HTTP transport requires Sandbox configuration, uses a fixed API endpoint, disables redirects/cookies and HTTP client loggers, and never retries POST automatically.

Network errors and ambiguous provider/persistence outcomes produce an `Uncertain` charge. Read-only provider reconciliation can recover a single payment from a stored subscription or external reference; it never creates a replacement. If the subscription was created but its ID could not be persisted and its generated payment has no matching external reference, manual provider reconciliation remains necessary. Do not delete that reservation or issue another charge blindly.

Webhooks compare a distinct authentication token in constant time, bound request size, validate minimal event fields, query the provider again and reconcile exact customer, amount, method and references. A tenant lock serializes processing; event receipts and charge/subscription changes commit together. Current provider state governs processing, not the callback's claimed status. Provider confirmation that is not yet visible returns 503 for retry. No successful acknowledgment is sent before known events are durably processed.

Existing OpenTelemetry infrastructure also exports counters for durable reservations, ambiguous outcomes and processed events. These counters carry no customer, tenant, credential or provider-payload tags.

Recurring payment events match only subscriptions already associated with a tenant charge. Each renewal receives its own unique payment/idempotency record; its provider due date determines ordering. Unknown provider payments cannot activate a tenant. Unrecognized event families are ignored; configure the payment events consumed by this implementation, rather than subscription/checkout event families.

## Frontend stage

The `/billing` screen provides selection, monthly/yearly totals, customer data, hosted credit payment, Pix instructions/QR/copy-paste, errors, in-flight controls and status lookup through the authenticated BFF. The browser sends neither tenant ID nor price. Capability checks keep payment creation disabled by default. Network retries retain the same key. The stored browser value is only a charge UUID; the backend checks tenant ownership on every read.

## Remaining operational verification

No external Sandbox requests were made. Validate credit acceptance via the hosted invoice, monthly Pix invoice generation, annual upfront amounts, QR codes, duplicate delivery, renewals and refunds with the dedicated database/provider account before enabling checkout.

Provider subscription cancellation and plan changes are not exposed by this increment. Billing PastDue scheduling/expiry and cancellation propagation need a separate lifecycle implementation before treating this module as complete SaaS entitlement enforcement. An existing recurring subscription is intentionally blocked from a second checkout. Refund reconciliation updates an active subscription to PastDue but does not erase or shorten trial metadata.

See `asaas-sandbox-billing.md` for the catalog and official provider references. Execution results are recorded below after validation.

## Files by stage

| Stage | Files |
| --- | --- |
| Audit/environment | `scripts/audit-billing-connection.ps1`, `docker-compose.billing-dev.yml`, `ApplicationDbContextFactory.cs`, this report and `asaas-sandbox-billing.md` |
| Persistence/domain | `BillingCharge.cs`, `BillingWebhookEvent.cs`, `TenantEntities.cs` (Subscription methods), `ApplicationDbContext.cs`, existing `20261010033728_AddBillingCharges` migration/designer and model snapshot |
| Checkout/provider | `BillingCheckout.cs`, `BillingCheckoutService.cs`, `AsaasSandboxClient.cs`, `BillingCheckoutController.cs`, infrastructure DI and `QueueFlowTelemetry.cs` |
| Webhooks | `BillingWebhookProcessor.cs`, `AsaasWebhookController.cs` |
| Frontend | billing page, `components/billing-checkout.tsx`, authenticated `app/api/billing` route handlers |
| Tests | `BillingSettlementTests.cs`, `BillingPersistenceTests.cs`, `AsaasSandboxTransportTests.cs`, `BillingEndpointSecurityTests.cs`, `BillingPostgresTests.cs`, `billing-checkout.test.tsx` |

Official references consulted: [subscription creation](https://docs.asaas.com/reference/create-new-subscription), [hosted payment methods](https://docs.asaas.com/docs/asaas-checkout), [payment events](https://docs.asaas.com/docs/payment-events), [webhook authentication/idempotency](https://docs.asaas.com/docs/about-webhooks). No debit acceptance is inferred from the general API product description.

## Executed validation

| Command/check | Actual result |
| --- | --- |
| API `dotnet build` | Passed, zero warnings/errors, including the final persistence constraints |
| Full Application test project | 71 passed, zero failures/skips |
| Billing/Asaas IntegrationTests filter | 17 passed, zero failures, 4 PostgreSQL tests skipped |
| Admin web `npm.cmd ... run build` | Passed, including TypeScript and the new BFF routes |
| Admin web `npm.cmd ... test -- --maxWorkers=1 --pool=threads` | 282 passed across 24 files, zero failures |
| Migration/runtime model parity | Passed (`HasPendingModelChanges` is false) |
| Audit PowerShell syntax | Zero parse errors |
| `git diff --check` | Passed |

The plain PowerShell `npm` invocation was blocked by execution policy. `npm.cmd` works without changing policy. The first default-worker Vitest run had 266 passing tests and two worker startup timeouts; the entire final suite passed using one threads worker. Earlier analyzer errors in the generated migration/controller and the offline HTTP fixture's CORS configuration were corrected before the passing final runs.

The four skipped PostgreSQL cases cover concurrent identical checkout requests, ambiguous provider acceptance, tenant isolation/database uniqueness, and duplicate/out-of-order settlement/refund events. They require `QUEUEFLOW_BILLING_TEST_DATABASE` pointing to an explicitly disposable database whose Host is exactly `127.0.0.1` and whose name starts with `queueflow_billing_test`. Each test migrates and removes only its own randomly generated schema. No API User Secret is used as a fallback. They are not reported as passed.

`docs/sql/20261010033728_AddBillingCharges.sql` is an offline review artifact for this migration only, starting after the already-existing `20261006000000_AddRefreshTokenHashUniqueIndex`. It creates billing tables, constraints and indexes plus the migration-history entry; it has not been applied. The Next.js-generated change to `next-env.d.ts` was restored to its original content.

Next step: provision/verify the isolated development database, run the four PostgreSQL cases, apply the reviewed migration only to that isolated target, then complete provider smoke tests before setting CheckoutEnabled to true. Subscription cancellation/plan changes and automated delinquency/entitlement expiry remain explicit follow-up work.
