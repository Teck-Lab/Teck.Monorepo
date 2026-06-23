# Catalog Service — Design

**Date:** 2026-06-23
**Status:** Approved (design); pending implementation plan
**Group:** `commerce`
**Location:** `src/services/commerce/catalog/`

## 1. Summary

A new `catalog` microservice for the Teck commerce group. It owns the **master
product data and sourcing** for the platform: products, variants, categories,
sell prices, suppliers, and supplier cost prices. It is built by mirroring the
reference `order` service and follows every convention in
`src/services/AGENTS.md` and `src/services/commerce/AGENTS.md`.

Two capabilities live inside the one service, with separate folder trees so the
sourcing concern could be lifted out later if procurement ever grows into its
own service:

- **Products** — products, variants, categories, sell pricing.
- **Suppliers** — suppliers, the variant↔supplier link (cost price + sourcing
  metadata), and supplier price history.

### Service boundary: catalog vs. product vs. inventory

The commerce scaffolding documents both a `catalog` and a `product` placeholder
with overlapping descriptions. This design resolves the overlap on a single
axis — **master/sell-side data vs. stock/inventory**:

- **Catalog** (this service) = *what we sell and where we source it.* Products,
  variants, categories, descriptions, sell prices, suppliers, supplier cost
  prices. Slow-changing, read-heavy master data. **Catalog never holds stock
  counts.**
- **`product` placeholder** = reserved for a future standalone **inventory**
  service (likely renamed `inventory` when built) — quantity-on-hand per variant
  per location, reservations, stock movements, replenishment. High write-churn,
  operational; deliberately a separate bounded context with its own DbContext.

Inventory will key off catalog's stable `VariantId`, **consume** catalog's
`ProductCreated`/`VariantCreated` integration events to register SKUs, and
**emit** `StockLevelChanged`/`OutOfStock` events for `basket`/`order`. No
catalog code references inventory; all communication is async via WolverineFx →
RabbitMQ.

## 2. Projects & layering

Three projects, mirroring `order`. Clean-architecture direction enforced by the
ArchUnitNET tests under `tests/architecture/` (they fail the build on violation):

```
src/services/commerce/catalog/
├── Catalog.Domain/        → SharedKernel.Core only
├── Catalog.Application/    → Catalog.Domain + SharedKernel.Core/.Infrastructure
├── Catalog.Host/           → Catalog.Application + SharedKernel.* + ServiceDefaults
├── Directory.Build.props
└── AGENTS.md               (links to parent commerce/AGENTS.md)
```

The `product` placeholder directory stays empty, with a note documenting it as
reserved for the future inventory service.

## 3. Domain model (`Catalog.Domain`)

All entities are tenant-scoped (`ITenantScoped`) and inherit `BaseEntity` (Guid
id, audit fields, soft-delete, domain-event list) from `SharedKernel.Core`.

### Products capability

- **Product** *(aggregate root)* — `Name`, `Description`, `CategoryId?`,
  `IsActive`. Owns its variants. Created with at least one variant; a product
  created without explicit variants gets one **default variant** so sourcing and
  pricing always have a home.
- **Variant** *(entity owned by Product)* — `Sku`, `SellPrice` (Money VO),
  `Attributes` (small owned set of name/value pairs, e.g. size/color),
  `IsActive`. Owns its supplier links.
- **Category** *(aggregate root)* — `Name`, `Slug`, `ParentId?` (self-referencing
  for a simple hierarchy).

### Suppliers capability

- **Supplier** *(aggregate root)* — `Name`, `ContactEmail`, `ContactPhone`,
  `IsActive`.
- **VariantSupplier** *(entity owned by Variant — the link)* — `SupplierId`,
  `CostPrice` (Money VO), `SupplierSku`, `LeadTimeDays`, `MinOrderQuantity`,
  `IsPreferred`.
- **SupplierPriceHistory** *(entity owned by VariantSupplier)* — effective-dated
  rows capturing every change to a link's cost: `CostPrice`, `EffectiveFrom`.

### Value objects

- **Money** — `Amount` (decimal) + `Currency` (ISO code string). Immutable,
  inherits `ValueObject`. Used for both `SellPrice` and `CostPrice`.

### Domain events (in-process; drive integration events / history)

- `ProductCreated` — raised on product creation (carries product + initial
  variants).
- `VariantCreated` — raised when a variant is added to an existing product.
- `VariantSellPriceChanged` — raised when a variant's sell price changes.
- `SupplierCostPriceChanged` — raised when a link's cost price changes; the
  domain logic also appends a `SupplierPriceHistory` row.

### Domain invariants

- **Exactly one preferred supplier per variant** — setting a new preferred
  supplier clears the previous one.
- **Cost-price changes are audited** — changing `VariantSupplier.CostPrice`
  writes a `SupplierPriceHistory` row and raises `SupplierCostPriceChanged`.
- **Deactivation cascades** — deactivating a product deactivates its variants.

## 4. Application layer (`Catalog.Application`)

Folder-per-capability, mirroring `order`'s `Orders/` tree. Handlers are **static
methods** discovered by WolverineFx (no `IRequest`/handler interfaces).

```
Products/
  Features/{CreateProduct,GetProduct,ListProducts,UpdateSellPrice,
            AddVariant,CreateCategory,...}/V1/   (Command/Query + static Handler)
  Responses/    ProductDto, VariantDto, CategoryDto
  ReadModels/   ProductByIdSpec, ProductsByCategorySpec   (Specification<T> / <T,TResult>)
  Mapping/      ProductMapper            ([Mapper], Mapperly)
  IntegrationEvents/  ProductPriceChangedIntegrationEvent,
                      ProductCreatedIntegrationEvent,
                      VariantCreatedIntegrationEvent
Suppliers/
  Features/{CreateSupplier,GetSupplier,LinkVariantSupplier,
            UpdateSupplierCost,SetPreferredSupplier,
            GetSupplierPriceHistory,...}/V1/
  Responses/    SupplierDto, VariantSupplierDto, SupplierPriceHistoryDto
  ReadModels/   SupplierByIdSpec, SuppliersForVariantSpec
  Mapping/      SupplierMapper
EventHandlers/DomainEvents/   (e.g. SupplierCostPriceChangedHandler,
                               ProductCreatedHandler — translate domain →
                               integration events)
Database/  CatalogDbContext   (declared here, implemented in Host, per order convention)
```

Rules (all enforced by convention + architecture tests):

- **Writes** inject `CatalogDbContext`; the DbContext is the unit of work —
  handlers call `SaveChangesAsync()` once, no `IUnitOfWork`.
- **Reads** inject `IRepositoryBase<T>` (Ardalis), backed by
  `CatalogReadDbContext`; queries return `ErrorOr<T>`.
- **No per-entity repositories.** All query logic lives in `Specification`
  classes under `ReadModels/`; no LINQ scattered in handlers.
- **Mapping via Mapperly only**, in `Mapping/`; never hand-written, never in
  endpoints.
- **Config via Options pattern** — `CatalogOptions` (e.g. default currency);
  handlers inject `IOptions<CatalogOptions>`, never `IConfiguration`.

## 5. Host (`Catalog.Host`)

- **CQRS DbContexts:** `CatalogDbContext` (tracked writes, `: BaseDbContext`) and
  `CatalogReadDbContext` (`AsNoTracking`, `: CatalogDbContext`).
- **EF configurations** (`IEntityTypeConfiguration<T>` in `Database/Configurations/`):
  - Product `OwnsMany` Variants; Variant `OwnsMany` VariantSuppliers;
    VariantSupplier `OwnsMany` SupplierPriceHistory.
  - Money mapped as an owned/complex type on `SellPrice` and `CostPrice`.
  - Category self-reference (`ParentId`); Supplier its own table.
  - `TenantId` column + global query filter + SaveChanges interceptor inherited
    from `BaseDbContext`.
- **Endpoints** (FastEndpoints in `Endpoints/`): `/products`, `/products/{id}`,
  `/products/{id}/variants`, `/categories`, `/suppliers`, `/suppliers/{id}`,
  `/variants/{id}/suppliers`, `/variants/{id}/suppliers/{supplierId}/history`,
  etc. Endpoints **only** dispatch via `IMessageBus.InvokeAsync` — no mapping or
  business logic. Request validators are `Validator<TRequest>` in the Host.
- **Migrations** in `Host/Database/Migrations/`; `Program.cs` runs them when
  started with `--migrate` (K8s init container) else `app.Run()`. Migrations
  must be backward-compatible. First migration = `Initial`.

## 6. Messaging & integration events

Catalog publishes three integration events (`[MemoryPackable]` :
`IntegrationEvent`) via `bus.PublishAsync` → RabbitMQ:

| Event | Raised when | Consumed by (v1) |
|---|---|---|
| `ProductPriceChangedIntegrationEvent` | a variant's **sell price** changes | `basket`, `order` |
| `ProductCreatedIntegrationEvent` | a product is created (carries initial variants) | — (future inventory) |
| `VariantCreatedIntegrationEvent` | a variant is added to an existing product | — (future inventory) |

The two lifecycle events are the **inventory seam**: unconsumed in v1
(documented as such), keyed by `VariantId`, so a future inventory service drops
in with zero catalog changes.

Supplier **cost** changes stay internal (price history + `SupplierCostPriceChanged`
domain event); not published, since suppliers are buy-side. Easy to add later.

Catalog consumes **no** inbound integration events in v1. No cross-service
project references.

## 7. Cross-cutting concerns

- **Multi-tenancy:** every entity implements `ITenantScoped`; isolation enforced
  by the inherited EF global query filter + SaveChanges interceptor; `X-TenantId`
  already propagated by the gateway and Wolverine middleware.
- **DI:** ServiceScan.SourceGenerator (compile-time), not Scrutor.
- **Observability:** inherited from `ServiceDefaults` / SharedKernel
  (OpenTelemetry).

## 8. Testing

- **Domain unit tests:** preferred-supplier invariant (setting a new one clears
  the old), cost-price change writes history + raises event, default-variant
  creation, deactivation cascade.
- **Handler / integration tests:** per `tests/AGENTS.md` conventions (create
  product → query, link supplier, update cost → history row, sell-price change →
  integration event published).
- **Architecture tests:** apply automatically (layer direction, no per-entity
  repositories, no LINQ in handlers, correct layer placement).

## 9. Deploy & specs

- `deploy/catalog/base/` created from `deploy/_template/base` with
  `SERVICE_NAME=Catalog`, `GROUP=commerce` (`deployment.yaml`, `service.yaml`,
  `kustomization.yaml`). Built via the shared `deploy/Containerfile.template`;
  image `ghcr.io/teck-lab/teck-monorepo/commerce/catalog` (semver or `sha-{hash}`,
  never `latest`). Migration init-container pattern (`--migrate`). WolverineFx
  codegen run pre-`dotnet publish` in CI
  (`/t:WolverineCodegenWrite /p:RunWolverineCodegen=true`).
- OpenAPI spec auto-generated to `specs/catalog-v1-public.json` (never
  hand-edited); `@teck/api-client` TS types via `bun run generate` downstream;
  `nx validate-specs` checks backward compatibility.
- **Environment overlays and Helm/infra do NOT live in this repo** — base K8s
  manifests only; overlays go to Teck.GitOps, infra to Teck.Terraform.

## 10. Out of scope (v1) / future services

- **Inventory / stock** — future standalone service (the `product` placeholder,
  likely renamed `inventory`). Keys off catalog `VariantId`; consumes
  `ProductCreated`/`VariantCreated`; emits `StockLevelChanged`/`OutOfStock`.
  Catalog never holds stock counts.
- Purchase orders / procurement workflows.
- Multi-currency conversion (Money stores a currency code, but no conversion).
- Supplier cost-change integration events (kept internal for now).
- Catalog consuming any inbound events.
