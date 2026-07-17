# Wolverine → RabbitMQ transport — branch summary

**Branch:** `worktree-wolverine-rabbitmq-transport`. **Plan:** `docs/superpowers/plans/2026-07-16-wolverine-rabbitmq-transport.md` (Tasks 1-6, all done; Task 3b explicitly deferred). This file is PR-body-ready content for the controller's final review + PR against `main`.

## What shipped

Cross-service messaging is now actually turned on. Before this branch, every integration-event handler
(inventory consuming `OrderPlaced`/`BasketCheckedOut`) was discovered by Wolverine but **inert** — no host
attached the RabbitMQ transport, so `bus.PublishAsync(...)` never left the process. This branch:

1. **Added a single shared entry point — `AddTeckMessaging(handlerAssembly, writeConnectionName)`**
   (`SharedKernel.Infrastructure/Hosting/TeckMessagingExtensions.cs`), config-gated:
   - a `rabbitmq` connection string present → the standard runtime (RabbitMQ transport, `AutoProvision`,
     `EnableWolverineControlQueues`, `UseConventionalRouting`, Postgres-backed outbox/inbox);
   - absent → a local-only runtime (durable Postgres-backed local queues, **no** `UseRabbitMq` call), so
     standalone `dotnet run` and single-host tests boot without a broker.
   - Unit-tested gating logic: `TeckMessagingExtensionsTests`, `WolverinePersistenceConfiguratorTests`
     (`SharedKernel.UnitTests`).
2. **Wired every existing commerce host** to `AddTeckMessaging`: order, inventory, basket, catalog,
   pricing, customer (6 hosts, one-line change each). `billing` does not exist on this branch (tracked
   separately as PR #15) and `gateway` does not publish, so neither is wired here.
3. **Proved the transport end-to-end** with `CrossService.IntegrationTests` /
   `OrderPlacedCrossServiceTests`: boots `order` + `inventory` against one shared RabbitMQ + Postgres
   (Testcontainers) and asserts placing an order on `order` genuinely reserves stock in `inventory` —
   i.e. `OrderPlacedIntegrationEvent` crosses the wire, not just an in-process call.
4. **Message-store self-creation.** The `wolverine` schema (outbox/inbox/dead-letter/control tables) now
   self-creates on host startup in **every** environment (`AutoBuildMessageStorageOnStartup =
   AutoCreate.CreateOrUpdate`, previously Development-only, which left production with no way to create
   the schema). Idempotent + advisory-lock-guarded, so concurrent replicas are safe. Verified by
   `MessageStoreSchemaTests` (Inventory.IntegrationTests, broker-backed boot) and a
   `WolverinePersistenceConfiguratorTests` regression guard exercising both `isDevelopment: true/false`.
   Full schema-ownership write-up: `deploy/AGENTS.md` → "Messaging / message-store schema".
5. **Fixed a real boot bug found along the way** (`bbc02f1` fix commit): made the Wolverine transport
   runtime actually boot in a real host (pre-existing wiring gap that would have broken every host the
   moment the transport was attached).

## The Option A decision (event-publishing model)

`ConfigureStandardRuntime` (which this branch turns on) ships with
`PublishDomainEventsFromEntityFrameworkCore<BaseEntity>` — an EF→Wolverine bridge that republishes every
entity **domain** event onto the bus after `SaveChanges`. The codebase does not use that bridge: every
producer (order, inventory, basket, catalog, pricing) already publishes its **integration** events
manually via `bus.PublishAsync(new XIntegrationEvent{...})` inside the command handler, after
`SaveChangesAsync`. Only `order` additionally had a dormant `OrderPlacedHandler` that would republish
`OrderPlacedIntegrationEvent` off the domain event — which, once the bridge is live, would double-publish
(inventory double-reserves stock; billing would double-capture payment) and would leak internal domain
event types onto RabbitMQ exchanges via conventional routing.

**Decision: Option A — manual-publish stays canonical.** The shared runtime does **not** wire
`PublishDomainEventsFromEntityFrameworkCore`; `order`'s redundant `DomainEvents/OrderPlacedHandler` was
deleted (`fc00e91`). This matches what every producer already does, is the smallest change, keeps
internal domain events off the wire, and still gets transactional publishing automatically once the
outbox is on (manual publishes run inside Wolverine's auto-applied outbox transaction). This decision was
made autonomously on-branch while the author was away, per the plan's explicit design-fork note — flagged
here for review confirmation. If review prefers Option B (bridge-canonical), the Task-1
configurator/order changes should be reverted and the larger refactor (converting every producer to
raise-then-bridge-publish) taken instead.

## Standalone / local-only boot — confirmed intact (Task 5)

- **Unit level:** `TeckMessagingExtensionsTests.ShouldUseBroker_*` and
  `WolverinePersistenceConfiguratorTests` assert the gate (absent `rabbitmq` → local-only) and that
  `ConfigureLocalOnlyRuntime` never calls `UseRabbitMq` (source: `ConfigureLocalOnlyRuntime` only calls
  `ConfigureCoreRuntime`, which has no `UseRabbitMq` reference at all — that call exists solely inside
  `ConfigureStandardRuntime`/`ConfigureStatelessRuntime`).
- **Integration level:** every single-host suite's `WebApplicationFactory` (order, inventory, basket,
  catalog, pricing, customer, gateway) boots **without** setting a `rabbitmq`/`RabbitMq` connection
  string, so they all exercise the local-only path and all pass. `CrossService.IntegrationTests` and
  `Inventory.IntegrationTests/MessageStoreSchemaTests` are the only suites that deliberately inject
  `ConnectionStrings:rabbitmq` (they exist specifically to prove the broker-backed path).

## Full affected gate (Task 6)

`bunx nx affected -t build test lint typecheck --base=main --head=HEAD` (lint/typecheck are Biome/tsc
targets inferred only for frontend projects via `@nx/next`/`@nx/expo`/`@nx/storybook`; none of the 41
affected projects on this .NET-only branch have those targets, so effectively `build test` ran for 41
projects / 61 dependent tasks). Result: **every build succeeded (0 errors); 23 of 24 test projects
passed; the sole failure is `Gateway.Public.IntegrationTests` (3/3 tests failing with
`System.InvalidOperationException: The server has not been started or no web application was
configured.`)**. Confirmed pre-existing and unrelated to this branch:

- This branch's diff (`main..HEAD`) touches nothing under `tests/integration/Gateway.Public.IntegrationTests`
  or `src/services/gateway`.
- Re-ran the same test project against a clean checkout of `main` (commit `1451bce`) in an isolated
  worktree: identical failure, same 3 tests, same exception, same stack trace.

## Known deferrals / issues (unchanged by this branch, listed for the PR)

- **Billing (#15) + Task 3b.** `billing` doesn't exist on this branch — it's built separately on
  `worktree-billing-service`/PR #15. Once billing merges to `main` and is wired to `AddTeckMessaging`,
  **Task 3b must land first**: turning on the transport makes concurrent `OrderPlaced` redelivery real,
  which exposes billing's read-then-act idempotency race (two concurrent captures for the same `OrderId`
  both pass the guard; the second `SaveChangesAsync` hits the unique `IX_payments_OrderId` as an unhandled
  500, and a real payment provider could be charged twice). Fix is scoped in the plan (Task 3b): catch the
  unique-constraint violation on save, re-read by `OrderId`, return the existing payment; pass `OrderId` as
  the provider idempotency key.
- **`--migrate` init container is broken (pre-existing, not caused by this branch).** No code handles the
  `--migrate` argument today; JasperFx treats it as an unknown flag and the process exits with code 1
  instead of migrating or no-op'ing. The `wolverine` message-store schema does not depend on this (it
  self-creates on every normal boot per Task 4), but the EF entity-schema migration path for production
  is not actually wired to the K8s init container. Tracked as a follow-up; out of scope here. See
  `deploy/AGENTS.md` → "Messaging / message-store schema", section 2.
- **Dormant middleware `next`-shape bug.** `IdempotencyMiddleware` and `LicenseEnforcementMiddleware`
  still use the invalid ASP.NET-style `next`-delegate shape that was fixed in `TenantPropagationMiddleware`
  during this work. They are inert today (no service registers a Redis `IDatabase` or an
  `ILicenseValidator`), so nothing currently exercises the bug — it will surface the first time a service
  actually uses Redis-backed idempotency or license enforcement. Not fixed here (explicitly out of scope
  per the brief).
- **`customer` is wired but event-less.** The `customer` host now calls `AddTeckMessaging` (transport is
  live for it), but `customer` has no producer or consumer integration-event handlers on `main` today —
  wiring it is forward-looking infrastructure, not a functional change for that service yet.
- **`Gateway.Public.IntegrationTests` pre-existing failure.** See gate section above — 3/3 tests fail with
  "server has not been started" identically on `main`; unrelated to messaging, not touched by this branch,
  not fixed here.

## Commits on this branch (`main..HEAD`, oldest first)

```
189d81b docs(messaging): add wolverine→rabbitmq transport plan
7c70ce3 docs(messaging): flag domain-event-bridge vs manual-publish fork in transport plan
e0e2caf docs(messaging): record Option A decision for event-publishing model
2d73740 feat(messaging): add config-gated AddTeckMessaging host extension
fc00e91 refactor(order): drop redundant OrderPlaced domain-event republisher (Option A)
bbc02f1 fix(messaging): make Wolverine transport runtime boot in a real host
f556596 feat(messaging): wire order + inventory hosts to config-gated transport
5ed157d test(messaging): prove OrderPlaced crosses RabbitMQ from order to inventory
7a03532 feat(messaging): roll out config-gated transport to basket/catalog/pricing
967c34f test(messaging): enable dev message-store schema in wired service integration suites
c834c3b feat(messaging): wire customer host to transport
858c104 fix(messaging): build the wolverine message store on startup in all environments
678a58b docs(deploy): document message-store schema creation vs EF migrations
38fa3dd test(shared-kernel): guard AutoBuildMessageStorageOnStartup unconditionality with a unit test
```

(plus this task's doc-cleanup commit closing out the branch).
