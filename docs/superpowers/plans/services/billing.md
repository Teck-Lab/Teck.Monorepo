# Work Package: `billing` service

**Group:** operations · **Tier:** 0 · **Status:** 🟢 payments/invoicing core complete (2026-07-16) · **Branch:** `worktree-billing-service`

> **Completed 2026-07-16** (plan `docs/superpowers/plans/2026-07-16-billing-service.md`): first `operations`-group service. `Payment` + `Invoice`(owned `InvoiceLine`) aggregates (`ITenantScoped`), `PaymentStatus` SmartEnum, provider-agnostic `IPaymentProvider` (primitives, stub impl via `IOptions<PaymentProviderOptions>` — no card data), `CapturePayment` CQRS (idempotent by unique `OrderId`, provider capture → invoice → `PaymentCaptured`/`PaymentFailed` after commit), an idempotent `OrderPlaced` consumer (maps → `CapturePaymentCommand` via bus), HTTP endpoints (`POST /payments`, `GET /payments/{id}`, `GET /payments`, `GET /invoices/{id}`), `InitialBilling` migration, Aspire wiring, deploy base (`operations`), and full unit/architecture/integration tests.
> **Deferred (documented):** (1) **RabbitMQ transport is dormant platform-wide** — the `OrderPlaced` consumer is discovered + unit-tested but receives no cross-service messages until transport is wired (see `docs/superpowers/plans/2026-07-16-wolverine-rabbitmq-transport.md`); (2) tenant resolution dormant like all siblings (`TenantId` empty at runtime); (3) provider webhook endpoint; (4) `PaymentMethod` management; (5) refund flow (status enum has `Refunded`, no feature); (6) real payment-provider SDK; (7) `OrderPlaced` carries no currency → billing applies `PaymentProvider:DefaultCurrency` ("USD") until the contract adds one; (8) no `nx.json` release group (follows the `inventory` precedent).
> **Known limitation — concurrent-capture race (harden with the transport task):** the capture handler's idempotency is read-then-act (`FirstOrDefaultAsync(PaymentByOrderSpec)` → provider → `SaveChangesAsync`). Sequential replay is safe (proven by `CapturePayment_SameOrderTwice_IsIdempotent` + the unique `IX_payments_OrderId`), but two *concurrent* captures for the same `OrderId` (true at-least-once redelivery, or parallel POSTs) can both pass the guard: the second `SaveChangesAsync` throws on the unique index (→ unhandled 500) and — with a real provider — the provider is charged twice. Latent today (stub provider + dormant RabbitMQ transport). **Before a real provider + live transport:** catch the unique-constraint violation on save → re-read → return the existing payment (converts the race into the idempotent path), and pass `OrderId` as a provider idempotency key so the provider dedupes the charge. Tracked in `docs/superpowers/plans/2026-07-16-wolverine-rabbitmq-transport.md`. Also note: a `Failed` payment is currently terminal (no retry) — the guard returns any existing payment including a decline; revisit with the refund/retry scope.
**Parallelism:** independent — consumes only the existing `OrderPlaced` contract.

> Scope brief, not a finished plan. Run the full SDD cycle, mirroring **order**/**basket**. Read `src/services/AGENTS.md` and `COORDINATION.md` first. Note this is the **operations** group, not commerce — but the same clean-architecture + conventions apply; do not reference commerce services directly (share via events only).

## Bounded context
Owns **payments, invoicing, and payment methods**: capturing payment for placed orders, issuing invoices, tracking payment state. Integrates external payment providers (behind an abstraction — do not hardcode a provider).

## Domain (starting shape)
- `Payment` (aggregate root, `ITenantScoped`): orderId, amount (`Money`), method, provider ref, status.
- `Invoice` (aggregate root, `ITenantScoped`): orderId, line snapshot, totals, issued date.
- `PaymentMethod` (entity): tokenized method reference (never store raw card data).
- Smart enums: `PaymentStatus` (Pending/Authorized/Captured/Failed/Refunded).

## Events
- **Emits:** `PaymentCaptured`, `PaymentFailed` — **billing owns these contracts** in `SharedKernel.Events`. Consumers: order (fulfilment), loyalty, vendor (payouts).
- **Consumes:** `OrderPlaced` (exists, order-owned) → initiate payment capture. Contract exists, so the consumer (`...Handler`) can subscribe now.

## API surface (indicative)
- `POST /payments` (capture for an order), `GET /invoices/{id}`, payment-method management (authenticated, tenant-scoped).
- Provider webhook endpoint (authenticated/verified) for async capture confirmation.

## Dependencies & ordering
Start now — `OrderPlaced` exists. No producer waits on you.

## Shared-file touchpoints
`.slnx`, `Directory.Packages.props` (likely a payment-provider SDK — **check the version isn't already present**), `AppHost.cs`/`.csproj` (`billingdb` + resource), `SharedKernel.Events/{PaymentCaptured,PaymentFailed}IntegrationEvent.cs` (new). No `nx.json` change (operations group exists).

## Watch-items
- **Never store raw payment credentials** — only provider tokens. Security-review this service carefully.
- Payment capture is externally-observable and hard to reverse — idempotent `OrderPlaced` consumer keyed by orderId; guard against double-capture on event re-delivery.
- Money handling via a `Money` value object with currency, consistent with pricing/order.
- Provider integration behind an interface (Options-pattern config, `IOptions<T>`), never `IConfiguration` in handlers.
