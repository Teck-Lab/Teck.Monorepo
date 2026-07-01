# Basket Service — Design

**Date:** 2026-07-01
**Status:** Approved — ready for implementation planning
**Group:** commerce (`commerce@{version}`)
**Reference implementation:** `src/services/commerce/order` (complete vertical slice — mirror it)
**Roadmap context:** `2026-07-01-commerce-platform-service-catalog-design.md` (Tier 0 spine, first service to build)

## Goal

Build the `basket` service — cart management + checkout — as the first new commerce service, mirroring the `order` reference service exactly. Checkout emits `BasketCheckedOut` over RabbitMQ; the `order` service consumes it and creates an `Order`, closing a demonstrable end-to-end cross-service flow. Supports **both anonymous (guest) and authenticated (customer) baskets**, with merge-on-login.

## Non-goals (deferred)

- Consuming `ProductPriceChanged` to re-price basket lines (no `pricing`/`catalog` producer exists yet) — lines carry a snapshot `UnitPrice` captured at add-time.
- Consuming `OrderPlaced` to re-sync/clear the basket post-order (checkout already transitions the basket to `CheckedOut`).
- Promotions/discounts, tax, shipping estimates on the basket (separate Tier 1 services).
- Basket expiry/abandonment jobs (status `Abandoned` exists in the model but no background sweeper yet).

## Architecture conventions (must follow)

Follows every rule in `src/services/AGENTS.md` and `src/services/commerce/AGENTS.md`:
- Clean architecture: `Basket.Domain → Basket.Application → Basket.Host`. Domain references only `SharedKernel.Core`.
- Three-context CQRS: `BasketDbContextBase` (abstract, model once, Application) → `BasketDbContext` (write leaf, Application, migration target) + `BasketReadDbContext` (`NoTracking`, Host).
- Handlers depend on `IGenericReadRepository<T,Guid>` / `IGenericWriteRepository<T,Guid>` / `IUnitOfWork` — **never** a `DbContext`, **never** Ardalis `IRepositoryBase<T>`. Single commit point: `IUnitOfWork.SaveChangesAsync`.
- Query logic in Ardalis `Specification` classes under `Application/Baskets/ReadModels/`. Mapping via Mapperly under `Application/Baskets/Mapping/`. DI via ServiceScan. Config via Options pattern.
- Multi-tenant: `Basket` implements `ITenantScoped`; EF global query filter + SaveChanges interceptor enforce isolation.
- WolverineFx is the mediator + bus. Handlers are static methods with injected deps.

## Domain model (`Basket.Domain`)

### `Basket` — aggregate root
`sealed class Basket : BaseEntity, IAggregateRoot, ITenantScoped`

| Member | Type | Notes |
|---|---|---|
| `CustomerId` | `Guid?` | Null for anonymous baskets |
| `AnonymousToken` | `Guid?` | Opaque identity for guest baskets; null once owned by a customer |
| `TenantId` | `string` | `ITenantScoped` |
| `Status` | `BasketStatus` | `Active` initially |
| `Items` | `List<BasketItem>` | Owned collection (`OwnsMany`) |
| `Subtotal` | `decimal` | Recalculated on every mutation |

**Behavior (all recalc `Subtotal` via `BasketPricingService`):**
- `static Basket CreateForCustomer(Guid customerId, string tenantId)`
- `static Basket CreateAnonymous(Guid anonymousToken, string tenantId)`
- `AddItem(Guid productId, string productName, decimal unitPrice, int quantity)` — merges by `ProductId` (increments quantity if present); validates `quantity > 0`.
- `UpdateItemQuantity(Guid productId, int quantity)` — replaces the item's quantity; `quantity <= 0` removes the line. Throws if product not in basket.
- `RemoveItem(Guid productId)`
- `Clear()`
- `MergeFrom(Basket source)` — absorbs `source.Items` (merge by `ProductId`, sum quantities), sets `source.Status = Merged`. Used on login.
- `AssignToCustomer(Guid customerId)` — sets `CustomerId`, clears `AnonymousToken` (used when a guest logs in and has no prior customer basket).
- `Checkout()` — requires `Status == Active` and non-empty `Items`; sets `Status = CheckedOut`; raises `BasketCheckedOut` domain event. Throws `InvalidOperationException` on empty/invalid basket.

### `BasketItem` — owned value object
`sealed record BasketItem(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity)` with computed `decimal LineTotal => UnitPrice * Quantity`. Mirrors `OrderLine`'s `OwnsMany` pattern.

### `BasketStatus` — SmartEnum
`Active(1)`, `CheckedOut(2)`, `Abandoned(3)`, `Merged(4)`. Mirrors `OrderStatus`.

### `BasketPricingService` — domain service
`static decimal CalculateSubtotal(IEnumerable<BasketItem> items)`. Mirrors `OrderPricingService`.

### `BasketCheckedOut` — domain event
`sealed class BasketCheckedOut : DomainEvent` carrying `BasketId, CustomerId, TenantId, Subtotal, List<BasketItem> Items, DateTimeOffset CheckedOutAt`.

## Application (`Basket.Application`)

### Database
- `BasketDbContextBase(DbContextOptions, IMultiTenantContextAccessor<TenantDetails>)` — `DbSet<Basket>`; `OnModelCreating` applies configurations then `base.OnModelCreating` (Finbuckle owned-entity ordering).
- `BasketDbContext` — write leaf, migration target.
- `Configurations/BasketConfiguration` — `OwnsMany(b => b.Items)`, indexes on `(TenantId, CustomerId, Status)` and `(TenantId, AnonymousToken, Status)` for the get-or-create lookups.

### Capability `Baskets/`
**Features (`Features/{UseCase}/V1/` — Command/Query + Handler each):**
| Use case | Kind | Handler behavior |
|---|---|---|
| `GetCurrentBasket` | query | Resolve identity → return existing `Active` basket, or create one (customer or anonymous). Anonymous create returns the minted token. |
| `AddItem` | command | Load-to-mutate (`enableTracking: true`) → `AddItem` → commit |
| `UpdateItemQuantity` | command | Load-to-mutate → `UpdateItemQuantity` → commit |
| `RemoveItem` | command | Load-to-mutate → `RemoveItem` → commit |
| `ClearBasket` | command | Load-to-mutate → `Clear` → commit |
| `MergeBasket` | command | Authenticated. Load customer's `Active` basket (create if none) + the anonymous basket by token → `MergeFrom` → commit both |
| `Checkout` | command | Load-to-mutate → `Checkout()` (raises domain event) → commit. Domain-event handler publishes the integration event. |

- `Responses/` → `BasketDto`, `BasketItemDto`.
- `ReadModels/` → `BasketByIdSpec`, `ActiveBasketByCustomerSpec`, `ActiveBasketByTokenSpec`.
- `Mapping/BasketMapper` — Mapperly (`ToDto`, collection map).
- `EventHandlers/DomainEvents/BasketCheckedOutHandler` — `Handle(BasketCheckedOut, IMessageBus bus)` publishes `BasketCheckedOutIntegrationEvent`. Mirrors `OrderPlacedHandler`.
- `BasketOptions` (Options pattern) — e.g. `MaxItemsPerBasket` (default 100), `MaxQuantityPerLine` (default 999).

### Identity resolution
- `IBasketIdentityAccessor` (Application interface) exposing `Guid? CustomerId` and `Guid? AnonymousToken`, plus `Guid EnsureAnonymousToken()` (mints one if absent).
- Host implementation `BasketIdentityAccessor` reads the authenticated `customer_id` claim (`IHttpContextAccessor`), else the `X-Basket-Token` request header. Registered via ServiceScan/DI.

## Shared contract (`SharedKernel.Events`)

`BasketCheckedOutIntegrationEvent` — `[MemoryPackable] partial class : IntegrationEvent`, mirroring `OrderPlacedIntegrationEvent` shape:
`BasketId, CustomerId (Guid?), TenantId, Subtotal, List<BasketCheckedOutLine> Items, CheckedOutAt`, where `BasketCheckedOutLine(ProductId, ProductName, UnitPrice, Quantity, LineTotal)`. Constructors: parameterless `[MemoryPackConstructor]`, plus one from the `BasketCheckedOut` domain event. Placed in `SharedKernel.Events` (not `Basket.Application`) so `order` consumes it without referencing `basket`.

## Host (`Basket.Host`)

### Endpoints (`Endpoints/Baskets/` — endpoint + Request + Validator each)
Basket endpoints allow **anonymous** access (guests must use a cart), unlike `order`'s `AuthenticatedEndpoint`. Use an anonymous-capable endpoint base / `AllowAnonymous`; identity is resolved via `IBasketIdentityAccessor`, not an auth requirement. `merge` requires authentication.

| Method + Route | Use case | Auth |
|---|---|---|
| `GET /baskets/current` | `GetCurrentBasket` (returns basket; for guests, response echoes the `X-Basket-Token` to persist) | anonymous ok |
| `POST /baskets/items` | `AddItem` | anonymous ok |
| `PUT /baskets/items/{productId}` | `UpdateItemQuantity` | anonymous ok |
| `DELETE /baskets/items/{productId}` | `RemoveItem` | anonymous ok |
| `POST /baskets/clear` | `ClearBasket` | anonymous ok |
| `POST /baskets/merge` | `MergeBasket` (body: `anonymousToken`) | authenticated |
| `POST /baskets/checkout` | `Checkout` → `201`, `Location: /baskets/{id}` | authenticated |

### Database (`Database/`)
- `BasketReadDbContext` (derives from base, `NoTracking`).
- `BasketWriteRepository<,>` / `BasketReadRepository<,>` (thin generic subclasses).
- `BasketPersistenceExtensions.AddBasketPersistence(builder)` — `AddHybridMultiTenantDbContexts<BasketDbContext, BasketReadDbContext>(...)`, open-generic repo registration, `UnitOfWork<BasketDbContext>` bound to the **scoped** write context. Mirrors `OrderPersistenceExtensions`.
- `BasketDbContextDesignTimeFactory`.
- `Migrations/` → `InitialBasket` (`dotnet ef migrations add`).

### `Program.cs`
Copy of `Order.Host/Program.cs`: `AddServiceDefaults`, `AddTeckService(typeof(Program).Assembly, ...)`, `AddBasketPersistence()`, `AddKeycloak(...)`, `UseWolverine` with `opts.Discovery.IncludeAssembly(typeof(BasketDbContext).Assembly)` + `AddTeckBehaviors()` + `AddTeckDeadLetterPolicy(...)`, `RunTeckServiceAsync(args)` (supports `--migrate`).

### `Directory.Build.props`
Copy of `order/Directory.Build.props`.

## Cross-service loop — Order-side consumer (one edit outside basket)

Add to `Order.Application` a WolverineFx consumer:
`EventHandlers/IntegrationEvents/BasketCheckedOutConsumer` — `static async Task Handle(BasketCheckedOutIntegrationEvent evt, IMessageBus bus)` that maps the event's lines to a `CreateOrderCommand` and invokes it (reusing the existing `CreateOrderHandler`). Requires `Order.Application` to reference the `SharedKernel.Events` contract (already referenced). Tenant flows via the event payload / Wolverine tenant middleware (consumers resolve tenant from the message, not inbound HTTP).

Full flow:
```
POST /baskets/checkout → CheckoutHandler → Basket.Checkout() raises BasketCheckedOut (domain)
  → BasketCheckedOutHandler publishes BasketCheckedOutIntegrationEvent → RabbitMQ
    → Order.BasketCheckedOutConsumer → CreateOrderCommand → Order created → OrderPlaced
```

## Error handling

- Domain guards throw (`InvalidOperationException` / `ArgumentException`) for invalid transitions (checkout empty basket, update missing item, non-positive quantity); endpoint/validation layer returns `400`.
- `GetCurrentBasket` never 404s — it creates on miss (get-or-create).
- `AddItem`/`UpdateItemQuantity`/`RemoveItem`/`Checkout` on a non-`Active` basket → `409 Conflict` (basket already checked out/merged).
- Validators (`FluentValidation`) enforce `quantity` bounds and required fields at the edge.

## Testing (`tests/`)

- `Basket.UnitTests` (`tests/unit/`) — domain rules: `AddItem` merge-by-product, `UpdateItemQuantity` remove-on-zero, `Checkout` empty-basket guard, `MergeFrom` quantity summation + source→`Merged`, `BasketPricingService` subtotal. Naming `Method_WhenCondition_ExpectedResult`, AAA.
- `Basket.IntegrationTests` (`tests/integration/`, Testcontainers) — endpoints against real Postgres; `checkout` publishes `BasketCheckedOutIntegrationEvent` to RabbitMQ; anonymous→customer merge path; tenant isolation.
- `Basket.Architecture.UnitTests` (`tests/architecture/`, ArchUnitNET) — Application does not depend on `DbContext` or `Ardalis.Specification.IRepositoryBase`; Host has no Request/Validator types; Application has no endpoint types.

## nx / build

- Register `basket-api` project targets (build/test/lint/typecheck) consistent with `order-api`.
- WolverineFx codegen pre-generated in Docker/CI before publish (per `deploy/AGENTS.md`).
- Dockerfile via `deploy/Containerfile.template` build args; base K8s manifests under `deploy/basket/base/` (overlays live in Teck.GitOps — not here).
- Migration runs as `--migrate` init container.

## Definition of done

1. `Basket.{Domain,Application,Host}` build clean under analyzers-as-errors; public types documented (StyleCop).
2. All three test projects pass; `nx affected -t build test lint typecheck` green.
3. `BasketCheckedOutIntegrationEvent` in `SharedKernel.Events`; `order`'s `BasketCheckedOutConsumer` creates an order from it (integration-tested).
4. `InitialBasket` migration applies via `--migrate`.
5. Anonymous cart → add items → login/merge → checkout → order created, demonstrated end-to-end.
