# Catalog Service — Completion Design

**Date:** 2026-07-15
**Status:** Approved (design); pending implementation plan
**Group:** `commerce`
**Location:** `src/services/commerce/catalog/`
**Builds on:** `2026-06-23-catalog-service-design.md` (original design)

## 1. Summary

The `catalog` service is ~70% built. Its **Domain** and **Application** layers are
complete and already conform to the platform's *current* conventions (repository +
`IUnitOfWork`, Mapperly, Ardalis specifications, ServiceScan DI, inline integration-event
publishing). What remains is the **Host HTTP surface, the EF migration, the generated
OpenAPI spec, and test coverage to sibling-service parity** so catalog becomes a complete,
shippable Tier-0 service alongside `order`, `basket`, `pricing`, and `inventory`.

This is a **completion pass, not new architecture.** No domain or application redesign.

### Correction to the original design (§4)

The 2026-06-23 spec §4 still describes the pre-reversal persistence model
("Writes inject `CatalogDbContext`; the DbContext is the unit of work — no `IUnitOfWork`;
Reads inject Ardalis `IRepositoryBase<T>`"). That model was **reversed platform-wide**
(see `2026-06-26-repository-unitofwork-architecture-design.md` and `CLAUDE.md`). The
catalog **code already follows the current model** — handlers depend on
`IGenericWriteRepository<T,TId>` / `IGenericReadRepository<T,TId>` + `IUnitOfWork`, and
`SaveChangesAsync` is the single commit point. This document supersedes §4 of the original
on that point; the rest of the original design stands.

## 2. Current state (what already exists)

- **`Catalog.Domain`** — complete: `Product`, `Variant`, `Category`, `Supplier`,
  `VariantSupplier`, `SupplierPriceHistory`; domain events `ProductCreated`,
  `VariantCreated`, `VariantSellPriceChanged`, `SupplierCostPriceChanged`; value objects
  `Money`, `VariantAttribute`.
- **`Catalog.Application`** — 12 static WolverineFx handlers:
  - Products: `CreateProduct`, `GetProduct`, `ListProducts`, `UpdateSellPrice`,
    `AddVariant`, `CreateCategory`.
  - Suppliers: `CreateSupplier`, `GetSupplier`, `LinkVariantSupplier`,
    `UpdateSupplierCost`, `SetPreferredSupplier`, `GetSupplierPriceHistory`.
  - Plus DTOs/Responses, Mapperly mappers (`ProductMapper`, `SupplierMapper`),
    specifications (`ProductByIdSpec`, `ProductsByCategorySpec`, `ProductByVariantSpec`,
    `SupplierByIdSpec`), `CatalogOptions`, and the three integration events.
  - Integration events are **published inline** in the handlers (matches `basket`; no
    `EventHandlers/` folder): `CreateProduct` → `ProductCreatedIntegrationEvent`,
    `UpdateSellPrice` → `ProductPriceChangedIntegrationEvent` (only on a real change),
    `AddVariant` → `VariantCreatedIntegrationEvent`.
- **`Catalog.Host`** — `CatalogDbContext` (write) + `CatalogReadDbContext` (read),
  `CatalogReadRepository` / `CatalogWriteRepository`, `CatalogPersistenceExtensions`,
  `Program.cs` / `Program.Public.cs`, EF configurations (`ProductConfiguration`,
  `CategoryConfiguration`, `SupplierConfiguration`).
- **Tests** — domain unit tests (`SupplierTests`, `ProductSourcingTests`, `CategoryTests`,
  `ProductCreationTests`, `MoneyTests`) + 3 application handler tests
  (`CreateProductHandlerTests`, `GetSupplierHandlerTests`, `UpdateSellPriceHandlerTests`);
  architecture tests (`CatalogArchitectureTests`).

## 3. The completion gap

| # | Gap | Work |
|---|---|---|
| 1 | **No HTTP endpoints** (largest) | `Catalog.Host/Endpoints/{Products,Suppliers}/` — one FastEndpoint + Request record + `Validator<TRequest>` per handler (~12). Endpoints **only** dispatch via `IMessageBus.InvokeAsync` — no mapping, no business logic. Mirror `basket`'s `Endpoints/Baskets/` structure exactly. |
| 2 | **No EF migration** | `Initial` migration targeting `CatalogDbContext` into `Host/Database/Migrations/`. Verify the model materializes: Product `OwnsMany` Variants → `OwnsMany` VariantSuppliers → `OwnsMany` SupplierPriceHistory; `Money` owned/complex type on `SellPrice` + `CostPrice`; Category self-reference (`ParentId`); Supplier own table; `TenantId` column + global query filter. |
| 3 | **No OpenAPI spec** | `specs/catalog-v1-public.json` auto-generated once endpoints exist; run `nx validate-specs`; regenerate `@teck/api-client` types downstream (`bun run generate`). |
| 4 | **Test coverage 3/12** | Handler tests for the remaining 9 handlers; endpoint/integration tests to `basket`/`pricing` level: create product → query; add variant; link supplier; update cost → history row written; sell-price change → integration event published. |
| 5 | **Verify wiring** | Confirm `UpdateSupplierCost` writes a `SupplierPriceHistory` row and raises `SupplierCostPriceChanged` (domain-internal, not published — buy-side, per original §6); confirm all 12 handlers register and dispatch; confirm Aspire boot and the `--migrate` init-container path run clean. |

## 4. Endpoint surface (from original §5)

| Method | Route | Handler |
|---|---|---|
| POST | `/products` | CreateProduct |
| GET | `/products/{id}` | GetProduct |
| GET | `/products` | ListProducts |
| PUT | `/products/{id}/sell-price` | UpdateSellPrice |
| POST | `/products/{id}/variants` | AddVariant |
| POST | `/categories` | CreateCategory |
| POST | `/suppliers` | CreateSupplier |
| GET | `/suppliers/{id}` | GetSupplier |
| POST | `/variants/{id}/suppliers` | LinkVariantSupplier |
| PUT | `/variants/{id}/suppliers/{supplierId}/cost` | UpdateSupplierCost |
| PUT | `/variants/{id}/suppliers/{supplierId}/preferred` | SetPreferredSupplier |
| GET | `/variants/{id}/suppliers/{supplierId}/history` | GetSupplierPriceHistory |

Exact routes/verbs are finalized during implementation against the existing command/query
shapes; the table is the intended surface, not a contract.

## 5. Decisions

1. **Scope: full parity, shippable.** All 12 handlers get an HTTP surface; migration,
   OpenAPI spec, and tests brought to sibling-service parity. Catalog becomes a complete
   Tier-0 service.
2. **ErrorOr → HTTP status gap: deferred, matches siblings.** Catalog behaves exactly like
   `order`/`basket`/`pricing` today — errored `ErrorOr<T>` returns from a WolverineFx
   handler surface as HTTP 200 with a null body, because Wolverine result-type codegen
   pre-unwraps `ErrorOr<T>` before the endpoint sees it (`InvokeAsync<ErrorOr<T>>` does not
   recover the raw error). A real fix is **platform-wide** (map errored ErrorOr → a
   mappable exception + middleware → 404/409/422 for every service) and is out of scope
   here. Handlers still return the correct `ErrorOr` types, so they are already correct for
   when the platform fix lands. Tracked as a separate platform ticket. See
   `erroror-http-status-platform-gap` and pricing PR #9.
3. **Process: isolated git worktree + subagent-driven implementation.** Every unit ships
   with tests; the EF migration is part of the change. (Standing preference for substantive
   feature work.)
4. **Event wiring: inline publish, as already built.** No `EventHandlers/DomainEvents/`
   folder is introduced — it would diverge from `basket`. The three integration events are
   already published from their handlers; the completion only adds the missing HTTP surface
   in front of them.

## 6. Out of scope

- Platform-wide ErrorOr → ProblemDetails fix (separate ticket; see decision 5.2).
- Inventory seam — `ProductCreated` / `VariantCreated` are already emitted; the consumer is
  the separate `inventory` service. No catalog change needed.
- Supplier cost **integration** events — stay internal (price history + domain event), per
  original §6 (buy-side).
- Full rewrite of the original spec §4 — corrected by note in §1 here, not rewritten.
- Any domain or application-layer redesign.

## 7. Testing (parity target)

- **Domain unit tests** — already present; keep green (preferred-supplier invariant,
  cost-price change writes history + raises event, default-variant creation, deactivation
  cascade).
- **Handler tests** — extend from 3 to all 12 handlers.
- **Endpoint / integration tests** — per `tests/AGENTS.md`: create product → query; add
  variant; create category; create supplier → link to variant → update cost → assert
  history row; sell-price change → assert `ProductPriceChangedIntegrationEvent` published.
- **Architecture tests** — apply automatically (layer direction, no per-entity repos, no
  LINQ in handlers, correct layer placement); keep green.

## 8. Deploy & specs

- `deploy/catalog/base/` per `deploy/Containerfile.template`; image
  `ghcr.io/teck-lab/teck-monorepo/commerce/catalog` (semver or `sha-{hash}`, never
  `latest`); migration init-container via `--migrate`; WolverineFx codegen pre-`dotnet
  publish` in CI. (Verify these exist; create from template if missing.)
- `specs/catalog-v1-public.json` auto-generated (never hand-edited); `nx validate-specs`
  gates backward compatibility; `@teck/api-client` regenerated downstream.
- Environment overlays → Teck.GitOps; infra/Helm → Teck.Terraform. Not in this repo.

## 9. Definition of done

- All 12 endpoints exist, dispatch-only, with request validators; service boots under
  Aspire and serves them.
- `Initial` migration applies cleanly (fresh DB) via `--migrate`; schema matches the model.
- `specs/catalog-v1-public.json` generated; `nx validate-specs` passes.
- Tests: all 12 handlers covered + integration tests above; `nx affected -t build test lint
  typecheck` green; architecture tests green.
- No new analyzer suppressions; signed commits; work isolated in a worktree.
