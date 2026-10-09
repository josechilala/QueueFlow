# QueueFlow — Asaas Sandbox billing (work in progress)

## Approved commercial catalog (BRL)
| Plan | Monthly | Annual, 5% discount | Attendants |
|---|---:|---:|---|
| Profissional | R$ 69,90 | R$ 796,86 upfront | 3 |
| Empresarial | R$ 179,90 | R$ 2.050,86 upfront | Expandable; seat add-on pricing pending |

Annual prices equal 12 monthly payments less 5%, rounded to cents. Annual Pix and debit must be paid **in full** before service activation; never create twelve installment invoices for these methods. Credit card may use the annual Asaas subscription cycle, charging the full annual amount each year. Annual credit-card installment support is not included.

## Payment collection
- Monthly credit: Asaas subscription (`MONTHLY`, `CREDIT_CARD`).
- Monthly Pix: Asaas subscription (`MONTHLY`, `PIX`); each generated invoice must be paid by the customer.
- Annual credit: Asaas subscription (`YEARLY`, `CREDIT_CARD`), full annual charge per renewal.
- Annual Pix: **one-time annual invoice** (`PIX`), full amount; renewal needs a new annual invoice.
- Debit: **one-time payment** only, full monthly or annual amount; debit acceptance depends on the Asaas hosted invoice/checkout product and must be confirmed in sandbox before offering the option in UI. Never send `DEBIT_CARD` as a subscription billing type.

## Implementation status
The branch currently has a domain-side price and collection-mode catalog plus an isolated Asaas sandbox transport. Neither is wired to live routes. No operational checkout, debit payment, webhook, migration, tenant state transition, or tests have been completed.

## Requirements before enabling
1. Add tenant-scoped billing persistence, unique payment/provider IDs and migration.
2. Add authorized checkout, customer mapping, one-time payment API and hosted debit flow; never store PAN/CVV.
3. Validate webhook token, deduplicate events, reconcile with provider, handle retries and out-of-order delivery; only confirmed payment may activate service.
4. Add Pix QR display/copy-paste, cancellation, delinquency and renewal rules.
5. Test all sandbox payment paths, annual upfront rules and cross-tenant isolation; run build and tests.
6. Keep existing 14-day trial unchanged until explicit transition rules are approved.

## Secrets and environment
Use `Asaas__SandboxApiKey` from secret management, never the repository. Sandbox endpoint: `https://api-sandbox.asaas.com/v3/`. Production credentials and endpoints must remain disabled in this feature branch.

## Documentation
- https://docs.asaas.com/docs/sandbox
- https://docs.asaas.com/reference/create-new-subscription
- https://docs.asaas.com/reference/obter-qr-code-para-pagamentos-via-pix
