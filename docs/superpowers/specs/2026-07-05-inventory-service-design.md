# Inventory Service — Design

**Date:** 2026-07-05
**Status:** Approved (buildable — feeds an implementation plan)
**Group:** commerce · **Tier:** 0 · **Branch:** `worktree-inventory-service`
**Work package:** `docs/superpowers/plans/services/inventory.md` · **Coordination:** `docs/superpowers/plans/services/COORDINATION.md`

## Purpose

The `inventory` service owns **stock**: how many units of each product exist at each location, what is reserved against baskets and orders, and what is available to sell. It is split out of the retired `product` placeholder (master data → `catalog`, stock → here). It closes a demonstrable loop with the transactional spine: `OrderPlaced` commits stock and moves availability; `BasketCheckedOut` places a soft hold during checkout.

This is the **full ("C") model**: two-phase reservations with expiry, per-SKU backorders, reorder-triggered events, and multi-location priority allocation. It is deliberately large, so the implementation plan phases it (see *Implementation phasing*) — each phase ships green.

## Scope decisions (from brainstorming)

1. **Full reservation lifecycle** — soft hold at checkout, commit at order, expiry/release, backorders, reorder events, multi-location allocation.
2. **Opaque location + priority allocation** — `locationId` is an opaque `Guid` inventory stores; **no dependency on a `location` service** (which is a future Tier-3 placeholder). A tenant configures an ordered list of locations; allocation fills in priority order; availability sums across locations. Upgrades to geo-aware strategies once `location` exists.
3. **Per-SKU opt-in backorder** — each stock item carries `AllowBackorder`; over-reservation is backordered when on, rejected when off.
4. **Decoupled basket-hold / order-commit** — the `Basket` hold and `Order` commit are independent; `OrderPlaced` reserves+commits fresh, the orphaned hold expires. Inventory takes **no dependency on order's contract**; short hold TTL bounds the transiently-conservative availability during the checkout→order window.
5. **Lazy expiry** — availability/allocation only count holds where `ExpiresAt > now`, so an expired hold stops consuming stock immediately via the query, with no dependency on the platform's still-dormant Wolverine durability. A lightweight periodic sweep persists the release as housekeeping.

## Domain model

Three aggregates, all `ITenantScoped`.

### `StockItem` (aggregate root)
Stock of one product at one location. Natural key: `(TenantId, ProductId, LocationId)`.
- `QuantityOnHand: int` — physical units present.
- `QuantityReserved: int` — units held by active (non-expired `Held` + `Committed`) reservations.
- `AllowBackorder: bool` — per-SKU policy.
- `ReorderThreshold: int` — when `Available` drops to/below this, emit `ReorderTriggered`.
- `RowVersion` — Postgres `xmin` optimistic-concurrency token.
- `Available` (computed) = `QuantityOnHand − QuantityReserved`; may be negative when backorder is allowed.
- Behavior: `Receive(qty)`, `Adjust(delta)`, `Reserve(qty)`, `Release(qty)`, `SetPolicy(allowBackorder, reorderThreshold)`. Mutations raise the domain events below and enforce invariants (e.g. cannot reserve beyond available unless `AllowBackorder`).

### `Reservation` (aggregate root)
One source's claim on stock. **Idempotency key: `(SourceType, SourceId)`.**
- `SourceType: ReservationSource` = `Basket` | `Order`.
- `SourceId: Guid` — basketId or orderId.
- `Status: ReservationStatus`.
- `ExpiresAt: DateTimeOffset?` — set for `Held`; null for `Committed`.
- Owned `ReservationLine` (one per product): `ProductId`, `RequestedQuantity`, `BackorderedQuantity`, and owned `Allocation`s (`LocationId`, `Quantity`).

### `LocationPriority` (aggregate root)
Per-tenant ordered list of `locationId`s that drives allocation order. `TenantId`, ordered `LocationId[]`.

### Smart enums
- `ReservationStatus`: `Held → Committed → Fulfilled`; `Held → Expired`; `Held | Committed → Released`.
- `ReservationSource`: `Basket`, `Order`.

## Reservation lifecycle

- **`BasketCheckedOut` → `Held`** reservation, `ExpiresAt = now + HoldTtl` (config, default e.g. 20 min). Soft-allocates across locations in priority order; increments each `StockItem.QuantityReserved`. Best-effort oversell protection during checkout.
- **`OrderPlaced` → `Committed`** reservation (authoritative), allocated fresh. Decoupled from any basket hold.
- **Expiry (lazy):** availability and allocation ignore holds with `ExpiresAt ≤ now`; a periodic sweep releases them (`Status = Expired`, decrement `Reserved`).
- **Allocation:** fill from the highest-priority location with stock, then next, until `RequestedQuantity` is met. A per-line shortfall with `AllowBackorder` on → `BackorderedQuantity`; off → that line cannot be satisfied. **Reservations are all-or-nothing:** if any line cannot be satisfied (shortfall with backorder off), the *whole* reservation is **rejected** with no state change, and `StockReservationRejected` lists the failing product(s) — inventory never partially commits a source's stock.
- **Idempotency:** a `ReservationBySourceSpec(SourceType, SourceId)` lookup makes re-delivered events no-ops.
- **Backorder fill:** when stock is received/adjusted up, outstanding backordered lines for that product fill in reservation order; `StockReplenished` (back-in-stock) fires.

## Integration events

Owned by inventory, defined as new files in `SharedKernel.Events`:

| Event | Fires when | Consumers |
|---|---|---|
| `StockReserved` | a hold/commit succeeds (carries reservationId, source, allocated lines) | order (fulfillment readiness), analytics |
| `StockReservationRejected` | reserve fails (insufficient, backorder off) | order/basket (surface failure) |
| `StockDepleted` | a stock item's `Available` crosses ≤ 0 | catalog/search (mark OOS), notification |
| `StockReplenished` | stock added back / backorders can fill | notification (back-in-stock), search |
| `ReorderTriggered` | `Available ≤ ReorderThreshold` | future purchasing service (emit-only now) |

**Consumed** (contracts already exist): `OrderPlaced` (order-owned) → commit; `BasketCheckedOut` (basket-owned) → hold. Consumers are WolverineFx static `...Handler` methods.

## API surface

Authenticated, tenant-scoped (FastEndpoints `AuthenticatedEndpoint`). Reservations are **event-driven, not API-driven**.

- `GET /inventory/availability?productId[&locationId]` — hot read: available quantity, summed across locations or per-location. Specification-backed, `AsNoTracking`. Modeled as an `IQuery<AvailabilityDto>`.
- `POST /inventory/stock-items` — register a product@location stock item.
- `POST /inventory/stock-items/{id}/adjust` — **stock intake**: receive (+) / shrinkage (−). Triggers `StockReplenished` / `ReorderTriggered` / backorder fill.
- `PUT /inventory/stock-items/{id}/policy` — set `AllowBackorder`, `ReorderThreshold`.
- `GET /inventory/stock-items?productId` — admin view across locations.
- `PUT /inventory/location-priorities` — set the tenant's allocation order.

## Persistence, concurrency, tenancy

- **CQRS three-context split** (mirrors basket/order): `InventoryDbContextBase` (abstract, Application) → `InventoryDbContext` (write leaf, Application, migration target) + `InventoryReadDbContext` (`AsNoTracking`, Host). Repository generics + `IUnitOfWork` single commit point; `enableTracking: true` on load-to-mutate.
- **Concurrency is the crux.** `StockItem.RowVersion` (`xmin`). A reserve/adjust loads the relevant `StockItem`s (priority-ordered), mutates under tracking, and commits once. On `DbUpdateConcurrencyException`, the handler **reloads and re-allocates**, with bounded retries — this is what prevents oversell when two reservations race the same stock.
- **Specifications** (`Application/{Capability}/ReadModels/`): `StockItemByProductLocationSpec`, `StockItemsByProductSpec`, `AvailabilityByProductSpec` (non-expired holds only), `ReservationBySourceSpec` (idempotency), `ExpiredHeldReservationsSpec` (sweep), `BackorderedLinesByProductSpec`.
- **Mapping:** Mapperly only, `Application/{Capability}/Mapping/`.
- **DI:** ServiceScan; config via Options (`InventoryOptions`: `HoldTtl`, retry bounds, sweep interval).
- **Multi-tenancy:** `ITenantScoped` on all three aggregates; EF global query filter + SaveChanges interceptor; `TenantId` carried on consumed events and stamped on emitted ones. Inherits the `ITenantInfo` DI registration fixed on the basket branch (this branch forks from that `main`).

## Testing strategy

- **Unit** (xunit.v3 + NSubstitute + EF InMemory): allocation across locations in priority order; backorder (on → backordered remainder, off → rejected); reserve/commit/release/expire transitions; reorder threshold crossing; concurrency-retry logic; consumer idempotency (re-delivered `OrderPlaced`/`BasketCheckedOut` no-op).
- **Architecture** (ArchUnitNET, mirrors order): layer direction, no `DbContext`/`IRepositoryBase` in Application, `ITenantScoped` aggregates, `...Handler` naming, `AuthenticatedEndpoint` base. Inventory has real `IQuery<>` types (availability), so it calls the full `SharedArchitectureRules.AssertAll` — unlike basket, which had to skip `QueriesShouldNotModifyState`.
- **Integration** (Testcontainers Postgres + RabbitMQ, mirrors basket/order harness): `OrderPlaced` → commit → availability reflects the decrement; a **concurrent-reserve race → no oversell** (the headline correctness test); backorder fill on replenish; lazy expiry (a past-TTL hold stops counting toward reserved).

## Implementation phasing

The plan sequences these so each phase builds and tests green:

1. **Stock core** — `StockItem` aggregate + persistence + availability query + `adjust`/`policy`/register APIs + `StockDepleted`/`StockReplenished`/`ReorderTriggered` on adjust. No reservations yet.
2. **Reservations + order commit** — `Reservation` aggregate + `OrderPlaced` consumer + priority allocation + optimistic concurrency + `StockReserved`/`StockReservationRejected`.
3. **Holds + expiry** — `BasketCheckedOut` consumer (`Held` + TTL) + lazy expiry + sweep.
4. **Backorder** — per-SKU `AllowBackorder` + backorder fill on replenish + back-in-stock (`StockReplenished`).

Cross-cutting each phase: architecture tests, integration tests, EF migration (backward-compatible), and Aspire registration (`inventorydb` + resource, mirroring the basket block).

## Shared-file touchpoints

`Teck.Platform.slnx` (new projects), `src/aspire/Teck.AppHost/{AppHost.cs,Teck.AppHost.csproj}` (`inventorydb` + resource), `SharedKernel.Events/{StockReserved,StockReservationRejected,StockDepleted,StockReplenished,ReorderTriggered}IntegrationEvent.cs` (new files). No `nx.json` change (commerce group exists). Additive edits only — see `COORDINATION.md`.

## Out of scope (future iterations)

- Geo-aware / split-shipment allocation strategies (needs the `location` service).
- Correlated basket-hold → order-commit conversion (would need `basketId` on `OrderPlaced`).
- Durable Wolverine-scheduled expiry (replaces lazy expiry once platform messaging durability is wired).
- Purchase-order / supplier intake (a future service consuming `ReorderTriggered`); intake here is the manual adjust API.
- Serial/lot tracking, multi-warehouse transfers.
