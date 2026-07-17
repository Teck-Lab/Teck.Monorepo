# Platform Task: Wire the WolverineFx → RabbitMQ Transport

> **Type:** platform-wide messaging enablement (spans every service host). **Status:** 📁 planned. **Branch:** `worktree-wolverine-rabbitmq-transport` (own worktree, own PR — not a per-service change).
> **REQUIRED SUB-SKILL:** superpowers:executing-plans or subagent-driven-development.

**Goal:** Actually turn on cross-service messaging. Today every integration-event handler (`inventory` consuming `OrderPlaced`/`BasketCheckedOut`, and — once built — `billing` consuming `OrderPlaced`) is **discovered but inert**: no host attaches the RabbitMQ transport, so `bus.PublishAsync(...)` never leaves the process and no consumer ever receives a cross-service message. This task attaches the transport + durable Postgres outbox to every service host so published integration events flow producer → RabbitMQ → consumer.

## The gap (verified on `main`)

- `SharedKernel.Infrastructure/Messaging/WolverinePersistenceConfigurator.cs` already implements the whole runtime — `ConfigureStandardRuntime(options, isDev, writeConn, rabbitConn, tenantSource?)`:
  - codegen `TypeLoadMode` (Dynamic in dev / Static in prod),
  - `PersistMessagesWithPostgresql(writeConn, "wolverine")` durable message store (outbox/inbox, schema `wolverine`, `OverrideAutoCreateResources(CreateOrUpdate)`),
  - `UseMemoryPackSerialization()`, `UseEntityFrameworkCoreTransactions()`,
  - `PublishDomainEventsFromEntityFrameworkCore<BaseEntity>(e => e.DomainEvents)` (EF→Wolverine domain-event bridge),
  - `Policies.UseDurableLocalQueues()`, `Policies.AddMiddleware<TenantPropagationMiddleware>()`,
  - `UseRabbitMq(rabbitUri).AutoProvision().EnableWolverineControlQueues().UseConventionalRouting()`.
  - Also present: `ConfigureStatelessRuntime` (rabbit only, no Postgres store) and `NormalizeRabbitConnectionString` (`rabbitmq[s]://` → `amqp[s]://`).
- **But no host calls it.** Every `*.Host/Program.cs` does only:
  ```csharp
  builder.Host.UseWolverine(opts =>
  {
      opts.Discovery.IncludeAssembly(typeof(XDbContext).Assembly);
      opts.AddTeckBehaviors();
      opts.AddTeckDeadLetterPolicy(new DeadLetterOptions());
  });
  ```
  → no transport, no outbox, no domain-event bridge.
- Rabbit is provisioned by Aspire (`AddRabbitMQ("rabbitmq").WithManagementPlugin()` in `AppHost.cs`) and injected to each service via `.WithReference(rabbitmq)`, surfacing as connection string **`rabbitmq`** in host config. Testcontainers integration fixtures already spin up `rabbitmq:3-management`.

## Why it was left off (the constraint the design must respect)

A bare `UseRabbitMq(...)` makes the broker a **hard startup dependency**. Running a single service standalone (`dotnet run`, or a unit-of-service dev loop) without Aspire/broker would then crash on boot. The wiring must be **config-gated**: attach the transport only when a `rabbitmq` connection string is present; otherwise fall back to local-only durable queues so standalone dev and existing single-host integration tests keep working.

## ⚠️ BLOCKING DESIGN FORK (discovered 2026-07-17 during Task 1 groundwork) — decide before implementing

`ConfigureStandardRuntime` (which this task turns on in every host) includes
`PublishDomainEventsFromEntityFrameworkCore<BaseEntity>(e => e.DomainEvents)` — an EF→Wolverine
bridge that publishes every entity **domain** event onto the bus after `SaveChanges`. But the
codebase does **not** currently use that bridge. Verified state on `main`:

- **Every producer** (basket, catalog, inventory, pricing, order) publishes its **integration**
  events with a **manual `bus.PublishAsync(new XIntegrationEvent{...})` inside the command handler**,
  after `SaveChangesAsync`. This is the consistent, working, platform-wide mechanism.
- **Only `order`** *additionally* has `Orders.Application/.../EventHandlers/DomainEvents/OrderPlacedHandler`
  that republishes `OrderPlacedIntegrationEvent` off the `OrderPlaced` **domain** event (raised by
  `Order.Create`). It is inert today because no host wires the bridge.

**The collision:** the instant this task wires the bridge, `order` publishes
`OrderPlacedIntegrationEvent` **twice** (manual handler + domain-event handler) → inventory reserves
stock twice; billing (once merged + live) would capture payment twice. The bridge also broadcasts
every service's *internal* domain-event types onto the bus, which `UseConventionalRouting()` would try
to route to RabbitMQ exchanges named after internal types — leaking domain internals across the wire.

**Two valid resolutions (pick one — this is the platform's canonical event-publishing model):**

- **Option A — manual-publish canonical (smaller, matches the codebase).** Keep the manual
  `bus.PublishAsync` in command handlers. In the shared runtime, **drop**
  `PublishDomainEventsFromEntityFrameworkCore` (or restrict the bridge so it never routes internal
  domain events outward). Delete `order`'s redundant `DomainEvents/OrderPlacedHandler`. Net: one file
  removed, one line removed from the shared runtime; every service already conforms. The manual
  publishes become transactional automatically once the outbox is on (they run inside the Wolverine
  handler's auto-applied outbox transaction).
- **Option B — domain-event-bridge canonical (bigger, arguably "more correct").** Keep the bridge.
  Convert *every* producer to raise a domain event and publish the integration event from a
  `DomainEvents/*Handler` (like order already does), and **remove** all the manual `bus.PublishAsync`
  calls from command handlers. Ensure internal domain events are not conventionally routed to RabbitMQ
  (explicit local-only routing for `IDomainEvent` types). Net: touches all 5 producers + routing config.

**Recommendation: Option A.** It matches what 5 of 5 services already do, is a ~2-file change, keeps
internal domain events off the wire, and still gets transactional publishing via the outbox. Option B
is a larger refactor for a marginal purity gain and expands this task's blast radius across every
producer. **Whichever is chosen, Task 1 must resolve it before wiring any host — otherwise the Task 2
pilot double-reserves.**

## Design

1. **One shared entry point, not per-host divergence.** Add `AddTeckMessaging(this IHostApplicationBuilder builder, Assembly handlerAssembly, string writeConnectionName)` (or fold into the existing `UseWolverine` call via a helper) in `SharedKernel.Infrastructure` so every host calls a single line instead of hand-repeating the block. It:
   - resolves the write connection string (same key the service's persistence uses, e.g. `BillingWrite` → fallback `Default`) and the `rabbitmq` connection string;
   - if `rabbitmq` is present → `ConfigureStandardRuntime(opts, isDev, writeConn, NormalizeRabbitConnectionString(rabbit))`;
   - if absent → local-only path (`ConfigureDatabasePersistence` + `UseDurableLocalQueues` + domain-event bridge, **no** `UseRabbitMq`) so boot succeeds without a broker;
   - always keeps `Discovery.IncludeAssembly(handlerAssembly)`, `AddTeckBehaviors()`, `AddTeckDeadLetterPolicy(...)`.
2. **Message-store creation vs `--migrate`.** The `wolverine` schema is created by Wolverine at startup (`CreateOrUpdate`), not by EF migrations. Confirm this coexists with the `--migrate` init-container pattern (either let Wolverine create its schema on normal boot, or add a startup step in the migrate path). Decide and document; do NOT let two mechanisms fight over the schema.
3. **Conventional routing agreement.** Producer and consumer both use `UseConventionalRouting()` and share the `SharedKernel.Events` contract types, so exchange/queue names line up automatically. No per-event binding config.
4. **At-least-once + idempotency.** RabbitMQ + the durable outbox deliver at-least-once; consumers already guard re-delivery by business key (inventory: reservation-by-source; billing: payment-by-OrderId unique index). Verify every existing integration-event consumer is idempotent before enabling (they are, but re-check).
5. **Tenant propagation.** `TenantPropagationMiddleware` stamps/reads `X-TenantId` on envelopes — already in `ConfigureStandardRuntime`; ensure it's active on both publish and consume.

## Tasks

- [ ] **Task 1 — shared `AddTeckMessaging` extension** in `SharedKernel.Infrastructure/Hosting` (or `/Messaging`): the config-gated wiring above, with the local-only fallback. Unit-test the connection-string resolution + gating logic (present → standard, absent → local-only).
- [ ] **Task 2 — pilot on one producer/consumer pair.** Wire `order` (produces `OrderPlaced`) and `inventory` (consumes it) to `AddTeckMessaging`. Add an integration test that boots both (or uses the Aspire AppHost harness) and asserts an order placed on `order` results in inventory reserving stock — i.e. the event actually crosses the wire. This proves the transport end-to-end before rollout.
- [ ] **Task 3 — roll out to all remaining hosts** (`basket`, `catalog`, `pricing`, `customer`, `billing`, gateway if it publishes) — one-line change each; build + each service's integration suite green.
- [ ] **Task 3b — harden billing's capture against concurrency (prerequisite for billing going live).** Turning on the transport makes concurrent `OrderPlaced` redelivery real, which exposes billing's read-then-act idempotency race (documented in `services/billing.md`): two concurrent captures for the same `OrderId` both pass the guard → the second `SaveChangesAsync` hits the unique `IX_payments_OrderId` (unhandled 500) and a real provider is charged twice. Before enabling billing's consumer end-to-end: (a) in `CapturePaymentHandler`, catch the unique-constraint violation (`EntityFramework.Exceptions` `UniqueConstraintException` — `BaseDbContext` already calls `UseExceptionProcessor()`) on save → re-read by `OrderId` → return the existing payment; (b) pass `OrderId` as a provider idempotency key so the real provider dedupes. Add a concurrency test now that this is exercisable with the real transport.
- [ ] **Task 4 — message-store / migrate reconciliation.** Verify the `wolverine` schema is created correctly under both normal boot and the `--migrate` init-container flow on a fresh DB; document the outcome in `deploy/AGENTS.md`.
- [ ] **Task 5 — standalone-dev + single-host-test regression check.** Confirm every service still boots with NO `rabbitmq` connection string (local-only fallback) so `dotnet run` and existing single-host integration tests are unaffected.
- [ ] **Task 6 — full gate + docs.** `nx affected -t build test`; update `CLAUDE.md`/`src/services/AGENTS.md` messaging notes (remove "transport not wired platform-wide" caveats); note that `billing`/`inventory` consumers are now live. PR against `main`.

## Risks / watch-items

- **Broker as hard dependency** — mitigated by the config gate (Task 1 + Task 5).
- **Schema ownership** — Wolverine `CreateOrUpdate` vs EF `--migrate` (Task 4).
- **Duplicate delivery** — consumers must stay idempotent (Task 4 pre-check).
- **Poison messages** — `AddTeckDeadLetterPolicy` already provides retry→error-queue; verify it engages once the real transport is on.
- **Ordering** — conventional routing is per-type; do not assume cross-type ordering. Consumers are already written order-independent.

## Relationship to the billing service

`billing`'s `OrderPlacedHandler` (built in `docs/superpowers/plans/2026-07-16-billing-service.md`, Task 7) is written and unit-tested but cross-service-inert until THIS task lands. After this task, billing auto-captures payment on `OrderPlaced` with no billing code change — that's the payoff of building the consumer now and deferring transport to here.
