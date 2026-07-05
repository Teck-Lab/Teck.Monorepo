# Work Package: `inventory` service

**Group:** commerce · **Tier:** 0 · **Status:** 🆕 new · **Branch:** `worktree-inventory-service`
**Parallelism:** independent — consumes only already-existing event contracts.

> Scope brief, not a finished plan. Run the full SDD cycle, mirroring **basket** and **order**. Read `src/services/AGENTS.md` and `COORDINATION.md` first.

## Bounded context
Owns **stock**: quantity on hand per product per location, reservations, and availability. Split out of the retired `product` placeholder (master data → catalog; stock → here). One product's stock across warehouses/stores lives here.

## Domain (starting shape)
- `StockItem` (aggregate root, `ITenantScoped`): productId, locationId, quantityOnHand, quantityReserved, reorder threshold.
- `Reservation` (entity/owned): orderId or basketId, quantity, status, expiry.
- Smart enums: `ReservationStatus` (Held/Committed/Released/Expired).
- Domain service: availability = onHand − reserved; reserve/release/commit transitions.

## Events
- **Emits:** `StockReserved`, `StockDepleted` — **inventory owns these contracts** in `SharedKernel.Events`.
- **Consumes:** `OrderPlaced` (exists, order-owned) → commit/reserve stock; `BasketCheckedOut` (exists, basket-owned, this branch) → optional soft-reserve at checkout. Both contracts already exist, so a WolverineFx consumer (`...Handler`, not `...Consumer`) can subscribe immediately.

## API surface (indicative)
- `GET /inventory/availability?productId&location` → available quantity.
- Adjust stock, set thresholds (authenticated, tenant-scoped).

## Dependencies & ordering
Start now — both consumed contracts exist. No producer waits on you.

## Shared-file touchpoints
`.slnx`, `AppHost.cs`/`.csproj` (`inventorydb` + resource), `SharedKernel.Events/{StockReserved,StockDepleted}IntegrationEvent.cs` (new files). No `nx.json` change.

## Watch-items
- **Concurrency is the crux**: reservations race. Use optimistic concurrency (row version) on `StockItem`; the `IUnitOfWork` single-commit-point still applies, but expect and handle `DbUpdateConcurrencyException` on reserve.
- Idempotent consumers: `OrderPlaced`/`BasketCheckedOut` may be delivered more than once — key reservations by orderId/basketId so re-delivery is a no-op (mirror basket's guest no-op discipline).
- Availability reads are hot — Specification-backed, `AsNoTracking` read context.
