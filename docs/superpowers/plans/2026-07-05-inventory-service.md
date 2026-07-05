# Inventory Service Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the multi-tenant `inventory` service — stock per product@location, two-phase reservations with lazy expiry, per-SKU backorders, reorder events, and priority allocation — closing the `OrderPlaced`/`BasketCheckedOut` → stock loop.

**Architecture:** Clean architecture (Domain → Application → Host) mirroring the committed `order` and `basket` services. CQRS three-context split; repository + `IUnitOfWork` single commit; WolverineFx static handlers; Mapperly; Ardalis specifications; Finbuckle multi-tenancy; FastEndpoints; integration-event contracts in `SharedKernel.Events`. Reservation correctness rests on `StockItem` optimistic concurrency (`xmin`) with reload-retry.

**Tech Stack:** .NET 10, EF Core (Postgres), WolverineFx, Mapperly, Ardalis.Specification + SmartEnum, Finbuckle, FastEndpoints, xunit.v3 + NSubstitute + EF InMemory (unit), ArchUnitNET (architecture), Testcontainers (integration).

## Global Constraints

- **Reference services:** mirror `src/services/commerce/basket/**` and `src/services/commerce/order/**` for every convention. Read `src/services/AGENTS.md` and `docs/superpowers/plans/services/COORDINATION.md` first.
- **Namespaces are PLURAL:** csproj `Inventory.Domain.csproj` but `RootNamespace` = `Inventories.Domain`; likewise `Inventories.Application`, `Inventories.Host`. (Mirrors `Basket.*.csproj` → `Baskets.*`.)
- **Repository + UoW:** handlers inject `IGenericReadRepository<T,Guid>` / `IGenericWriteRepository<T,Guid>` + `IUnitOfWork` (`SharedKernel.Core.Database`). Never a concrete `DbContext`, never Ardalis `IRepositoryBase`. `IUnitOfWork.SaveChangesAsync` is the single commit point; load-to-mutate uses `enableTracking: true`.
- **CQRS three-context split:** `InventoryDbContextBase` (abstract, Application) → `InventoryDbContext` (write leaf, Application, migration target) + `InventoryReadDbContext` (`AsNoTracking`, Host).
- **Query logic in Ardalis `Specification` classes** under `Application/Inventory/ReadModels/`, never LINQ in handlers.
- **Mapping:** Mapperly only, `Application/Inventory/Mapping/`. Never map in endpoints.
- **Messaging:** publish integration events **directly** from the command/consumer handler after commit (mirror `order`'s `CreateOrderHandler` and `basket`'s `CheckoutHandler` — the EF→Wolverine domain-event bridge is NOT wired platform-wide). `IMessageBus.PublishAsync(evt)` takes no CancellationToken; `InvokeAsync<T>(msg, ct)` does.
- **Consumers** are WolverineFx static classes whose name ends in `Handler` (NOT `Consumer` — the arch test `...Handlers_ShouldEndWithHandler` fails otherwise).
- **Multi-tenancy:** every aggregate implements `ITenantScoped`; EF global query filter + SaveChanges interceptor enforce isolation; `TenantId` carried on consumed events and stamped on emitted ones. (The `ITenantInfo` DI registration is already fixed on this `main`.)
- **Idempotency:** consumers look up `ReservationBySourceSpec(SourceType, SourceId)` and no-op on re-delivery.
- **Analyzers-as-errors:** every PUBLIC type/member needs an XML `<summary>`; file-scoped namespaces; one type per file; ordered usings. EF's generated migration `.cs` needs manual fixup to file-scoped namespace + trailing commas.
- **Commits MUST be GPG-signed** (never `--no-gpg-sign`; stop and surface if signing fails). End every commit body with:
  `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`
- **Never** create git tags or run `nx release` from this branch.
- Work from the worktree `/workspaces/Teck.Monorepo/.claude/worktrees/inventory-service` (branch `worktree-inventory-service`, forked from updated `main`).

## File structure

```
src/services/commerce/inventory/
  Directory.Build.props                         # mirror basket's (RootNamespace prefix Inventories)
  Inventory.Domain/
    Inventory.Domain.csproj
    Entities/StockItem.cs                        # aggregate root
    Entities/Reservation.cs                      # aggregate root
    Entities/LocationPriority.cs                 # aggregate root
    ValueObjects/ReservationLine.cs              # owned by Reservation
    ValueObjects/Allocation.cs                   # owned by ReservationLine
    ValueObjects/ReservationStatus.cs            # SmartEnum
    ValueObjects/ReservationSource.cs            # SmartEnum
    Services/StockAllocator.cs                   # pure priority-allocation domain service
  Inventory.Application/
    Inventory.Application.csproj
    InventoryOptions.cs                          # HoldTtl, MaxReserveRetries, SweepInterval
    Database/InventoryDbContextBase.cs
    Database/InventoryDbContext.cs
    Database/Configurations/{StockItem,Reservation,LocationPriority}Configuration.cs
    Inventory/Responses/{StockItemDto,AvailabilityDto,ReservationDto}.cs
    Inventory/Mapping/{StockItem,Reservation}Mapper.cs
    Inventory/ReadModels/*Spec.cs
    Inventory/Features/RegisterStockItem/V1/{RegisterStockItemCommand,RegisterStockItemHandler}.cs
    Inventory/Features/AdjustStock/V1/{AdjustStockCommand,AdjustStockHandler}.cs
    Inventory/Features/SetPolicy/V1/{SetPolicyCommand,SetPolicyHandler}.cs
    Inventory/Features/SetLocationPriorities/V1/{...}.cs
    Inventory/Features/GetAvailability/V1/{GetAvailabilityQuery,GetAvailabilityHandler}.cs
    Inventory/Features/ExpireHeldReservations/V1/{ExpireHeldReservationsCommand,ExpireHeldReservationsHandler}.cs
    Inventory/EventHandlers/IntegrationEvents/OrderPlacedHandler.cs     # commit
    Inventory/EventHandlers/IntegrationEvents/BasketCheckedOutHandler.cs # hold
  Inventory.Host/
    Inventory.Host.csproj
    Program.cs, Program.Public.cs, appsettings*.json
    Database/InventoryReadDbContext.cs
    Database/{InventoryReadRepository,InventoryWriteRepository}.cs
    Database/InventoryPersistenceExtensions.cs
    Database/InventoryDbContextDesignTimeFactory.cs
    Database/Migrations/*
    Endpoints/Inventory/*                         # 6 endpoints + requests + validators
    Infrastructure/ReservationExpirySweepService.cs  # IHostedService -> InvokeAsync(ExpireHeldReservationsCommand)
src/shared/SharedKernel.Events/
    {StockReserved,StockReservationRejected,StockDepleted,StockReplenished,ReorderTriggered}IntegrationEvent.cs (+ line types)
tests/unit/Inventory.UnitTests/
tests/architecture/Inventory.Architecture.UnitTests/
tests/integration/Inventory.IntegrationTests/
```

---

# PHASE 1 — Stock core (no reservations)

Ships: stock items exist, can be registered/adjusted, availability is queryable, and adjustments emit depletion/replenish/reorder events.

### Task 1: Scaffold Domain/Application/Host projects + solution wiring

**Files:** create the three `.csproj` + `Directory.Build.props` under `src/services/commerce/inventory/`; register all three in `Teck.Platform.slnx`.

**Interfaces:** Produces the `Inventories.Domain` / `Inventories.Application` / `Inventories.Host` assemblies.

- [ ] **Step 1:** Copy the structure of `src/services/commerce/basket/Basket.Domain/Basket.Domain.csproj`, `Basket.Application/Basket.Application.csproj`, `Basket.Host/Basket.Host.csproj`, and `Basket.Host`'s `Directory.Build.props`, swapping `Basket`→`Inventory` in file names and `Baskets`→`Inventories` in `RootNamespace`/project references. Read each basket file and reproduce it with those swaps (package refs, project refs, `RootNamespace`).
- [ ] **Step 2:** Add three `<Project Path=...>` lines to `Teck.Platform.slnx` next to the basket entries.
- [ ] **Step 3:** Run `dotnet build src/services/commerce/inventory/Inventory.Host/Inventory.Host.csproj`. Expected: succeeds (empty projects, 0 warnings).
- [ ] **Step 4:** Commit `feat(inventory): scaffold Domain/Application/Host projects`.

### Task 2: `ReservationStatus` + `ReservationSource` SmartEnums

**Files:** Create `Inventory.Domain/ValueObjects/ReservationStatus.cs`, `ReservationSource.cs`. Test: `tests/unit/Inventory.UnitTests/ReservationEnumsTests.cs` (create the test project first by mirroring `Basket.UnitTests.csproj`).

**Interfaces:** Produces `ReservationStatus` with static members `Held, Committed, Fulfilled, Released, Expired` and a bool `IsActive` (`Held` or `Committed`); `ReservationSource` with `Basket, Order`.

- [ ] **Step 1: Write failing test** — assert `ReservationStatus.Held.IsActive` is true, `ReservationStatus.Expired.IsActive` is false, and `ReservationStatus.FromName("Committed") == ReservationStatus.Committed`. Mirror the SmartEnum pattern in `basket`'s `BasketStatus.cs` (read it).

```csharp
using Inventories.Domain.ValueObjects;
using Xunit;

namespace Inventories.UnitTests;

public sealed class ReservationEnumsTests
{
    [Fact]
    public void ActiveStatuses_AreHeldAndCommitted()
    {
        Assert.True(ReservationStatus.Held.IsActive);
        Assert.True(ReservationStatus.Committed.IsActive);
        Assert.False(ReservationStatus.Expired.IsActive);
        Assert.False(ReservationStatus.Released.IsActive);
        Assert.False(ReservationStatus.Fulfilled.IsActive);
    }
}
```

- [ ] **Step 2:** Run `dotnet test tests/unit/Inventory.UnitTests/Inventory.UnitTests.csproj --filter ReservationEnumsTests`. Expected: FAIL (types don't exist).
- [ ] **Step 3:** Implement both SmartEnums mirroring `BasketStatus.cs` (Ardalis.SmartEnum base, `sealed`, private ctor, static instances, XML docs). `IsActive => this == Held || this == Committed`.
- [ ] **Step 4:** Run the test. Expected: PASS.
- [ ] **Step 5:** Commit `feat(inventory): add reservation status and source smart enums`.

### Task 3: `StockItem` aggregate

**Files:** Create `Inventory.Domain/Entities/StockItem.cs`. Test: `tests/unit/Inventory.UnitTests/StockItemTests.cs`.

**Interfaces:** Produces `StockItem : BaseEntity, IAggregateRoot, ITenantScoped` with:
- static `Create(Guid productId, Guid locationId, string tenantId, int quantityOnHand, bool allowBackorder, int reorderThreshold) : StockItem`
- props (private set): `Guid ProductId`, `Guid LocationId`, `string TenantId`, `int QuantityOnHand`, `int QuantityReserved`, `bool AllowBackorder`, `int ReorderThreshold`, `uint RowVersion`
- computed `int Available => QuantityOnHand - QuantityReserved`
- `void Receive(int quantity)` (quantity>0 → QuantityOnHand += quantity), `void Adjust(int delta)` (QuantityOnHand += delta; guards non-negative onHand), `void Reserve(int quantity)` (guards: `quantity <= Available || AllowBackorder`; QuantityReserved += quantity), `void Release(int quantity)` (QuantityReserved -= quantity, clamp ≥ 0), `void SetPolicy(bool allowBackorder, int reorderThreshold)`.
- `bool CrossedReorderThreshold()` → `Available <= ReorderThreshold`; `bool IsDepleted()` → `Available <= 0`.

- [ ] **Step 1: Write failing tests** covering: `Create` sets fields and `Available == onHand`; `Reserve` beyond available throws `InvalidOperationException` when `AllowBackorder==false`; `Reserve` beyond available succeeds and drives `Available` negative when `AllowBackorder==true`; `Release` clamps at 0; `Adjust` to negative onHand throws; `IsDepleted`/`CrossedReorderThreshold` boundaries.

```csharp
using Inventories.Domain.Entities;
using Xunit;

namespace Inventories.UnitTests;

public sealed class StockItemTests
{
    private static StockItem New(int onHand, bool backorder = false, int reorder = 0) =>
        StockItem.Create(Guid.NewGuid(), Guid.NewGuid(), "t1", onHand, backorder, reorder);

    [Fact]
    public void Reserve_BeyondAvailable_WithoutBackorder_Throws()
    {
        var item = New(5);
        Assert.Throws<InvalidOperationException>(() => item.Reserve(6));
    }

    [Fact]
    public void Reserve_BeyondAvailable_WithBackorder_GoesNegative()
    {
        var item = New(5, backorder: true);
        item.Reserve(8);
        Assert.Equal(-3, item.Available);
        Assert.Equal(8, item.QuantityReserved);
    }

    [Fact]
    public void Release_ClampsAtZero()
    {
        var item = New(5);
        item.Reserve(2);
        item.Release(5);
        Assert.Equal(0, item.QuantityReserved);
    }

    [Fact]
    public void IsDepleted_WhenAvailableReachesZero()
    {
        var item = New(2);
        item.Reserve(2);
        Assert.True(item.IsDepleted());
    }
}
```

- [ ] **Step 2:** Run `dotnet test ... --filter StockItemTests`. Expected: FAIL.
- [ ] **Step 3:** Implement `StockItem` (mirror `basket`'s `Basket.cs` aggregate style: `sealed`, `BaseEntity`, private setters, static `Create`, guarded mutators, XML docs). `RowVersion` is a `public uint RowVersion { get; private set; }` mapped to `xmin` in configuration (Task 5).
- [ ] **Step 4:** Run the test. Expected: PASS.
- [ ] **Step 5:** Commit `feat(inventory): add StockItem aggregate`.

### Task 4: Integration-event contracts (5 events) in `SharedKernel.Events`

**Files:** Create `StockReservedIntegrationEvent.cs`, `StockReservationRejectedIntegrationEvent.cs`, `StockDepletedIntegrationEvent.cs`, `StockReplenishedIntegrationEvent.cs`, `ReorderTriggeredIntegrationEvent.cs` (+ a `StockReservationLine` record for the reserved/rejected events) in `src/shared/SharedKernel.Events/`.

**Interfaces:** Produces MemoryPack-serializable records mirroring `BasketCheckedOutIntegrationEvent` (read it). Each carries `TenantId`. Field sets:
- `StockDepleted` / `StockReplenished` / `ReorderTriggered`: `Guid ProductId, Guid LocationId, string TenantId, int Available` (+ `int ReorderThreshold` for `ReorderTriggered`).
- `StockReserved` / `StockReservationRejected`: `Guid ReservationId, string SourceType, Guid SourceId, string TenantId, IReadOnlyList<StockReservationLine> Lines`. `StockReservationLine`: `Guid ProductId, int RequestedQuantity, int BackorderedQuantity`.

- [ ] **Step 1:** Read `src/shared/SharedKernel.Events/BasketCheckedOutIntegrationEvent.cs` + `BasketCheckedOutLine.cs` for the exact MemoryPack attribute + XML-doc pattern.
- [ ] **Step 2:** Create the five event files + `StockReservationLine.cs`, one type per file, full XML docs, mirroring that pattern.
- [ ] **Step 3:** Run `dotnet build src/shared/SharedKernel.Events/SharedKernel.Events.csproj`. Expected: succeeds.
- [ ] **Step 4:** Commit `feat(events): add inventory stock integration event contracts`.

### Task 5: Persistence — DbContexts, configurations, repositories, DI

**Files:** Create `InventoryDbContextBase.cs`, `InventoryDbContext.cs`, `Configurations/StockItemConfiguration.cs` (Phase 1 only maps `StockItem`); Host `InventoryReadDbContext.cs`, `InventoryReadRepository.cs`, `InventoryWriteRepository.cs`, `InventoryPersistenceExtensions.cs`, `InventoryDbContextDesignTimeFactory.cs`. Test: `tests/unit/Inventory.UnitTests/InventoryDbContextTests.cs`.

**Interfaces:** Produces `InventoryDbContext` (write) with `DbSet<StockItem>`; `InventoryReadDbContext` (NoTracking). `StockItemConfiguration` maps the `(TenantId, ProductId, LocationId)` unique index and the `xmin` concurrency token.

- [ ] **Step 1: Write failing test** — construct `InventoryDbContext` on EF InMemory (mirror `basket`'s `BasketDbContextTests.cs`), add a `StockItem`, save, reload, assert fields round-trip.
- [ ] **Step 2:** Run it. Expected: FAIL (contexts don't exist).
- [ ] **Step 3:** Implement the contexts/repos/persistence extension by mirroring `basket`'s equivalents (`Basket.Application/Database/*`, `Basket.Host/Database/*`) swapping `Basket`→`Inventory`/`Baskets`→`Inventories`. In `StockItemConfiguration`: `builder.HasIndex(s => new { s.TenantId, s.ProductId, s.LocationId }).IsUnique();` and `builder.Property(s => s.RowVersion).IsRowVersion();` (maps to `xmin` on Postgres — verify against how order/basket map concurrency if present; if not present in refs, use `.HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken()`).
- [ ] **Step 4:** Run the test. Expected: PASS. Then `dotnet build` the Host. Expected: 0/0.
- [ ] **Step 5:** Commit `feat(inventory): add persistence contexts, StockItem config, repositories, DI`.

### Task 6: `StockItemDto` + `AvailabilityDto` + Mapperly mapper

**Files:** Create `Inventory/Responses/StockItemDto.cs`, `AvailabilityDto.cs`, `Inventory/Mapping/StockItemMapper.cs`. Test: `tests/unit/Inventory.UnitTests/StockItemMapperTests.cs`.

**Interfaces:** `StockItemDto(Guid Id, Guid ProductId, Guid LocationId, int OnHand, int Reserved, int Available, bool AllowBackorder, int ReorderThreshold)`; `AvailabilityDto(Guid ProductId, int Available, IReadOnlyList<LocationAvailabilityDto> ByLocation)` with `LocationAvailabilityDto(Guid LocationId, int Available)`. `StockItemMapper.ToDto(StockItem)`.

- [ ] **Step 1:** Failing test asserting `StockItemMapper.ToDto(item).Available == item.Available` (mirror `basket`'s `BasketMapperTests.cs`).
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3:** Implement DTOs + `[Mapper]` static partial (mirror `BasketMapper`).
- [ ] **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit `feat(inventory): add stock DTOs and Mapperly mapper`.

### Task 7: Specifications

**Files:** Create `ReadModels/StockItemByProductLocationSpec.cs`, `StockItemsByProductSpec.cs`, `AvailabilityByProductSpec.cs`. Test: `tests/unit/Inventory.UnitTests/StockSpecsTests.cs`.

**Interfaces:** `StockItemByProductLocationSpec(Guid productId, Guid locationId)` (single); `StockItemsByProductSpec(Guid productId)` (all locations, ordered by location priority is applied later); `AvailabilityByProductSpec(Guid productId)` (all locations for summing).

- [ ] **Step 1:** Failing test constructing each spec and asserting the query filter matches expected items in an in-memory list (mirror `basket`'s spec tests).
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement mirroring `basket`'s `ReadModels/*Spec.cs`. **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit `feat(inventory): add stock specifications`.

### Task 8: `RegisterStockItem`, `AdjustStock`, `SetPolicy` command handlers (+ events on adjust)

**Files:** Create the three `Features/{RegisterStockItem,AdjustStock,SetPolicy}/V1/*Command|Handler.cs`. Test: `tests/unit/Inventory.UnitTests/AdjustStockHandlerTests.cs`.

**Interfaces:**
- `RegisterStockItemCommand(Guid ProductId, Guid LocationId, int QuantityOnHand, bool AllowBackorder, int ReorderThreshold) : ICommand<StockItemDto>`
- `AdjustStockCommand(Guid StockItemId, int Delta) : ICommand<StockItemDto>`
- `SetPolicyCommand(Guid StockItemId, bool AllowBackorder, int ReorderThreshold) : ICommand<StockItemDto>`
- Handlers inject `IGenericWriteRepository<StockItem,Guid>`, `IUnitOfWork`, `IMessageBus`, `ITenantInfo`, `CancellationToken`.

**Adjust behavior:** load (tracking) → capture `wasDepleted = item.IsDepleted()` → `item.Adjust(delta)` → `SaveChangesAsync` → then publish directly: if `delta>0 && wasDepleted && !item.IsDepleted()` → `StockReplenishedIntegrationEvent`; if `!wasDepleted && item.IsDepleted()` → `StockDepletedIntegrationEvent`; if `item.CrossedReorderThreshold()` → `ReorderTriggeredIntegrationEvent`. (Publish after commit, mirroring `basket`'s `CheckoutHandler`.)

- [ ] **Step 1: Write failing tests** for `AdjustStockHandler`: (a) a negative adjust that depletes publishes `StockDepletedIntegrationEvent` once (NSubstitute `IMessageBus`), commits once; (b) a positive adjust that crosses back publishes `StockReplenished`; (c) an adjust that lands ≤ threshold publishes `ReorderTriggered`. Assert via `bus.Received(1).PublishAsync(Arg.Is<...>(...))` and `unitOfWork.Received(1).SaveChangesAsync(...)`. Mirror `basket`'s `CheckoutHandlerTests` NSubstitute setup.
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement the three handlers (static classes ending `Handler`; `TenantId` stamped from `ITenantInfo` on register). **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit `feat(inventory): add register/adjust/set-policy handlers with stock events`.

### Task 9: `GetAvailability` query handler

**Files:** Create `Features/GetAvailability/V1/{GetAvailabilityQuery,GetAvailabilityHandler}.cs`. Test: `tests/unit/Inventory.UnitTests/GetAvailabilityHandlerTests.cs`.

**Interfaces:** `GetAvailabilityQuery(Guid ProductId, Guid? LocationId) : IQuery<AvailabilityDto>`. Handler injects `IGenericReadRepository<StockItem,Guid>`, sums `Available` across locations (filtered by `LocationId` if provided), returns `AvailabilityDto`. **This is a real `IQuery<>`** — it lets the arch test call the full `AssertAll`.

- [ ] **Step 1:** Failing test: seed two `StockItem`s for one product at two locations (available 3 and 4) via a substitute read repo returning them; assert `GetAvailabilityHandler.Handle` returns `Available == 7` and two `ByLocation` entries.
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement. **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit `feat(inventory): add availability query handler`.

### Task 10: Host endpoints (Phase 1: register, adjust, set-policy, availability, list)

**Files:** Create `Endpoints/Inventory/{RegisterStockItem,AdjustStock,SetPolicy,GetAvailability,ListStockItems}Endpoint.cs` + `Request` + `Validator` where the endpoint has a body. Program bootstrap. Test: none new (endpoints are thin; covered by integration in Task 21).

**Interfaces:** Consumes the Task 8/9 commands/query. Endpoints derive from `AuthenticatedEndpoint`, invoke via `IMessageBus.InvokeAsync`. Mirror `basket`'s `Endpoints/Baskets/*` exactly (routes, request records, FluentValidation validators with `GreaterThan`/`NotEmpty` rules).

- [ ] **Step 1:** Create `Program.cs` + `Program.Public.cs` + `appsettings*.json` mirroring `basket`'s (swap names, `InventoryOptions`, `AddInventoryPersistence`). Register `InventoryOptions`.
- [ ] **Step 2:** Create the 5 endpoints + requests + validators mirroring `basket`'s endpoint files. Routes: `POST /inventory/stock-items`, `POST /inventory/stock-items/{id}/adjust`, `PUT /inventory/stock-items/{id}/policy`, `GET /inventory/availability`, `GET /inventory/stock-items`.
- [ ] **Step 3:** Run `dotnet build` the Host. Expected: 0/0 (fix any missing validator/XML-doc analyzer errors).
- [ ] **Step 4:** Commit `feat(inventory): add host endpoints and program bootstrap`.

### Task 11: Initial EF migration (Phase 1 schema: StockItem)

**Files:** `Inventory.Host/Database/Migrations/*` + design-time factory (from Task 5).

- [ ] **Step 1:** Run `dotnet ef migrations add InitialInventory --project src/services/commerce/inventory/Inventory.Application/Inventory.Application.csproj --startup-project src/services/commerce/inventory/Inventory.Host/Inventory.Host.csproj --output-dir Database/Migrations` (verify the exact `--output-dir` against how `basket`'s migration was generated).
- [ ] **Step 2:** Hand-fix the generated `.cs` to file-scoped namespace + trailing commas (EF generates block namespace / no trailing commas → analyzer errors). Verify the `StockItems` table + unique index + `xmin` column.
- [ ] **Step 3:** `dotnet build` the Host. Expected: 0/0.
- [ ] **Step 4:** Commit `feat(inventory): add InitialInventory EF migration`.

---

# PHASE 2 — Reservations + order commit

Ships: `OrderPlaced` commits stock across locations with optimistic-concurrency retry; reserved/rejected events fire.

### Task 12: `Allocation` + `ReservationLine` value objects + `Reservation` aggregate

**Files:** Create `ValueObjects/Allocation.cs`, `ValueObjects/ReservationLine.cs`, `Entities/Reservation.cs`. Test: `tests/unit/Inventory.UnitTests/ReservationTests.cs`.

**Interfaces:** Produces:
- `Allocation(Guid LocationId, int Quantity)` — owned record.
- `ReservationLine` — owned: `Guid ProductId`, `int RequestedQuantity`, `int BackorderedQuantity`, `IReadOnlyList<Allocation> Allocations`.
- `Reservation : BaseEntity, IAggregateRoot, ITenantScoped`: static `CreateCommitted(ReservationSource source, Guid sourceId, string tenantId, IReadOnlyList<ReservationLine> lines) : Reservation` (Status=Committed, ExpiresAt=null); props `ReservationSource SourceType`, `Guid SourceId`, `ReservationStatus Status`, `DateTimeOffset? ExpiresAt`, `IReadOnlyList<ReservationLine> Lines`, `string TenantId`; methods `void Release()`, `void Fulfil()`, `void Expire()` (guarded transitions per the SmartEnum lifecycle).

- [ ] **Step 1: Write failing tests:** `CreateCommitted` yields `Status==Committed`, `ExpiresAt==null`, lines preserved; `Release()` from `Committed` sets `Released`; `Expire()` from `Committed` throws (only `Held` expires).
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement (owned collections via backing field `_lines`, mirroring how `basket`'s `Basket` owns `_items`; `HasField("_lines")` will be needed in config, Task 14). **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit `feat(inventory): add Reservation aggregate and value objects`.

### Task 13: `StockAllocator` domain service (priority allocation, all-or-nothing)

**Files:** Create `Domain/Services/StockAllocator.cs`. Test: `tests/unit/Inventory.UnitTests/StockAllocatorTests.cs`.

**Interfaces:** Produces a pure static service:
`AllocationResult Allocate(IReadOnlyList<StockItem> itemsInPriorityOrder, int requestedQuantity)` returning `AllocationResult(bool Satisfied, IReadOnlyList<Allocation> Allocations, int BackorderedQuantity)`. Rules: fill from items in the given order (each contributes `min(remaining, item.Available)` for non-backorder; for backorder the last item absorbs the remainder as backordered). `Satisfied=false` only when total available < requested AND no item allows backorder. **The service computes the plan; it does not mutate** (the handler applies `item.Reserve(...)`).

- [ ] **Step 1: Write failing tests:** (a) request 7 across locations available [5,4] → allocations [5,2], satisfied, backorder 0; (b) request 10 across [3,2] all `AllowBackorder=false` → `Satisfied=false`; (c) request 10 across [3,2] where the priority-tail item has `AllowBackorder=true` → allocations sum 5, `BackorderedQuantity=5`, satisfied true.
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement the pure allocator. **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit `feat(inventory): add priority stock allocator`.

### Task 14: Reservation persistence config + LocationPriority

**Files:** Create `Entities/LocationPriority.cs`, `Configurations/ReservationConfiguration.cs`, `Configurations/LocationPriorityConfiguration.cs`; add `DbSet<Reservation>`, `DbSet<LocationPriority>` to `InventoryDbContextBase`. Test: `tests/unit/Inventory.UnitTests/ReservationPersistenceTests.cs`.

**Interfaces:** `LocationPriority : BaseEntity, IAggregateRoot, ITenantScoped` — `string TenantId`, `IReadOnlyList<Guid> LocationIds` (ordered), static `Create`, `void Set(IReadOnlyList<Guid> ordered)`. `ReservationConfiguration` maps owned `Lines` → owned `Allocations` (`OwnsMany` + `Navigation(...).HasField("_lines")`, mirroring `basket`'s `BasketConfiguration` owned-collection handling).

- [ ] **Step 1:** Failing test: persist a `Reservation` with 2 lines (one with 2 allocations) to EF InMemory, reload, assert lines/allocations round-trip. Persist a `LocationPriority`, assert ordered ids round-trip.
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement configs (owned collections; `LocationIds` as a converted `jsonb`/comma list — mirror any list-value conversion in the refs, else `HasConversion` to a delimited string). **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit `feat(inventory): add Reservation/LocationPriority persistence`.

### Task 14b: `SetLocationPriorities` command, handler, and endpoint

**Files:** Create `Features/SetLocationPriorities/V1/{SetLocationPrioritiesCommand,SetLocationPrioritiesHandler}.cs`, `Endpoints/Inventory/SetLocationPrioritiesEndpoint.cs` + `Request` + `Validator`. Test: `tests/unit/Inventory.UnitTests/SetLocationPrioritiesHandlerTests.cs`.

**Interfaces:** `SetLocationPrioritiesCommand(IReadOnlyList<Guid> LocationIds) : ICommand<Unit>` (or return the persisted ordered list DTO). Handler injects `IGenericWriteRepository<LocationPriority,Guid>`, `IUnitOfWork`, `ITenantInfo`: upsert the tenant's single `LocationPriority` (load existing by tenant, `Set(ordered)`; else `Create`), commit once. Endpoint `PUT /inventory/location-priorities` derives from `AuthenticatedEndpoint`, validator requires a non-empty, distinct `LocationIds`.

- [ ] **Step 1: Write failing test:** setting priorities creates a `LocationPriority` for the tenant with the ordered ids and commits once; setting again updates the existing one (no duplicate).
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement command/handler + endpoint/request/validator (mirror `basket`'s endpoint style). **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit `feat(inventory): add set-location-priorities handler and endpoint`.

### Task 15: `OrderPlaced` commit consumer (with concurrency retry)

**Files:** Create `Inventory/EventHandlers/IntegrationEvents/OrderPlacedHandler.cs`, `ReadModels/ReservationBySourceSpec.cs`, `ReadModels/StockItemsByProductForTenantSpec.cs`, and a helper `Inventory/Features/CommitReservation/V1/ReservationCommitter.cs` (application service holding the load→allocate→reserve→save loop with retry). Test: `tests/unit/Inventory.UnitTests/OrderPlacedHandlerTests.cs`.

**Interfaces:** Consumes `OrderPlacedIntegrationEvent` (read its actual shape in `Order.Application/Orders/IntegrationEvents/OrderPlacedIntegrationEvent.cs` — confirm it exposes order id, tenant id, and lines with productId + quantity). Handler signature: `static async Task Handle(OrderPlacedIntegrationEvent evt, IServiceProvider sp / injected repos, IMessageBus bus, CancellationToken ct)`.

**Behavior:**
1. Idempotency: `ReservationBySourceSpec(ReservationSource.Order, evt.OrderId)` → if exists, return.
2. For each order line: load that product's `StockItem`s across locations, ordered per the tenant's `LocationPriority` (fallback: any order); `StockAllocator.Allocate(...)`.
3. If **any** line unsatisfiable → publish `StockReservationRejectedIntegrationEvent` (with failing products), do NOT mutate, return. (All-or-nothing.)
4. Else apply `item.Reserve(alloc.Quantity)` to each affected `StockItem`, create `Reservation.CreateCommitted(...)`, `SaveChangesAsync` once.
5. On `DbUpdateConcurrencyException`: reload + re-run steps 2-4, up to `InventoryOptions.MaxReserveRetries`; on final failure publish `StockReservationRejected` (contention).
6. After commit: publish `StockReservedIntegrationEvent`; for each depleted/reorder-crossed `StockItem`, publish `StockDepleted`/`ReorderTriggered`.

- [ ] **Step 1: Write failing tests** (NSubstitute repos + `IMessageBus`): (a) an order for a product with enough stock across two locations reserves and publishes `StockReserved` once, commits once, and the reservation is `Committed`; (b) re-delivering the same `OrderPlaced` (idempotency spec returns an existing reservation) publishes nothing and does not commit; (c) an order line exceeding stock with backorder off publishes `StockReservationRejected` and does not commit. (Unit-test the committer's decision logic with substitutes; the real concurrency retry is covered by the integration test in Task 22.)
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement `ReservationCommitter` + `OrderPlacedHandler`. Keep the retry loop in `ReservationCommitter`; catch `DbUpdateConcurrencyException`, reload via fresh reads, bounded by `MaxReserveRetries`. **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit `feat(inventory): consume OrderPlaced to commit stock reservations`.

### Task 15b: Phase-2 EF migration (Reservation + LocationPriority tables)

**Files:** `Inventory.Host/Database/Migrations/*` (a second, additive migration).

**Interfaces:** Adds the `Reservations` (+ owned `ReservationLine`/`Allocation` tables or owned-JSON columns per the Task 14 config), `LocationPriorities` tables and the `(SourceType, SourceId)` unique index for idempotency. `StockItems` from Task 11 is untouched.

- [ ] **Step 1:** Run `dotnet ef migrations add InventoryReservations --project ...Inventory.Application.csproj --startup-project ...Inventory.Host.csproj --output-dir Database/Migrations`.
- [ ] **Step 2:** Hand-fix the generated `.cs` to file-scoped namespace + trailing commas. Verify the new tables, the owned-collection mapping, and a **unique index on `(TenantId, SourceType, SourceId)`** (idempotency). Confirm the migration is additive (no destructive change to `StockItems`).
- [ ] **Step 3:** `dotnet build` the Host. Expected: 0/0.
- [ ] **Step 4:** Commit `feat(inventory): add InventoryReservations EF migration`.

---

# PHASE 3 — Holds + lazy expiry

### Task 16: `BasketCheckedOut` hold consumer

**Files:** Create `Inventory/EventHandlers/IntegrationEvents/BasketCheckedOutHandler.cs`; extend `ReservationCommitter` with `HoldFor(...)` (Status=Held, ExpiresAt=now+HoldTtl). Test: `tests/unit/Inventory.UnitTests/BasketCheckedOutHandlerTests.cs`.

**Interfaces:** Consumes `BasketCheckedOutIntegrationEvent` (read its shape). Same allocate→reserve→save flow as commit, but creates a `Held` reservation with `ExpiresAt = clock.now + InventoryOptions.HoldTtl`. Inject a clock abstraction (`TimeProvider`) so expiry is testable. Idempotency keyed by `(Basket, evt.BasketId)`.

- [ ] **Step 1: Write failing test:** a `BasketCheckedOut` creates a `Held` reservation with `ExpiresAt == now + HoldTtl` (inject a fixed `TimeProvider`), reserves stock, publishes `StockReserved`. Re-delivery is a no-op.
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement (reuse `ReservationCommitter`, add `HoldFor`). **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit `feat(inventory): consume BasketCheckedOut to place soft holds`.

### Task 17: Lazy expiry in availability/allocation

**Files:** Modify `AvailabilityByProductSpec` and the stock-load path so **only non-expired reservations count toward reserved**. Because `QuantityReserved` is stored on `StockItem`, lazy expiry needs the "effective reserved" to discount expired holds. Approach: the availability query joins active (non-expired) reservation allocations rather than trusting the stored `QuantityReserved`, OR the sweep (Task 18) keeps `QuantityReserved` truthful and lazy read subtracts expired-hold allocations. **Decision (make explicit in code + comment):** compute effective available in the read model as `OnHand − (Committed allocations + Held allocations where ExpiresAt > now)` via a dedicated read query, so an expired hold stops counting immediately even before the sweep runs. Test: `tests/unit/Inventory.UnitTests/LazyExpiryTests.cs`.

**Interfaces:** Produces `EffectiveAvailabilityQuery`/spec that computes available from live allocations, used by `GetAvailabilityHandler` and by the allocation load path.

- [ ] **Step 1: Write failing test:** seed a `StockItem` (onHand 5) with a `Held` reservation for 5 whose `ExpiresAt` is in the past → effective available == 5 (hold ignored); with `ExpiresAt` in the future → effective available == 0.
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement the effective-availability read path (project over reservations filtered by `Status` active + `ExpiresAt > now`). **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit `feat(inventory): compute availability with lazy hold expiry`.

### Task 18: Expiry sweep (housekeeping) + hosted service

**Files:** Create `Features/ExpireHeldReservations/V1/{ExpireHeldReservationsCommand,ExpireHeldReservationsHandler}.cs`, `ReadModels/ExpiredHeldReservationsSpec.cs`, `Inventory.Host/Infrastructure/ReservationExpirySweepService.cs`. Test: `tests/unit/Inventory.UnitTests/ExpireHeldReservationsHandlerTests.cs`.

**Interfaces:** `ExpireHeldReservationsCommand() : ICommand<int>` (returns count expired). Handler loads `ExpiredHeldReservationsSpec` (Status=Held, ExpiresAt ≤ now), for each: `reservation.Expire()` + `item.Release(qty)` on each allocated `StockItem`, `SaveChangesAsync` once. `ReservationExpirySweepService : BackgroundService` invokes the command every `InventoryOptions.SweepInterval` via `IMessageBus.InvokeAsync`.

- [ ] **Step 1: Write failing test:** seed one expired `Held` reservation (allocation qty 3 on a StockItem with reserved 3) → handler sets it `Expired`, releases 3 (reserved → 0), returns count 1.
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement handler + spec + hosted service (register the hosted service in `Program.cs`). **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit `feat(inventory): add held-reservation expiry sweep`.

---

# PHASE 4 — Backorder

### Task 19: Backorder fill on replenish

**Files:** Modify `AdjustStockHandler` (Task 8) so a positive adjust that creates availability fills outstanding backordered lines for that product; add `ReadModels/BackorderedLinesByProductSpec.cs`. Test: `tests/unit/Inventory.UnitTests/BackorderFillTests.cs`.

**Interfaces:** On positive adjust, after applying stock: load reservations with `BackorderedQuantity > 0` for the product (FIFO by created time), convert backordered quantity to real allocations up to the new availability, reduce `BackorderedQuantity`, `item.Reserve(...)` the newly-covered amount, publish `StockReplenishedIntegrationEvent`. All within the single commit.

- [ ] **Step 1: Write failing test:** a product with a committed reservation carrying `BackorderedQuantity=4`; adjust +6 → 4 of the 6 fill the backorder (backordered → 0, reserved += 4), `StockReplenished` published; remaining 2 stay on hand.
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement the fill logic + spec. **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit `feat(inventory): fill backorders on stock replenishment`.

---

# CROSS-CUTTING (do after Phase 4)

### Task 20: Architecture test project

**Files:** Create `tests/architecture/Inventory.Architecture.UnitTests/{Inventory.Architecture.UnitTests.csproj,InventoryArchitectureTests.cs}`; register in `Teck.Platform.slnx`.

**Interfaces:** Mirror `tests/architecture/Order.Architecture.UnitTests/OrderArchitectureTests.cs` (NOT basket's) — inventory has real `IQuery<>` types, so it calls the full `SharedArchitectureRules.AssertAll` and keeps the `...Handlers_ShouldEndWithHandler` reflection test. Anchors: `Inventories.Domain.Entities.StockItem`, a concrete Application handler type, `typeof(Program)`.

- [ ] **Step 1:** Create csproj mirroring `Order.Architecture.UnitTests.csproj` (swap Order→Inventory). **Step 2:** Create the test class mirroring `OrderArchitectureTests` with inventory anchors + `AssertAll`. Register in `.slnx`.
- [ ] **Step 3:** Run `dotnet test tests/architecture/Inventory.Architecture.UnitTests/...`. Expected: ALL green. If a rule fails, it is a real violation — fix the offending inventory code, do NOT weaken the test.
- [ ] **Step 4:** Commit `test(inventory): add architecture boundary tests`.

### Task 21: Integration test — availability + adjust events

**Files:** Create `tests/integration/Inventory.IntegrationTests/{Inventory.IntegrationTests.csproj, InventoryStockTests.cs, MockBearerAuthenticationHandler.cs, SharedTestcontainersCollection.cs}`; register in `.slnx`. Mirror `tests/integration/Basket.IntegrationTests/*` (Testcontainers Postgres+RabbitMQ, `WebApplicationFactory<Program>`, mock bearer with `tenant_id`).

- [ ] **Step 1:** Mirror the basket harness (swap names; align the mock claims to what inventory endpoints read — tenant only, no customer needed).
- [ ] **Step 2:** Test: `POST /inventory/stock-items` (onHand 5) → `GET /inventory/availability?productId` returns 5 → `POST .../adjust` −5 → availability 0. Un-skipped, runs against Testcontainers.
- [ ] **Step 3:** Run `dotnet test tests/integration/Inventory.IntegrationTests/...`. Expected: PASS.
- [ ] **Step 4:** Commit `test(inventory): add stock/availability integration test`.

### Task 22: Integration test — concurrent-reserve race → no oversell (headline)

**Files:** Add `InventoryConcurrencyTests.cs` to the integration project.

**Interfaces:** Exercises the real `OrderPlacedHandler` + `ReservationCommitter` retry path against real Postgres (`xmin` concurrency).

- [ ] **Step 1: Write the test:** register a `StockItem` with `onHand=1`, `AllowBackorder=false`, single location. Fire **two** `OrderPlaced` events concurrently (each requesting quantity 1) — e.g. publish both via the bus / invoke both handlers in parallel `Task.WhenAll`. Assert: exactly **one** reservation ends `Committed` and one `StockReservationRejected` was published (or the losing commit retried and then rejected); final `StockItem.QuantityReserved == 1` and `Available == 0` — **never oversold to 2**.
- [ ] **Step 2:** Run it. Expected: PASS (the `xmin` concurrency + reload-retry prevents the double reserve). If it oversells, the retry/allocation is wrong — fix `ReservationCommitter`, do not weaken the assertion.
- [ ] **Step 3:** Commit `test(inventory): prove concurrent reservations never oversell`.

### Task 23: Aspire registration + full gate

**Files:** Modify `src/aspire/Teck.AppHost/AppHost.cs` + `Teck.AppHost.csproj` (add `inventorydb` + `Projects.Inventory_Host` resource with rabbitmq/redis/keycloak refs, mirroring the `basket` block). Add the `Basket.Host`-style `PublishDomainEventsFromEntityFrameworkCore` is NOT needed (direct publish).

- [ ] **Step 1:** Add `inventorydb` + the inventory project resource in `AppHost.cs` (mirror the committed basket block) and the project reference in `Teck.AppHost.csproj`. Do NOT add inventory to the gateway `WaitFor` (keep the Aspire smoke test independent).
- [ ] **Step 2:** `dotnet build` the AppHost. Expected: 0/0 (`Projects.Inventory_Host` resolves). Run `dotnet test tests/integration/Aspire.AppHost.IntegrationTests/...`. Expected: still 1/1.
- [ ] **Step 3:** Run the full gate: `bunx nx affected -t build test lint typecheck --base=main --head=HEAD`. Expected: all green.
- [ ] **Step 4:** Commit `chore(inventory): wire inventory into Aspire local orchestration`.

---

## Notes for the executor

- **Confirm event shapes before consuming:** read `OrderPlacedIntegrationEvent` and `BasketCheckedOutIntegrationEvent` in `src/shared/SharedKernel.Events/` and `Order.Application/.../IntegrationEvents/` for exact field names/types before Tasks 15/16 — do not assume.
- **`xmin` concurrency mapping** is the one genuinely new EF detail vs. basket/order — verify it maps and that `DbUpdateConcurrencyException` is thrown on conflict (Task 22 proves it end-to-end).
- **`TimeProvider`** (injected clock) is used for `ExpiresAt` and lazy-expiry comparisons so Phase-3 tests are deterministic — register `TimeProvider.System` in `Program.cs`, inject the fake in unit tests.
- **Phasing:** each phase ends green and is independently reviewable. A reviewer can gate Phase 1 (stock core works) before Phase 2 starts.
