# Work Package: `pricing` service

**Group:** commerce · **Tier:** 0 · **Status:** 🆕 new · **Branch:** `worktree-pricing-service`
**Parallelism:** fully independent — consumes no events. **Best first pick** for a fresh session.

> This is a scope brief, not a finished plan. Run the full SDD cycle (brainstorm → spec → plan → implement), mirroring the **basket** build (`docs/superpowers/plans/2026-07-01-basket-service.md`) and the **order** reference service. Read `src/services/AGENTS.md` first. Follow every convention in `COORDINATION.md`.

## Bounded context
Owns **prices**, decoupled from product master data (catalog) and from cart/order math. Commercetools-style: price lists, currencies, and channel/customer-group-specific prices. A product's *price* is resolved here; its *attributes* live in catalog.

## Domain (starting shape — refine in your spec)
- `PriceList` (aggregate root, `ITenantScoped`): name, currency, validity window, channel/customer-group scope.
- `Price` (owned/entity): productId, list membership, amount (money), min-quantity tiers.
- Value objects: `Money` (amount + currency), `PriceScope` (channel, customer group).
- Smart enums: `PriceListStatus` (Draft/Active/Archived).

## Events
- **Emits:** `PriceChanged` (productId, priceListId, new amount, currency, effective date) — **pricing owns this contract** in `SharedKernel.Events`. Consumers: basket (reprice carts), search, catalog display.
- **Consumes:** none.

## API surface (indicative)
- `GET /prices/resolve?productId&channel&customerGroup&quantity` → resolved price (the hot path).
- CRUD for price lists and prices (authenticated, tenant-scoped).

## Dependencies & ordering
Start now. Nothing blocks it. Emitting `PriceChanged` is additive — basket can consume it later.

## Shared-file touchpoints
`.slnx`, `Directory.Packages.props` (probably none new), `AppHost.cs`/`.csproj` (add `pricingdb` + resource), `SharedKernel.Events/PriceChangedIntegrationEvent.cs` (new file). No `nx.json` change (commerce group exists).

## Watch-items
- Money handling: use a `Money` value object with explicit currency; never bare `decimal` across boundaries.
- Price *resolution* is a read-heavy hot path — put the resolution logic in a Specification / dedicated read query, cache-friendly.
- No `IQuery<>` type yet? Then your arch test must skip `QueriesShouldNotModifyState` (see `BasketArchitectureTests`). If you build a real read query as `IQuery<>`, you can call `AssertAll` directly like order.
