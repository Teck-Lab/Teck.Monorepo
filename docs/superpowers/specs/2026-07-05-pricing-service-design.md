# Pricing Service — Design

**Date:** 2026-07-05
**Status:** Approved — ready for implementation planning
**Group:** commerce (`commerce@{version}`) · **Tier:** 0 (transactional spine)
**Reference implementations:** `src/services/commerce/order` and `src/services/commerce/basket` (mirror them)
**Work-package brief:** `docs/superpowers/plans/services/pricing.md`
**Coordination rules:** `docs/superpowers/plans/services/COORDINATION.md`
**Roadmap context:** `2026-07-01-commerce-platform-service-catalog-design.md`

## Goal

Build the `pricing` service — the authority for **product list prices** and their **currency conversion** — as a fully independent Tier-0 service (consumes no events). It resolves *"what is the list price for product X in context C?"* via a commercetools-style scoped-price-list model with multi-currency FX conversion, and emits `PriceChanged` so downstream services (basket reprice, search, catalog display) can subscribe later. Mirrors the `order`/`basket` reference services exactly.

## Bounded context & boundaries

Owns **prices**, decoupled from product *master data* (`catalog` owns attributes) and from cart/order *math*. A product's *price* is resolved here; its *attributes* live in catalog.

**Explicitly out of this context:**
- **Discounts / promotions** — coupons, cart/product discounts, and the commercetools "discounted price" staging belong to the `promotion` service (Tier 1). Pricing returns the *list* price; promotion decides what to knock off.
- **Tax** — the `tax` service. Prices here are tax-exclusive list prices unless a scope explicitly models tax-inclusive lists (deferred).
- **Catalog master data** — pricing references products only by opaque `ProductId` (Guid); it never stores product names/attributes.

> **Naming note:** `SharedKernel.Core/Pricing/*` (TenantPlan, PricingTier, VolumePricingCalculator, …) is **SaaS tenant-plan/licensing** pricing — unrelated to this commerce service. Do not reuse those types here.

## Architecture conventions (must follow)

Follows every rule in `src/services/AGENTS.md` and `src/services/commerce/AGENTS.md`:
- Clean architecture: `Pricing.Domain → Pricing.Application → Pricing.Host`. Domain references only `SharedKernel.Core`.
- Three-context CQRS: `PricingDbContextBase` (abstract, model once, Application) → `PricingDbContext` (write leaf, Application, migration target) + `PricingReadDbContext` (`NoTracking`, Host).
- Handlers depend on `IGenericReadRepository<T,Guid>` / `IGenericWriteRepository<T,Guid>` / `IUnitOfWork` — **never** a `DbContext`, **never** Ardalis `IRepositoryBase<T>`. Single commit point: `IUnitOfWork.SaveChangesAsync`.
- Query logic in Ardalis `Specification` classes under `Application/{Capability}/ReadModels/`. Mapping via Mapperly under `Application/{Capability}/Mapping/`. DI via ServiceScan. Config via Options pattern (`IOptions<PricingOptions>`).
- Multi-tenant: every aggregate implements `ITenantScoped`; EF global query filter + SaveChanges interceptor enforce isolation; `X-TenantId` propagates.
- WolverineFx is the mediator + bus. Handlers are static methods with injected deps ending in `Handler` (arch test enforces the suffix — do **not** name message consumers `Consumer`).

## Data-modeling decision (resolved)

**Approach A — `Price` is a first-class queryable entity**, not an owned collection of `PriceList`. Rationale: price resolution is a read-heavy hot path keyed by `ProductId`; an owned sub-collection does not index well for product-keyed lookups (the brief calls for a "cache-friendly … dedicated read query"). `Price` carries a `(TenantId, ProductId)` index and an FK to its `PriceList`. Write invariants are still enforced **through the `PriceList` aggregate root** — prices are only added/removed/updated via `PriceList` behavior; the read side queries `Price` joined to `PriceList` scope directly.

## Domain model (`Pricing.Domain`)

### `PriceList` — aggregate root
`sealed class PriceList : BaseEntity, IAggregateRoot, ITenantScoped`

| Member | Type | Notes |
|---|---|---|
| `Name` | `string` | Human label |
| `Description` | `string?` | Optional |
| `TenantId` | `string` | `ITenantScoped` |
| `Status` | `PriceListStatus` | `Draft` initially |
| `Scope` | `PriceScope` | Owned VO: currency (req) + optional country/customerGroup/channel |
| `ValidFrom` | `DateTimeOffset?` | Null = open-started |
| `ValidUntil` | `DateTimeOffset?` | Null = open-ended |
| `Prices` | `IReadOnlyCollection<Price>` | Child entities (FK back to list); mutated only via root behavior |

**Behavior (price-affecting ops raise `PriceChanged`):**
- `static PriceList Create(string name, PriceScope scope, DateTimeOffset? validFrom, DateTimeOffset? validUntil, string tenantId)` — starts `Draft`.
- `UpdateDetails(string name, string? description)` / `UpdateScope(PriceScope scope)` / `UpdateValidity(from, until)` — mutating scope/validity of an `Active` list re-emits `PriceChanged` for all contained products (effective prices moved).
- `Activate()` — `Draft`/`Archived` → `Active`; emits `PriceChanged` (`Upserted`) per contained price.
- `Archive()` — → `Archived`; emits `PriceChanged` (`Removed`) per contained price (no longer resolvable).
- `AddOrUpdatePrice(Guid productId, Money amount, IReadOnlyList<PriceTier> tiers)` — upsert by `ProductId`; validates tiers (see invariants). Emits `PriceChanged` (`Upserted`) **only when the list is `Active`** (a Draft price is not yet effective).
- `RemovePrice(Guid productId)` — throws if absent. Emits `PriceChanged` (`Removed`) **only when the list is `Active`**.

**Emission rule:** `PriceChanged` reflects *effective* price changes only. Mutations on a `Draft` list emit nothing; `Activate()` is what emits the list's prices to consumers, and `Archive()` retracts them.

**Invariants (domain guards → `ArgumentException`/`InvalidOperationException`):**
- `Scope.Currency` required; `amount.Currency` must equal `Scope.Currency` (a list is single-currency).
- `ValidUntil > ValidFrom` when both set.
- Tiers: `MinQuantity >= 1`, strictly ascending and unique `MinQuantity`, each tier `Amount.Currency == Scope.Currency`; a base tier (`MinQuantity == 1`) is implied by the price's base `Money` if no explicit tier at 1.

### `Price` — child entity
`sealed class Price : BaseEntity, ITenantScoped` — `ProductId`, base `Money Amount`, `List<PriceTier> Tiers`, FK `PriceListId`. `TenantId` mirrors the parent (denormalized for the query filter + index). Effective unit amount for a quantity = highest tier with `MinQuantity <= quantity`, else base `Amount`.

### `PriceTier` — value object
`sealed record PriceTier(int MinQuantity, Money Amount)`. Owned collection under `Price`.

### `PriceScope` — value object
`sealed class PriceScope : ValueObject` — `Currency` (ISO 4217, required), `Country?` (ISO 3166-1 alpha-2), `CustomerGroupId?` (Guid), `ChannelId?` (Guid). A null dimension = wildcard (matches any request value). Equality over all four components.

### `Money` — value object
`sealed class Money : ValueObject` — `decimal Amount` (non-negative), `string Currency` (required). Own copy in `Pricing.Domain.ValueObjects`, mirroring `Catalog.Domain.ValueObjects.Money` (services do not share domain types).

### `ExchangeRate` — aggregate root
`sealed class ExchangeRate : BaseEntity, IAggregateRoot, ITenantScoped` — `string FromCurrency`, `string ToCurrency`, `decimal Rate` (> 0), `DateTimeOffset? ValidFrom`, `DateTimeOffset? ValidUntil`. Tenant-managed via CRUD. Behavior: `static Create(...)`, `UpdateRate(decimal)`, `UpdateValidity(from, until)`. Guards: distinct from/to, `Rate > 0`, valid window ordering.

**v1 cardinality:** **at most one rate per `(FromCurrency, ToCurrency)` pair per tenant.** The validity window is optional — null = open (always valid). `SetExchangeRate` upserts by the pair. Multiple historical/overlapping windowed rates for the same pair are **deferred**. Resolution treats a rate as usable only if its window contains `at` (a windowed rate whose window excludes `at` → no usable rate → `422`).

### `PriceListStatus` — SmartEnum
`Draft(1)`, `Active(2)`, `Archived(3)`. Mirrors `OrderStatus`/`BasketStatus`.

### Domain services
- `PriceResolutionService` — pure selection: given candidate `Price`s (+ their list scopes/validity) and a `PriceResolutionContext`, returns the winning `Price` + applied `PriceTier` (or none). Encapsulates the most-specific-match algorithm below.
- `CurrencyConverter` — `Money Convert(Money source, string targetCurrency, ExchangeRate rate)`; applies rounding policy (v1: `MidpointRounding.ToEven` to 2 decimals; per-currency minor units deferred).

### `PriceChanged` — domain event
`sealed class PriceChanged : DomainEvent` carrying `ProductId, PriceListId, TenantId, decimal Amount, string Currency, DateTimeOffset EffectiveFrom, PriceChangeType ChangeType` where `PriceChangeType` ∈ `{ Upserted, Removed }`.

## Price resolution algorithm (the core capability)

**Context:** `productId` (req), `currency` (req), `quantity` (default 1), `country?`, `customerGroupId?`, `channelId?`, `at` (default = now).

1. **Candidates** = all `Price`s for `productId` belonging to **`Active`** `PriceList`s whose validity window contains `at`.
2. **Scope match filter:** keep candidates whose list scope is *compatible* — each non-null scope dimension (`country`/`customerGroup`/`channel`) must equal the corresponding request value; null = wildcard (always compatible). Currency is handled in step 4, not filtered out here.
3. **Most-specific wins:** score each compatible candidate by the count of non-null scope dimensions it matched (channel, customerGroup, country). Highest score wins. **Deterministic tie-break order:** (a) channel-specific over not, (b) customerGroup-specific over not, (c) country-specific over not, (d) lowest amount, (e) earliest `PriceList.CreatedAt`.
4. **Currency / FX:**
   - Partition compatible candidates into **native** (list currency == requested) and **foreign**.
   - If native candidates exist → run steps 2–3 within native only; return the winner's amount as-is.
   - Else → run steps 2–3 across foreign candidates, then **FX-convert** the winner to the requested currency using an `ExchangeRate` (`From = winner.currency`, `To = requested`) valid at `at`. If no such rate → resolution fails with a "no conversion rate" reason.
5. **Tier:** within the winning `Price`, select the tier with the highest `MinQuantity <= quantity`; fall back to the base `Amount`.
6. **Result** (`ResolvedPrice`): resolved `Money`, `PriceListId`, `bool Converted` + rate applied (nullable), `PriceTier` applied (nullable), `ProductId`.

**No candidate after step 3** → "no applicable price". **Foreign winner but no rate** → "no conversion rate". Both surface as structured resolution failures (see Error handling).

## Application (`Pricing.Application`)

### Database
- `PricingDbContextBase(DbContextOptions, IMultiTenantContextAccessor<TenantDetails>)` — `DbSet<PriceList>`, `DbSet<Price>`, `DbSet<ExchangeRate>`; `OnModelCreating` applies configurations then `base.OnModelCreating` (Finbuckle ordering).
- `PricingDbContext` — write leaf, migration target.
- `Configurations/`:
  - `PriceListConfiguration` — owns `PriceScope`; `HasMany(Prices)` with FK; index `(TenantId, Status)`.
  - `PriceConfiguration` — `OwnsMany(Tiers)`; index `(TenantId, ProductId)` (the hot-path key); FK to `PriceList`.
  - `ExchangeRateConfiguration` — index `(TenantId, FromCurrency, ToCurrency)`.

### Capability `Pricing/`
**Features (`Features/{UseCase}/V1/` — Command/Query + Handler each):**

| Use case | Kind | Handler behavior |
|---|---|---|
| `CreatePriceList` | command | Create `Draft` list → commit |
| `UpdatePriceList` | command | Load-to-mutate (details/scope/validity) → commit |
| `ActivatePriceList` | command | Load-to-mutate → `Activate()` → commit |
| `ArchivePriceList` | command | Load-to-mutate → `Archive()` → commit |
| `AddOrUpdatePrice` | command | Load list (tracking) → `AddOrUpdatePrice` → commit |
| `RemovePrice` | command | Load list (tracking) → `RemovePrice` → commit |
| `SetExchangeRate` | command | Upsert `ExchangeRate` by (from,to) → commit |
| `RemoveExchangeRate` | command | Load-to-delete → commit |
| `ResolvePrice` | query (`IQuery<ResolvedPriceDto>`) | Load candidates via `PricesByProductSpec` + active/valid lists + rates → `PriceResolutionService` (+ `CurrencyConverter`) → map |
| `GetPriceList` | query | `PriceListByIdSpec` → map |
| `ListPriceLists` | query | paged list |

- `Responses/` → `PriceListDto`, `PriceDto`, `PriceTierDto`, `ResolvedPriceDto`, `ExchangeRateDto`.
- `ReadModels/` (Ardalis specs) → `PricesByProductSpec`, `ActivePriceListsSpec`, `ExchangeRateSpec`, `PriceListByIdSpec`.
- `Mapping/` (Mapperly) → `PriceListMapper`, `PriceMapper`, `ExchangeRateMapper`, `ResolvedPriceMapper`.
- `EventHandlers/DomainEvents/PriceChangedHandler` — `Handle(PriceChanged, IMessageBus bus)` publishes `PriceChangedIntegrationEvent`. Mirrors `OrderPlacedHandler`/`BasketCheckedOutHandler`.
- `PricingOptions` (Options pattern) — `RoundingMode` (default `ToEven`), `RoundingDecimals` (default 2), `MaxTiersPerPrice` (default 20), `MaxPricesPerList` (default 10000).
- `IExchangeRateProvider` (interface) — `Task<IReadOnlyList<RateSnapshot>> GetRatesAsync(...)`; Host provides a **stub** implementation for now (no external calls). Real ECB/OXR adapter deferred; the seam exists so a background refresh can adopt it later.

## Shared contract (`SharedKernel.Events`)

`PriceChangedIntegrationEvent` — `[MemoryPackable] partial class : IntegrationEvent`, new file (pricing-owned per COORDINATION ownership table). Shape: `ProductId (Guid)`, `PriceListId (Guid)`, `TenantId (string)`, `Amount (decimal)`, `Currency (string)`, `EffectiveFrom (DateTimeOffset)`, `ChangeType (string)`. Parameterless `[MemoryPackConstructor]` + a constructor from the `PriceChanged` domain event. Mirrors `BasketCheckedOutIntegrationEvent`. No consumer today — emission is additive; basket/search/catalog subscribe later without a pricing change.

## Host (`Pricing.Host`)

### Endpoints (`Endpoints/Pricing/` — endpoint + Request + Validator each)
All `AuthenticatedEndpoint`, tenant-scoped (admin CRUD + the resolve hot path; service-to-service callers reach it via the gateway with tenant propagation).

| Method + Route | Use case | Notes |
|---|---|---|
| `GET /prices/resolve` | `ResolvePrice` | Query params: `productId,currency,quantity?,country?,customerGroup?,channel?,at?` → `200 ResolvedPriceDto`; unresolved → `404`; no FX rate → `422` |
| `POST /price-lists` | `CreatePriceList` | `201`, `Location: /price-lists/{id}` |
| `GET /price-lists` | `ListPriceLists` | paged |
| `GET /price-lists/{id}` | `GetPriceList` | `404` if absent |
| `PUT /price-lists/{id}` | `UpdatePriceList` | details/scope/validity |
| `POST /price-lists/{id}/activate` | `ActivatePriceList` | |
| `POST /price-lists/{id}/archive` | `ArchivePriceList` | |
| `PUT /price-lists/{id}/prices/{productId}` | `AddOrUpdatePrice` | body: amount + tiers |
| `DELETE /price-lists/{id}/prices/{productId}` | `RemovePrice` | |
| `PUT /exchange-rates` | `SetExchangeRate` | body: from,to,rate,validity |
| `GET /exchange-rates` | list rates | |
| `DELETE /exchange-rates/{from}/{to}` | `RemoveExchangeRate` | |

### Database (`Database/`)
- `PricingReadDbContext` (derives from base, `NoTracking`).
- `PricingWriteRepository<,>` / `PricingReadRepository<,>` (thin open-generic subclasses).
- `PricingPersistenceExtensions.AddPricingPersistence(builder)` — `AddHybridMultiTenantDbContexts<PricingDbContext, PricingReadDbContext>(...)`, open-generic repo registration, `UnitOfWork<PricingDbContext>` bound to the **scoped** write context. Mirrors `BasketPersistenceExtensions`.
- `PricingDbContextDesignTimeFactory`.
- `Migrations/` → `InitialPricing` (`dotnet ef migrations add`; hand-fix generated `.cs` to file-scoped namespace + trailing commas per COORDINATION gotcha).
- Host `ExchangeRateProviderStub : IExchangeRateProvider` (returns empty/no-op; registered via ServiceScan/DI).

### `Program.cs`
Copy of `Basket.Host/Program.cs`: `AddServiceDefaults`, `AddTeckService(typeof(Program).Assembly, ...)`, `AddPricingPersistence()`, `AddKeycloak(...)`, `UseWolverine` with `opts.Discovery.IncludeAssembly(typeof(PricingDbContext).Assembly)` + `AddTeckBehaviors()` + `AddTeckDeadLetterPolicy(...)`, `RunTeckServiceAsync(args)` (supports `--migrate`). `Directory.Build.props` copied from `basket`.

## Error handling

- Domain guards throw (`ArgumentException`/`InvalidOperationException`) for invalid transitions/invariants (currency mismatch, bad tiers, non-positive rate, bad validity window) → endpoint layer returns `400`.
- `ResolvePrice`: no applicable price → `404`; foreign winner with no conversion rate → `422` with a structured reason. Never silently returns an unconverted foreign price.
- `AddOrUpdatePrice`/`RemovePrice` on an unknown `productId` (remove) → `404`; on an `Archived` list → `409 Conflict`.
- Overlapping validity windows for the same scope are **allowed**; resolution's most-specific + deterministic tie-break resolves them (documented behavior, not an error).
- FluentValidation validators enforce ISO currency/country format, `quantity >= 1`, `rate > 0`, required fields at the edge.

## Testing (`tests/`)

- `Pricing.UnitTests` (`tests/unit/`) — resolution precedence (most-specific wins + each tie-break rung), tier selection (boundary quantities), FX conversion + rounding, validity-window filtering (`at` inside/outside), native-preferred-over-converted, `PriceListStatus` transitions, `Money`/`ExchangeRate`/tier guards. Naming `Method_WhenCondition_ExpectedResult`, AAA.
- `Pricing.IntegrationTests` (`tests/integration/`, Testcontainers) — CRUD + `resolve` against real Postgres; multi-list winner selection; cross-currency resolve via a seeded rate; `PriceChanged` published to RabbitMQ on price upsert/remove/activate/archive; tenant isolation; owned-tier + FK round-trip through real Postgres. Mirror `Basket.IntegrationTests` harness (`AddMultiTenant<TenantDetails>()`, auth-mock tenant claims matching the identity accessor).
- `Pricing.Architecture.UnitTests` (`tests/architecture/`, ArchUnitNET) — Application does not depend on `DbContext` or `Ardalis.Specification.IRepositoryBase`; Host has no Request/Validator-in-Application leakage; Application has no endpoint types. Because `ResolvePrice` is a real `IQuery<>`, call `AssertAll` directly (like `order`) — no need to skip `QueriesShouldNotModifyState`.

## nx / build / deploy

- Register `pricing-api` project targets (build/test/lint/typecheck) consistent with `basket-api`/`order-api`.
- Shared-file touchpoints (additive, per COORDINATION contention map): `Teck.Platform.slnx` (three `<Project>` lines), `Directory.Packages.props` (verify no new versions needed first), `src/aspire/Teck.AppHost/AppHost.cs` + `.csproj` (`pricingdb` resource + project block, mirror the `basket` block), `SharedKernel.Events/PriceChangedIntegrationEvent.cs` (new file). No `nx.json` change (commerce group exists).
- WolverineFx codegen pre-generated in Docker/CI before publish (per `deploy/AGENTS.md`).
- Dockerfile via `deploy/Containerfile.template` build args; base K8s manifests under `deploy/pricing/base/` (overlays live in Teck.GitOps — not here).
- Migration runs as `--migrate` init container.
- **Build in an isolated worktree** `git worktree add .claude/worktrees/pricing-service -b worktree-pricing-service main` (per COORDINATION — one worktree per service; fork from current `main`, which already has the basket `ITenantInfo` fix + shared-file baseline). Land at a PR; CI handles the rest. Never tag or `nx release` from the branch.

## Definition of done

1. `Pricing.{Domain,Application,Host}` build clean under analyzers-as-errors; public types documented (StyleCop).
2. All three test projects pass; `nx affected -t build test lint typecheck` green.
3. `PriceChangedIntegrationEvent` defined in `SharedKernel.Events`; emitted on every effective-price change (integration-tested against RabbitMQ).
4. `InitialPricing` migration applies via `--migrate`.
5. End-to-end demonstrated: create list → add tiered price → activate → `GET /prices/resolve` returns the correct most-specific, correctly-tiered price; a cross-currency resolve returns an FX-converted price using a seeded rate; tenant isolation holds.

## Deferred (YAGNI — revisit later)

- Discounts/promotions and "discounted price" staging (→ `promotion` service).
- Tax-inclusive price lists (→ `tax` service).
- External FX provider (`IExchangeRateProvider` real adapter) + scheduled rate refresh — seam only for now.
- Per-currency minor-unit rounding tables (v1 = 2-dp banker's rounding).
- Bulk price import / external price sync.
- A caching layer for `resolve` (query is index-cheap; add only if profiling warrants).
