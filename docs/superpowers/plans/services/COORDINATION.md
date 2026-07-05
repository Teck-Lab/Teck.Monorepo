# Parallel Service Build — Coordination Map

**Date:** 2026-07-05
**Purpose:** Let multiple sessions (each with its own sub-agents) build different services concurrently without corrupting each other's work. Read this **before** starting any service work-package in this directory.

Companion docs:
- Roadmap / full catalog: `docs/superpowers/specs/2026-07-01-commerce-platform-service-catalog-design.md`
- Reference implementations to mirror: the **order** service (`src/services/commerce/order/`) and the **basket** service (`src/services/commerce/basket/`, built via `docs/superpowers/plans/2026-07-01-basket-service.md`).

---

## The one rule that matters: one worktree per service

Each service is built in its **own git worktree on its own branch**, never in a shared checkout:

```bash
git worktree add .claude/worktrees/<service>-service -b worktree-<service>-service main
```

Two agents editing the same working tree **will** corrupt each other (we hit exactly this — see the basket build's runaway-subagent event). Isolation via worktrees is mandatory, not optional.

### Fork from `main` **after** basket merges

The basket branch lands two things every later service depends on:
1. A **platform bug fix** — `SharedKernel.Infrastructure/.../MultiTenantDbExtensions.cs` now registers `ITenantInfo` (via `AddHybridMultiTenantDbContexts`). Before this fix, any handler injecting `ITenantInfo` 500s at runtime. Fork before basket merges and you inherit the bug.
2. A clean **shared-file baseline** (`.slnx`, `Directory.Packages.props`, `AppHost.cs` already have working entries to copy).

**So: merge basket first, then branch every service from the updated `main`.**

---

## Shared files — the contention map

Every new service touches this handful of root files. Conflicts here are **additive and trivial** to resolve *if* each session appends in the conventional spot. Keep all edits to these on your own branch.

| File | What you add | Conflict risk |
|---|---|---|
| `Teck.Platform.slnx` | one `<Project Path=...>` line per new `.csproj` | Low — additive; resolve by keeping both lines |
| `Directory.Packages.props` | any new central `<PackageVersion>` | Low — additive; **check the version isn't already there** before adding |
| `src/aspire/Teck.AppHost/AppHost.cs` + `Teck.AppHost.csproj` | a `{service}db` + project resource block + project reference (mirror the `basket` block) | Low — additive |
| `SharedKernel.Events/` | your service's integration-event contract, as **new files** (never edit another service's event file) | Very low if you only add files |
| `nx.json` | **only** if you introduce a new release group (`engagement`, `marketplace`) | Medium — Tier 0 services are all in the existing `commerce` group, so Tier 0 needs no `nx.json` change |
| Gateway routing (`src/services/gateway/public/`) | YARP routes, only if the service needs public HTTP | Coordinate if two services touch it same-day |

**Practice:** rebase your branch on `main` right before opening your PR so these additive edits merge cleanly; land PRs one at a time.

---

## Integration-event contract ownership

Cross-service events live in `SharedKernel.Events` so a consumer never references the producing service. **Each event has exactly one owning service** that defines its contract file. Do not define another service's event.

| Event | Owner (defines contract) | Status |
|---|---|---|
| `OrderPlaced`, `OrderShipped` | order | exists (order service) |
| `BasketCheckedOut` | basket | **exists** (`SharedKernel.Events`, this branch) |
| `PriceChanged` | pricing | to build |
| `ProductPriceChanged` / catalog master-data events | catalog | to build |
| `StockReserved`, `StockDepleted` | inventory | to build |
| `CustomerCreated` | customer | to build |
| `PaymentCaptured`, `PaymentFailed` | billing | to build |

If service A consumes service B's event, A only needs the **contract** (already in `SharedKernel.Events` once B defines it) — A never references B's projects. If you need to consume an event whose owner hasn't built it yet, coordinate: either the owner lands the contract file first (a tiny PR), or you stub the contract and the owner adopts it.

---

## Dependency ordering — what can start now

All five remaining Tier-0 services can start **in parallel today**, because each either consumes nothing or consumes an event contract that already exists (`OrderPlaced`, `BasketCheckedOut`):

| Service | Consumes | Blocked by? |
|---|---|---|
| pricing | — | No — fully independent, start anytime |
| customer | — | No — independent (it's the tenant authority) |
| catalog | — | No — independent *(already in progress in `worktree-catalog-service`)* |
| inventory | `OrderPlaced` (exists), `BasketCheckedOut` (exists) | No — both contracts exist |
| billing | `OrderPlaced` (exists) | No — contract exists |

The only ordering constraint is the **fork-after-basket** rule above (for the `ITenantInfo` fix + baseline), not an inter-service one.

---

## How each session works a service

1. `git worktree add` a fresh branch from updated `main` (see above).
2. Read the service's work-package brief in this directory (`<service>.md`).
3. Read the AGENTS.md nearest the code (`src/services/AGENTS.md` is the canonical one) and the **order** + **basket** reference services.
4. Run the full SDD cycle for the service: **brainstorm → spec → plan → implement** (the brief is a starting scope, not a finished plan — expand it into your own spec + TDD plan, mirroring how basket was done).
5. Mirror basket/order for every convention: CQRS three-context split, repository + `IUnitOfWork` single commit point, Ardalis specifications, Mapperly, WolverineFx static handlers, `ITenantScoped` + query filters, FastEndpoints `AuthenticatedEndpoint`, arch tests (**a query-less service must skip `QueriesShouldNotModifyState` — see `CustomerArchitectureTests` / `BasketArchitectureTests`**), Testcontainers integration test, EF migration, Aspire registration.
6. Land at a PR. CI handles the rest. Do not tag or `nx release` from a feature branch.

## Gotchas learned building basket (save yourself the pain)

- **`ITenantInfo` injection now works** (fixed this branch) — but if you fork before basket merges, handlers injecting `ITenantInfo` 500 at runtime with `UnResolvableVariableException`. Unit tests won't catch it (they construct handlers directly); only the integration test will.
- **Arch test `...Handlers_ShouldEndWithHandler`** fails any sealed-static `Handle` class not ending in `Handler` — name message consumers `...Handler`, not `...Consumer`.
- **EF main migration `.cs`** is generated with block namespace / no trailing commas and trips the analyzers — hand-fix to file-scoped namespace + trailing commas every time.
- **Owned-collection key** (`ValueGeneratedOnAdd`) does not clobber caller-supplied non-empty ids (verified for basket), but assert it round-trips through real Postgres in your integration test.
- **Integration test harness** must register `AddMultiTenant<TenantDetails>()` and the auth mock's tenant/customer claims must match what your `IdentityAccessor` reads — mirror `Basket.IntegrationTests` / `Order.IntegrationTests`.
