# QueueFlow — Asaas Sandbox billing (work in progress)

## Scope
- Only sandbox: `https://api-sandbox.asaas.com/v3/`.
- Monthly (`MONTHLY`) and yearly (`YEARLY`) subscriptions.
- Asaas recurring billing types: `CREDIT_CARD` and `PIX`. Debit is **not** a supported recurring subscription billing type; do not label it as automatic debit.
- Pix subscriptions generate charges; customers must pay each charge. Obtain dynamic QR codes via `GET /payments/{id}/pixQrCode`.
- Commercial plan codes, entitlements and prices require product approval; no prices should be invented.
- Keep the existing 14-day trial behavior unchanged.

## Status of this branch
This first commit provides billing period/method validation and an isolated sandbox HTTP transport. It is **not** an operational checkout and is intentionally not wired into the running API. No live payment can be made through this transport.

## Remaining before any checkout is enabled
1. Agree on plan names, features, monthly and annual prices, trial-to-paid transition and grace period.
2. Add persistence/migration for customer, subscription, charges, provider event IDs and unique idempotency keys, with tenant isolation.
3. Implement tenant-authorized checkout with duplicate-subscription prevention and payment-method UX. Prefer Asaas-hosted card checkout/tokenization; never log or persist PAN/CVV.
4. Add authenticated webhook ingress with Asaas access-token validation, deduplication, out-of-order handling, reconciliation and durable retries. Never activate solely from the synchronous creation response.
5. Implement cancellation, renewals, failed payments, Pix QR display/copy-paste and lifecycle management.
6. Add unit/integration tests, verify migrations and execute sandbox scenarios before merge or deployment.

## Secrets
Configure `Asaas__SandboxApiKey` via environment secret management only. Never commit credentials. Sandbox account and production account use separate credentials. Webhook secrets must be managed independently.

## API documentation
- https://docs.asaas.com/docs/sandbox
- https://docs.asaas.com/reference/create-new-subscription
- https://docs.asaas.com/reference/obter-qr-code-para-pagamentos-via-pix
