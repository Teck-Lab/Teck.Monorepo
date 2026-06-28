# Repository + UnitOfWork Architecture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every Application command/query handler in `order` and `catalog` depend only on SharedKernel repository + UnitOfWork abstractions (never a concrete `DbContext` or Ardalis `IRepositoryBase<T>`), with a real read/write `DbContext` split, and enforce it with an architecture test.

**Architecture:** Adopt SharedKernel's already-built-but-dormant `IGenericReadRepository<T,TId>`, `IGenericWriteRepository<T,TId>`, and `IUnitOfWork`. Repositories only track changes; `IUnitOfWork.SaveChangesAsync` is the single commit point. Each service gets an abstract `{Service}DbContextBase` (model defined once) with sibling `{Service}DbContext` (write, tracked) and `{Service}ReadDbContext` (read, NoTracking). Per-service `*WriteRepository`/`*ReadRepository` subclasses bind the contexts so the 3-type-param generics can be registered as open generics.

**Tech Stack:** .NET 10, EF Core (Npgsql + InMemory for tests), Ardalis.Specification, WolverineFx (static-method handlers with injected deps), Finbuckle.MultiTenant, NSubstitute + xUnit v3, ArchUnitNET.

## Global Constraints

- Target framework `net10.0`; nullable + implicit usings on (root `Directory.Build.props`). `TreatWarningsAsErrors=true` — analyzers/StyleCop fail the build. Public types/members need XML docs; usings ordered; file-scoped namespaces.
- The `DbContext` is **never** injected into an Application handler after this plan. Handlers inject only `IGenericReadRepository<T,TId>`, `IGenericWriteRepository<T,TId>`, `IUnitOfWork` (from `SharedKernel.Core.Database`), plus existing non-persistence deps (`IMessageBus`, etc.).
- `IUnitOfWork.SaveChangesAsync` is the only commit call in handlers. Repositories expose **no** `SaveChangesAsync`.
- Command handlers that load-then-mutate an existing aggregate load it **tracked** via `enableTracking: true`; create handlers use `AddAsync` then `unitOfWork.SaveChangesAsync`.
- Ids are `Guid` everywhere (`DefaultIdType = System.Guid`). Use `Guid` as `TId`.
- Layer direction: Domain ← Application ← Host. Read contexts and repository subclasses live in the **Host** (composition root); the abstract base + write context live in **Application**. Application must not reference Host.
- Keep each task's build green: `nx affected -t build test` (or `dotnet build Teck.Platform.slnx`) must pass at every commit. The enforcement architecture test is added **last**, after all handlers are migrated.
- Conventional commits (`type(scope): description`). Never create tags or run `nx release`.

## File Structure

**SharedKernel (Phase 0 — foundation):**
- Modify `src/shared/SharedKernel.Core/Domain/IBaseEntity.cs` — `IBaseEntity<TId> : IReadModel<TId>`.
- Modify `src/shared/SharedKernel.Core/Database/IGenericWriteRepository.cs` — remove `SaveChangesAsync`; rename `Excecut*`→`Execute*`.
- Modify `src/shared/SharedKernel.Infrastructure/Database/EFCore/GenericWriteRepository.cs` — remove `SaveChangesAsync` impl; rename `Excecut*`→`Execute*`.

**Catalog (Phase 1):**
- Modify `…/catalog/Catalog.Application/Database/CatalogDbContext.cs` → becomes write leaf.
- Create `…/catalog/Catalog.Application/Database/CatalogDbContextBase.cs` — abstract base (model).
- Create `…/catalog/Catalog.Host/Database/CatalogReadDbContext.cs` — read sibling.
- Create `…/catalog/Catalog.Host/Database/CatalogReadRepository.cs`, `CatalogWriteRepository.cs`.
- Create `…/catalog/Catalog.Host/Database/CatalogPersistenceExtensions.cs` — DI wiring.
- Modify `…/catalog/Catalog.Host/Program.cs` — call the wiring.
- Modify 10 handlers under `Catalog.Application/{Products,Suppliers}/Features/**`.
- Modify `tests/unit/Catalog.UnitTests/TestContext/CatalogTestContext.cs` + the affected `*HandlerTests.cs`.
- Create `tests/architecture/Catalog.Architecture.UnitTests/` (Phase 3).

**Order (Phase 2):**
- Create `…/order/Order.Application/Database/OrderDbContextBase.cs`; modify `OrderDbContext.cs`; modify `…/order/Order.Host/Database/OrderReadDbContext.cs`.
- Create `…/order/Order.Host/Database/{OrderReadRepository,OrderWriteRepository,OrderPersistenceExtensions}.cs`; modify `Order.Host/Program.cs`.
- Modify `CreateOrderHandler.cs`, `GetOrderHandler.cs` + their tests in `tests/unit/Order.UnitTests/`.

**Enforcement + docs (Phase 3):**
- Modify `tests/architecture/Order.Architecture.UnitTests/OrderArchitectureTests.cs`; create `Catalog.Architecture.UnitTests`.
- Rewrite persistence sections in `src/services/AGENTS.md`, `CLAUDE.md`, `…/order/AGENTS.md`, `…/catalog/AGENTS.md`; update memory note.

---

## Phase 0 — SharedKernel foundation

### Task 1: Entities satisfy the read-repository constraint

**Files:**
- Modify: `src/shared/SharedKernel.Core/Domain/IBaseEntity.cs:14`
- Test: `tests/unit/Catalog.UnitTests/Application/CatalogDbContextTests.cs` (add one fact) — or a new `tests/unit/Catalog.UnitTests/Application/ReadModelConstraintTests.cs`

**Interfaces:**
- Produces: `IBaseEntity<TId>` now implements `IReadModel<TId>`, so every `BaseEntity`-derived entity (`Order`, `Product`, `Category`, `Supplier`) satisfies `where T : class, IReadModel<TId>`. No member changes (`Id` already present on both).

- [ ] **Step 1: Write the failing test** — create `tests/unit/Catalog.UnitTests/Application/ReadModelConstraintTests.cs`:

```csharp
using Catalog.Domain.Entities;
using SharedKernel.Core.Domain;
using Xunit;

namespace Catalog.UnitTests.Application;

public sealed class ReadModelConstraintTests
{
    [Fact]
    public void Product_ImplementsIReadModelOfGuid()
    {
        Assert.True(typeof(IReadModel<System.Guid>).IsAssignableFrom(typeof(Product)));
    }
}
```

- [ ] **Step 2: Run it, expect FAIL** — `nx test --project=catalog-unit-tests` (or `dotnet test tests/unit/Catalog.UnitTests`). Expected: assertion fails (`Product` does not implement `IReadModel<Guid>`).

- [ ] **Step 3: Implement** — edit `IBaseEntity.cs` line 14:

```csharp
public interface IBaseEntity<out TId> : IBaseEntity, ISoftDeletable, IAuditable, IReadModel<TId>
{
    /// <summary>
    /// Gets the id.
    /// </summary>
    TId Id { get; }
}
```

Add `using SharedKernel.Core.Domain;`? No — `IReadModel<TId>` is in the same `SharedKernel.Core.Domain` namespace; no using needed.

- [ ] **Step 4: Run it, expect PASS.** Also run `dotnet build Teck.Platform.slnx` to confirm no analyzer/doc regressions.

- [ ] **Step 5: Commit** — `git add -A && git commit -m "feat(shared): IBaseEntity implements IReadModel so entities satisfy read-repo constraint"`

### Task 2: Repositories stop owning SaveChanges; fix method typos

**Files:**
- Modify: `src/shared/SharedKernel.Core/Database/IGenericWriteRepository.cs` (remove lines 63-68 `SaveChangesAsync`; rename `ExcecutSoftDeleteAsync`/`ExcecutSoftDeleteByAsync`/`ExcecutHardDeleteAsync` → `ExecuteSoftDeleteAsync`/`ExecuteSoftDeleteByAsync`/`ExecuteHardDeleteAsync`).
- Modify: `src/shared/SharedKernel.Infrastructure/Database/EFCore/GenericWriteRepository.cs` (remove the `SaveChangesAsync` method, lines 148-156; rename the three `Excecut*` impls).
- Test: `tests/unit/Catalog.UnitTests/Application/ReadModelConstraintTests.cs` (extend with a reflection fact).

**Interfaces:**
- Produces: `IGenericWriteRepository<TEntity,TId>` exposes `AddAsync`, `Update`, `Delete`, `DeleteRange`, `ExecuteSoftDeleteAsync(IReadOnlyCollection<TId>, …)`, `ExecuteSoftDeleteByAsync(Expression<Func<TEntity,bool>>, …)`, `ExecuteHardDeleteAsync(IReadOnlyCollection<TId>, …)` and all read members (inherited). **No `SaveChangesAsync`.**

- [ ] **Step 1: Write the failing test** — append to `ReadModelConstraintTests.cs`:

```csharp
[Fact]
public void WriteRepository_DoesNotExposeSaveChanges()
{
    var method = typeof(SharedKernel.Core.Database.IGenericWriteRepository<,>)
        .GetMethod("SaveChangesAsync");
    Assert.Null(method);
}
```

- [ ] **Step 2: Run it, expect FAIL** (method still present).

- [ ] **Step 3: Implement** —
  - In `IGenericWriteRepository.cs`: delete the `SaveChangesAsync` declaration (the `<summary>` block + `Task<int> SaveChangesAsync(...)`); rename the three `Excecut*` methods to `Execute*`.
  - In `GenericWriteRepository.cs`: delete the `SaveChangesAsync` method (its summary + body); rename the three `Excecut*` method bodies to `Execute*` to match.

- [ ] **Step 4: Run it, expect PASS.** Run `dotnet build Teck.Platform.slnx` — must be green (nothing referenced these members; they were dormant).

- [ ] **Step 5: Commit** — `git add -A && git commit -m "refactor(shared): UnitOfWork owns SaveChanges; remove it from write repo and fix Execute* typos"`

---

## Phase 1 — Catalog adoption

### Task 3: Catalog read/write context split (abstract base + siblings)

**Files:**
- Create: `src/services/commerce/catalog/Catalog.Application/Database/CatalogDbContextBase.cs`
- Modify: `src/services/commerce/catalog/Catalog.Application/Database/CatalogDbContext.cs`
- Create: `src/services/commerce/catalog/Catalog.Host/Database/CatalogReadDbContext.cs`
- Test: `tests/unit/Catalog.UnitTests/Application/CatalogDbContextTests.cs`

**Interfaces:**
- Produces: `CatalogDbContextBase` (abstract, holds `Products`/`Categories`/`Suppliers` `DbSet`s + `OnModelCreating`), `CatalogDbContext : CatalogDbContextBase` (write, tracked), `CatalogReadDbContext : CatalogDbContextBase` (read, NoTracking). All keep ctor `(DbContextOptions, IMultiTenantContextAccessor<TenantDetails>)`.

- [ ] **Step 1: Write the failing test** — add to `CatalogDbContextTests.cs`:

```csharp
[Fact]
public void ReadContext_UsesNoTracking()
{
    var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<Catalog.Host.Database.CatalogReadDbContext>()
        .UseInMemoryDatabase($"read-{System.Guid.NewGuid()}").Options;
    using var ctx = new Catalog.Host.Database.CatalogReadDbContext(options, NSubstitute.Substitute.For<IMultiTenantContextAccessor<TenantDetails>>());
    Assert.Equal(Microsoft.EntityFrameworkCore.QueryTrackingBehavior.NoTracking, ctx.ChangeTracker.QueryTrackingBehavior);
}
```

Add usings `Finbuckle.MultiTenant.Abstractions;` and `SharedKernel.Infrastructure.MultiTenant;` if not present. (This requires the test project to reference `Catalog.Host`; add `<ProjectReference Include="..\..\..\src\services\commerce\catalog\Catalog.Host\Catalog.Host.csproj" />` to `Catalog.UnitTests.csproj`.)

- [ ] **Step 2: Run it, expect FAIL** (`CatalogReadDbContext` does not exist / does not compile).

- [ ] **Step 3a: Create `CatalogDbContextBase.cs`** (move the model out of the old write context):

```csharp
using Catalog.Domain.Entities;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.MultiTenant;

namespace Catalog.Application.Database;

/// <summary>
/// Abstract catalog context that defines the entity model exactly once. The write context
/// (<see cref="CatalogDbContext"/>) and the read context (<c>CatalogReadDbContext</c>) derive from it.
/// </summary>
/// <param name="options">The options used to configure the context.</param>
/// <param name="tenantContextAccessor">The accessor used to resolve the current tenant for multi-tenant isolation.</param>
public abstract class CatalogDbContextBase(DbContextOptions options, IMultiTenantContextAccessor<TenantDetails> tenantContextAccessor)
    : BaseDbContext(options, tenantAccessor: tenantContextAccessor)
{
    /// <summary>Gets the products.</summary>
    public DbSet<Product> Products => Set<Product>();

    /// <summary>Gets the categories.</summary>
    public DbSet<Category> Categories => Set<Category>();

    /// <summary>Gets the suppliers.</summary>
    public DbSet<Supplier> Suppliers => Set<Supplier>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Owned-type configurations must run before base.OnModelCreating so that
        // Finbuckle's ConfigureMultiTenant() does not discover Variant/VariantSupplier/
        // SupplierPriceHistory as plain entity types before they are marked owned.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContextBase).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
```

- [ ] **Step 3b: Replace `CatalogDbContext.cs`** with the thin write leaf:

```csharp
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Infrastructure.MultiTenant;

namespace Catalog.Application.Database;

/// <summary>
/// The catalog write context (change tracking enabled). Owns EF Core migrations.
/// </summary>
/// <param name="options">The options used to configure the context.</param>
/// <param name="tenantContextAccessor">The accessor used to resolve the current tenant for multi-tenant isolation.</param>
public class CatalogDbContext(DbContextOptions options, IMultiTenantContextAccessor<TenantDetails> tenantContextAccessor)
    : CatalogDbContextBase(options, tenantContextAccessor);
```

- [ ] **Step 3c: Create `Catalog.Host/Database/CatalogReadDbContext.cs`:**

```csharp
using Catalog.Application.Database;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Infrastructure.MultiTenant;

namespace Catalog.Host.Database;

/// <summary>
/// The catalog read context (change tracking disabled).
/// </summary>
/// <param name="options">The options used to configure the context.</param>
/// <param name="tenantContextAccessor">The accessor providing the current tenant context.</param>
public class CatalogReadDbContext(DbContextOptions options, IMultiTenantContextAccessor<TenantDetails> tenantContextAccessor)
    : CatalogDbContextBase(options, tenantContextAccessor)
{
    /// <inheritdoc/>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    }
}
```

- [ ] **Step 4: Run it, expect PASS.** Run `dotnet build Teck.Platform.slnx` — green (existing handlers still inject `CatalogDbContext`, which still exists and still exposes `Products`/etc. via the base).

- [ ] **Step 5: Commit** — `git add -A && git commit -m "feat(catalog): split catalog DbContext into abstract base + write/read siblings"`

### Task 4: Catalog repository subclasses + DI wiring

**Files:**
- Create: `src/services/commerce/catalog/Catalog.Host/Database/CatalogWriteRepository.cs`
- Create: `src/services/commerce/catalog/Catalog.Host/Database/CatalogReadRepository.cs`
- Create: `src/services/commerce/catalog/Catalog.Host/Database/CatalogPersistenceExtensions.cs`
- Modify: `src/services/commerce/catalog/Catalog.Host/Program.cs`

**Interfaces:**
- Consumes: `GenericWriteRepository<TEntity,TId,TContext>` (ctor `(TContext, IHttpContextAccessor)`), `GenericReadRepository<TReadModel,TId,TContext>` (ctor `(TContext)`), `UnitOfWork<TContext>` (ctor `(TContext)`), `AddHybridMultiTenantDbContexts<TWrite,TRead>(...)`.
- Produces: open-generic registrations `IGenericReadRepository<,>`→`CatalogReadRepository<,>`, `IGenericWriteRepository<,>`→`CatalogWriteRepository<,>`, and `IUnitOfWork`→`UnitOfWork<CatalogDbContext>` bound to the **scoped** write context (same instance the write repo uses).

- [ ] **Step 1: Create `CatalogWriteRepository.cs`:**

```csharp
using Catalog.Application.Database;
using Microsoft.AspNetCore.Http;
using SharedKernel.Core.Domain;
using SharedKernel.Infrastructure.Database.EFCore;

namespace Catalog.Host.Database;

/// <summary>
/// Catalog write repository bound to <see cref="CatalogDbContext"/> so the three-type-parameter
/// <see cref="GenericWriteRepository{TEntity, TId, TContext}"/> can be registered as an open generic.
/// </summary>
/// <typeparam name="TEntity">The aggregate entity type.</typeparam>
/// <typeparam name="TId">The entity identifier type.</typeparam>
/// <param name="dbContext">The catalog write context.</param>
/// <param name="httpContextAccessor">The HTTP context accessor used for audit stamping on bulk deletes.</param>
public sealed class CatalogWriteRepository<TEntity, TId>(CatalogDbContext dbContext, IHttpContextAccessor httpContextAccessor)
    : GenericWriteRepository<TEntity, TId, CatalogDbContext>(dbContext, httpContextAccessor)
    where TEntity : BaseEntity;
```

- [ ] **Step 2: Create `CatalogReadRepository.cs`:**

```csharp
using Catalog.Application.Database;
using SharedKernel.Core.Domain;
using SharedKernel.Infrastructure.Database.EFCore;

namespace Catalog.Host.Database;

/// <summary>
/// Catalog read repository bound to <see cref="CatalogReadDbContext"/> (NoTracking) so the
/// three-type-parameter <see cref="GenericReadRepository{TReadModel, TId, TContext}"/> can be
/// registered as an open generic.
/// </summary>
/// <typeparam name="TReadModel">The read model type.</typeparam>
/// <typeparam name="TId">The identifier type.</typeparam>
/// <param name="dbContext">The catalog read context.</param>
public sealed class CatalogReadRepository<TReadModel, TId>(CatalogReadDbContext dbContext)
    : GenericReadRepository<TReadModel, TId, CatalogReadDbContext>(dbContext)
    where TReadModel : class, IReadModel<TId>;
```

- [ ] **Step 3: Create `CatalogPersistenceExtensions.cs`:**

```csharp
using Catalog.Application.Database;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Core.Database;
using SharedKernel.Infrastructure.Database;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.Database.MultiTenant;

namespace Catalog.Host.Database;

/// <summary>
/// Registers the catalog persistence stack: tenant-aware read/write contexts, generic
/// repositories, and the unit of work.
/// </summary>
public static class CatalogPersistenceExtensions
{
    /// <summary>
    /// Adds the catalog read/write contexts, repositories and unit of work to the host.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <returns>The same builder for chaining.</returns>
    public static WebApplicationBuilder AddCatalogPersistence(this WebApplicationBuilder builder)
    {
        var write = builder.Configuration.GetConnectionString("CatalogWrite")
            ?? builder.Configuration.GetConnectionString("Default")
            ?? throw new System.InvalidOperationException("Missing 'CatalogWrite'/'Default' connection string.");
        var read = builder.Configuration.GetConnectionString("CatalogRead") ?? write;

        builder.AddHybridMultiTenantDbContexts<CatalogDbContext, CatalogReadDbContext>(
            migrationsAssembly: typeof(Program).Assembly,
            defaultWriteConnectionString: write,
            defaultReadConnectionString: read,
            serviceName: "catalog");

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped(typeof(IGenericReadRepository<,>), typeof(CatalogReadRepository<,>));
        builder.Services.AddScoped(typeof(IGenericWriteRepository<,>), typeof(CatalogWriteRepository<,>));
        builder.Services.AddScoped<IUnitOfWork>(sp => new UnitOfWork<CatalogDbContext>(sp.GetRequiredService<CatalogDbContext>()));

        return builder;
    }
}
```

> **Why the explicit `UnitOfWork` lambda:** `UnitOfWork<T>` has both a `(TContext)` and an `(IDbContextFactory<T>)` constructor; the factory one creates a *separate* context, which would make `SaveChanges` commit a different instance than the repositories wrote to. Resolving the scoped `CatalogDbContext` guarantees one shared instance per request.

- [ ] **Step 4: Wire it in `Catalog.Host/Program.cs`** — after line 11 (`builder.Services.AddTeckService(...)`) add:

```csharp
builder.AddCatalogPersistence();
```

Add `using Catalog.Host.Database;` at the top.

- [ ] **Step 5: Build, expect green** — `dotnet build Teck.Platform.slnx`. (No unit test asserts runtime DI; the registration is verified at compile time here and exercised by handler migrations next. Full DB round-trip is covered by future Testcontainers integration tests; confirm the real connection-string keys at first deploy.)

- [ ] **Step 6: Commit** — `git add -A && git commit -m "feat(catalog): register generic repositories + unit of work over read/write contexts"`

### Task 5: Test helpers for repository + unit of work

**Files:**
- Modify: `tests/unit/Catalog.UnitTests/TestContext/CatalogTestContext.cs`

**Interfaces:**
- Produces: `CatalogTestContext.WriteRepo<TEntity>(CatalogDbContext) : IGenericWriteRepository<TEntity, Guid>` (where `TEntity : BaseEntity`), `CatalogTestContext.UnitOfWork(CatalogDbContext) : IUnitOfWork`. These let handler tests build the new dependencies over the same in-memory context they seed/stub.

- [ ] **Step 1: Add helpers** to `CatalogTestContext.cs` (new usings: `Microsoft.AspNetCore.Http;`, `SharedKernel.Core.Database;`, `SharedKernel.Core.Domain;`, `SharedKernel.Infrastructure.Database.EFCore;`):

```csharp
/// <summary>Builds a write repository over the given context (audit accessor is a no-op substitute).</summary>
public static IGenericWriteRepository<TEntity, Guid> WriteRepo<TEntity>(CatalogDbContext db)
    where TEntity : BaseEntity =>
    new GenericWriteRepository<TEntity, Guid, CatalogDbContext>(db, Substitute.For<IHttpContextAccessor>());

/// <summary>Builds a unit of work that commits the given context.</summary>
public static IUnitOfWork UnitOfWork(CatalogDbContext db) =>
    new UnitOfWork<CatalogDbContext>(db);
```

- [ ] **Step 2: Build the test project, expect green** — `dotnet build tests/unit/Catalog.UnitTests`. (No behavior yet; helpers are unused until the next tasks.)

- [ ] **Step 3: Commit** — `git add -A && git commit -m "test(catalog): add write-repo + unit-of-work test helpers"`

### Task 6: Migrate the create handlers (CreateProduct, CreateCategory, CreateSupplier)

**Files:**
- Modify: `Catalog.Application/Products/Features/CreateProduct/V1/CreateProductHandler.cs`
- Modify: `Catalog.Application/Products/Features/CreateCategory/V1/CreateCategoryHandler.cs`
- Modify: `Catalog.Application/Suppliers/Features/CreateSupplier/V1/CreateSupplierHandler.cs`
- Test: `tests/unit/Catalog.UnitTests/Application/CreateProductHandlerTests.cs` (+ any CreateCategory/CreateSupplier tests if present).

**Interfaces:**
- Consumes: `IGenericWriteRepository<TEntity, Guid>.AddAsync`, `IUnitOfWork.SaveChangesAsync`, `CatalogTestContext.WriteRepo`/`UnitOfWork`.
- Produces: create handlers whose signature replaces `CatalogDbContext db` with `IGenericWriteRepository<TEntity, Guid> repository, IUnitOfWork unitOfWork`.

- [ ] **Step 1: Update `CreateProductHandlerTests.cs`** to build repo+uow over the real in-memory context (red — handler signature still takes `db`):

```csharp
using var db = CatalogTestContext.CreateInMemory();
var repository = CatalogTestContext.WriteRepo<Catalog.Domain.Entities.Product>(db);
var unitOfWork = CatalogTestContext.UnitOfWork(db);
var bus = Substitute.For<IMessageBus>();
var command = new CreateProductCommand("Widget", "A widget", null, "WIDGET-1", 9.99m, "USD");

var dto = await CreateProductHandler.Handle(command, repository, unitOfWork, bus, CancellationToken.None);
```

Leave the assertions (including `Assert.Equal(1, await db.Products.CountAsync())`) unchanged — `AddAsync` + `unitOfWork.SaveChangesAsync` persist to the same context.

- [ ] **Step 2: Run, expect FAIL** (`Handle` arity/parameter mismatch — does not compile).

- [ ] **Step 3: Rewrite `CreateProductHandler.cs`** body & signature:

```csharp
public static async Task<ProductDto> Handle(
    CreateProductCommand command,
    IGenericWriteRepository<Product, Guid> repository,
    IUnitOfWork unitOfWork,
    IMessageBus bus,
    CancellationToken ct)
{
    var product = Product.Create(
        string.Empty,
        command.Name,
        command.Description,
        command.CategoryId,
        command.Sku,
        new Money(command.SellPriceAmount, command.SellPriceCurrency));

    await repository.AddAsync(product, ct).ConfigureAwait(false);
    await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

    await bus.PublishAsync(new ProductCreatedIntegrationEvent(product)).ConfigureAwait(false);

    return product.ToDto();
}
```

Replace `using Catalog.Application.Database;` with `using SharedKernel.Core.Database;` (keep `Catalog.Domain.Entities;` for `Product`). Update the `<param>` docs: drop `db`, add `repository` and `unitOfWork`.

- [ ] **Step 4: Apply the identical transformation to `CreateCategoryHandler` and `CreateSupplierHandler`:**
  - `CreateCategoryHandler.Handle`: params `(CreateCategoryCommand command, IGenericWriteRepository<Category, Guid> repository, IUnitOfWork unitOfWork, CancellationToken ct)`; body `await repository.AddAsync(category, ct); await unitOfWork.SaveChangesAsync(ct); return category.ToDto();`. Swap `using Catalog.Application.Database;` → `using SharedKernel.Core.Database;`.
  - `CreateSupplierHandler.Handle`: params `(CreateSupplierCommand command, IGenericWriteRepository<Supplier, Guid> repository, IUnitOfWork unitOfWork, CancellationToken ct)`; body `await repository.AddAsync(supplier, ct); await unitOfWork.SaveChangesAsync(ct); return supplier.ToDto();`. Same using swap.
  - If `CreateCategoryHandlerTests`/`CreateSupplierHandlerTests` exist, update them to build `WriteRepo<Category>`/`WriteRepo<Supplier>` + `UnitOfWork` over a real `CreateInMemory()` context exactly as in Step 1.

- [ ] **Step 5: Run tests, expect PASS** — `nx test --project=catalog-unit-tests`. Build the solution green.

- [ ] **Step 6: Commit** — `git add -A && git commit -m "refactor(catalog): create handlers use write repository + unit of work"`

### Task 7: Migrate the load-mutate handlers (UpdateSellPrice, AddVariant, UpdateSupplierCost, SetPreferredSupplier, LinkVariantSupplier)

**Files:**
- Modify the five handlers under `Catalog.Application/{Products,Suppliers}/Features/**`.
- Test: `UpdateSellPriceHandlerTests.cs`, `AddVariantHandlerTests.cs`, `UpdateSupplierCostHandlerTests.cs`, `SetPreferredSupplierHandlerTests.cs`, `LinkVariantSupplierHandlerTests.cs`.

**Interfaces:**
- Consumes: `IGenericWriteRepository<Product, Guid>.FirstOrDefaultAsync(ISpecification<Product>, bool enableTracking, CancellationToken)`, `IUnitOfWork.SaveChangesAsync`.
- Produces: load-mutate handlers that replace `CatalogDbContext db` with `IGenericWriteRepository<Product, Guid> repository, IUnitOfWork unitOfWork`, load **tracked**, mutate, then commit via the unit of work.

**Recipe (applies to all five — they all load a `Product` aggregate, mutate it, save):**
- Replace the load `await db.Products.WithSpecification(spec).FirstOrDefaultAsync(ct)` with `await repository.FirstOrDefaultAsync(spec, enableTracking: true, ct)`.
- Replace `await db.SaveChangesAsync(ct)` with `await unitOfWork.SaveChangesAsync(ct)`.
- Drop `using Catalog.Application.Database;`, `using Ardalis.Specification.EntityFrameworkCore;`, `using Microsoft.EntityFrameworkCore;` (the `WithSpecification`/`FirstOrDefaultAsync` EF extensions are no longer used); add `using SharedKernel.Core.Database;`. Keep the spec's `ReadModels` using.
- Tracked load is required because the repo's spec methods default to `AsNoTracking`; `enableTracking: true` keeps the aggregate tracked so the mutation is persisted by `SaveChanges`.

- [ ] **Step 1: Update `UpdateSellPriceHandlerTests.cs`** ACT blocks (red). In each of the three facts, after constructing `db` (`CreateWithStubbedSave`/`CreateInMemory`), build deps and call the new signature:

```csharp
using var db = CatalogTestContext.CreateWithStubbedSave("price-change");
var repository = CatalogTestContext.WriteRepo<Catalog.Domain.Entities.Product>(db);
var unitOfWork = CatalogTestContext.UnitOfWork(db);
var bus = Substitute.For<IMessageBus>();
var command = new UpdateSellPriceCommand(product.Id, product.Variants[0].Id, 14.00m, "USD");

var result = await UpdateSellPriceHandler.Handle(command, repository, unitOfWork, bus, CancellationToken.None);
```

Keep seeding (`SeedAsync`) and all assertions unchanged. The write repo over the stubbed-save context queries the same named in-memory store (finds the seeded product) and `unitOfWork.SaveChangesAsync` invokes the stubbed `SaveChangesAsync` (returns 1, no persistence) — same behavior the test relied on before.

- [ ] **Step 2: Run, expect FAIL** (arity mismatch).

- [ ] **Step 3: Rewrite `UpdateSellPriceHandler.cs`:**

```csharp
public static async Task<ErrorOr<VariantDto>> Handle(
    UpdateSellPriceCommand command,
    IGenericWriteRepository<Product, Guid> repository,
    IUnitOfWork unitOfWork,
    IMessageBus bus,
    CancellationToken ct)
{
    var product = await repository
        .FirstOrDefaultAsync(new ProductByIdSpec(command.ProductId), enableTracking: true, ct)
        .ConfigureAwait(false);

    if (product is null)
    {
        return Error.NotFound(description: $"Product '{command.ProductId}' was not found.");
    }

    var variant = product.Variants.FirstOrDefault(v => v.Id == command.VariantId);
    if (variant is null)
    {
        return Error.NotFound(description: $"Variant '{command.VariantId}' was not found.");
    }

    product.ChangeVariantSellPrice(command.VariantId, new Money(command.Amount, command.Currency));
    await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

    var priceChange = product.DomainEvents.OfType<VariantSellPriceChanged>().LastOrDefault();
    if (priceChange is not null)
    {
        await bus.PublishAsync(new ProductPriceChangedIntegrationEvent(priceChange, product.TenantId)).ConfigureAwait(false);
    }

    return variant.ToVariantDto();
}
```

Update usings as per the recipe (`Product` is in `Catalog.Domain.Entities`; ensure that using is present).

- [ ] **Step 4: Apply the recipe to the other four**, updating each handler's signature (`IGenericWriteRepository<Product, Guid> repository, IUnitOfWork unitOfWork` in place of `CatalogDbContext db`, before any `IMessageBus`/`ct`), the load call, and the save call:
  - `AddVariantHandler` (has `IMessageBus bus`): spec `ProductByVariantSpec`? No — uses `ProductByIdSpec(command.ProductId)`. Load tracked; after `product.AddVariant(...)`, `await unitOfWork.SaveChangesAsync(ct)`; publish unchanged.
  - `UpdateSupplierCostHandler` (no bus): spec `ProductByVariantSpec(command.VariantId)`; after `product.ChangeSupplierCost(...)`, `await unitOfWork.SaveChangesAsync(ct)`.
  - `SetPreferredSupplierHandler` (no bus): spec `ProductByVariantSpec(command.VariantId)`; after `product.SetPreferredSupplier(...)`, save.
  - `LinkVariantSupplierHandler` (no bus): spec `ProductByVariantSpec(command.VariantId)`; after `product.LinkSupplier(...)`, save.
  - Update each corresponding `*HandlerTests.cs` ACT block exactly as in Step 1 (build `WriteRepo<Product>` + `UnitOfWork` over the test's existing context; keep seeding + assertions).

- [ ] **Step 5: Run tests, expect PASS** — `nx test --project=catalog-unit-tests`; solution build green.

- [ ] **Step 6: Commit** — `git add -A && git commit -m "refactor(catalog): load-mutate handlers use write repository + unit of work (tracked load)"`

### Task 8: Migrate the query handlers (GetProduct, ListProducts, GetSupplier, GetSupplierPriceHistory)

**Files:**
- Modify the four query handlers under `Catalog.Application/{Products,Suppliers}/Features/**`.
- Test: `ProductQueryHandlerTests.cs`, `GetSupplierHandlerTests.cs` (+ any GetSupplierPriceHistory test if present).

**Interfaces:**
- Consumes: `IGenericReadRepository<TEntity, Guid>.FirstOrDefaultAsync(ISpecification<TEntity>, CancellationToken)` and `.ListAsync(ISpecification<TEntity>, CancellationToken)` — same method names/shapes the handlers already call on Ardalis `IRepositoryBase<T>`.
- Produces: query handlers injecting `IGenericReadRepository<TEntity, Guid> repository` instead of `IRepositoryBase<TEntity> repository`.

- [ ] **Step 1: Update `ProductQueryHandlerTests.cs`** (red): change `Substitute.For<IRepositoryBase<Product>>()` → `Substitute.For<IGenericReadRepository<Product, System.Guid>>()`; replace `using Ardalis.Specification;`-based mock setup’s repo type only (the `ISpecification<Product>` argument matchers stay — keep `using Ardalis.Specification;` for `ISpecification`). For `ListProducts`, change the stubbed return to the interface’s return type:

```csharp
repository.ListAsync(Arg.Any<ISpecification<Product>>(), Arg.Any<CancellationToken>())
    .Returns(Task.FromResult<IReadOnlyList<Product>>([a, b]));
```

Add `using SharedKernel.Core.Database;`.

- [ ] **Step 2: Run, expect FAIL** (handler still takes `IRepositoryBase<Product>`).

- [ ] **Step 3: Rewrite `GetProductHandler.cs`** — change the parameter type and the using:

```csharp
using SharedKernel.Core.Database;
// …
public static async Task<ErrorOr<ProductDto>> Handle(
    GetProductQuery query,
    IGenericReadRepository<Product, Guid> repository,
    CancellationToken ct)
{
    var product = await repository.FirstOrDefaultAsync(new ProductByIdSpec(query.ProductId), ct).ConfigureAwait(false);

    return product is null
        ? Error.NotFound(description: $"Product '{query.ProductId}' was not found.")
        : product.ToDto();
}
```

Remove `using Ardalis.Specification;` only if nothing else needs it (the spec type is referenced by name, not the namespace, here — `ProductByIdSpec` comes from `Catalog.Application.Products.ReadModels`). Keep `Catalog.Domain.Entities;` for `Product`.

- [ ] **Step 4: Apply to the other three:**
  - `ListProductsHandler`: param `IGenericReadRepository<Product, Guid> repository`; body unchanged (`repository.ListAsync(new ProductsByCategorySpec(query.CategoryId), ct)`). Swap using to `SharedKernel.Core.Database`.
  - `GetSupplierHandler`: param `IGenericReadRepository<Supplier, Guid> repository`; body unchanged (`FirstOrDefaultAsync(new SupplierByIdSpec(...), ct)`).
  - `GetSupplierPriceHistoryHandler`: param `IGenericReadRepository<Product, Guid> repository`; body unchanged (`FirstOrDefaultAsync(new ProductByVariantSpec(...), ct)`).
  - Update `GetSupplierHandlerTests.cs` (and any price-history test) to `Substitute.For<IGenericReadRepository<Supplier, System.Guid>>()` / `<Product, System.Guid>` with `using SharedKernel.Core.Database;`.

- [ ] **Step 5: Run tests, expect PASS**; solution build green.

- [ ] **Step 6: Commit** — `git add -A && git commit -m "refactor(catalog): query handlers use IGenericReadRepository"`

---

## Phase 2 — Order adoption

### Task 9: Order context split + repositories + DI

**Files:**
- Create: `Order.Application/Database/OrderDbContextBase.cs`; Modify: `Order.Application/Database/OrderDbContext.cs`; Modify: `Order.Host/Database/OrderReadDbContext.cs`.
- Create: `Order.Host/Database/OrderWriteRepository.cs`, `OrderReadRepository.cs`, `OrderPersistenceExtensions.cs`; Modify: `Order.Host/Program.cs`.
- Test: `tests/unit/Order.UnitTests/` — add a `ReadContext_UsesNoTracking` style fact if an order DbContext test exists; otherwise rely on build + handler tests.

**Interfaces:** mirror Catalog Tasks 3–4 with `Orders.Application.Database` / `Orders.Host.Database` namespaces, entity `Order`, `DbSet<Order> Orders`.

- [ ] **Step 1: Create `OrderDbContextBase.cs`** (abstract; move `Orders` `DbSet` + `OnModelCreating` out of `OrderDbContext`):

```csharp
using Microsoft.EntityFrameworkCore;
using Finbuckle.MultiTenant.Abstractions;
using Orders.Domain.Entities;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.MultiTenant;

namespace Orders.Application.Database;

/// <summary>
/// Abstract order context that defines the entity model exactly once. The write and read contexts derive from it.
/// </summary>
/// <param name="options">The options used to configure the context.</param>
/// <param name="tenantContextAccessor">The accessor that provides the current tenant context.</param>
public abstract class OrderDbContextBase(DbContextOptions options, IMultiTenantContextAccessor<TenantDetails> tenantContextAccessor)
    : BaseDbContext(options, tenantAccessor: tenantContextAccessor)
{
    /// <summary>Gets the set of tracked orders.</summary>
    public DbSet<Order> Orders => Set<Order>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrderDbContextBase).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
```

- [ ] **Step 2: Replace `OrderDbContext.cs`** with the write leaf (`public class OrderDbContext(...) : OrderDbContextBase(options, tenantContextAccessor);`) and **change `OrderReadDbContext.cs`’s base** from `OrderDbContext(...)` to `OrderDbContextBase(...)` (keep its `OnConfiguring` NoTracking override).

- [ ] **Step 3: Create `OrderWriteRepository.cs`, `OrderReadRepository.cs`, `OrderPersistenceExtensions.cs`** mirroring Catalog Task 4 (entity constraint `where TEntity : BaseEntity` for write; `where TReadModel : class, IReadModel<TId>` for read; `AddOrderPersistence` with connection keys `OrderWrite`/`OrderRead`, `serviceName: "order"`, `UnitOfWork<OrderDbContext>`). Call `builder.AddOrderPersistence();` in `Order.Host/Program.cs`.

- [ ] **Step 4: Build green** — `dotnet build Teck.Platform.slnx`.

- [ ] **Step 5: Commit** — `git add -A && git commit -m "feat(order): context base+sibling split, generic repositories, unit of work"`

### Task 10: Migrate order handlers + tests

**Files:**
- Modify: `CreateOrderHandler.cs`, `GetOrderHandler.cs`.
- Test: `tests/unit/Order.UnitTests/CreateOrderHandlerTests.cs`, `GetOrderHandlerTests.cs`.

**Interfaces:** as Catalog Tasks 6 & 8, entity `Order`.

- [ ] **Step 1: `CreateOrderHandlerTests.cs`** — build `IGenericWriteRepository<Order, Guid>` + `IUnitOfWork` over the test’s real in-memory `OrderDbContext` (mirror `CatalogTestContext.WriteRepo`/`UnitOfWork`; if Order tests construct the context inline, add equivalent local helpers or inline `new GenericWriteRepository<Order, Guid, OrderDbContext>(db, Substitute.For<IHttpContextAccessor>())` and `new UnitOfWork<OrderDbContext>(db)`). Call the new `Handle(command, repository, unitOfWork, bus, ct)`. (red)

- [ ] **Step 2: Run, expect FAIL.**

- [ ] **Step 3: Rewrite `CreateOrderHandler.cs`** — params `(CreateOrderCommand command, IGenericWriteRepository<Order, Guid> repository, IUnitOfWork unitOfWork, IMessageBus bus, CancellationToken ct)`; body: build order, `await repository.AddAsync(order, ct); await unitOfWork.SaveChangesAsync(ct); await bus.PublishAsync(...); return OrderMapper.ToDto(order);`. Swap `using Orders.Application.Database;` → `using SharedKernel.Core.Database;`.

- [ ] **Step 4: `GetOrderHandler.cs`** — change param `IRepositoryBase<Order>` → `IGenericReadRepository<Order, Guid>` (`using SharedKernel.Core.Database;`); body unchanged. Update `GetOrderHandlerTests.cs` to `Substitute.For<IGenericReadRepository<Order, System.Guid>>()`.

- [ ] **Step 5: Run tests, expect PASS**; solution build green.

- [ ] **Step 6: Commit** — `git add -A && git commit -m "refactor(order): handlers use repository + unit of work"`

---

## Phase 3 — Enforcement + documentation

### Task 11: Architecture rule forbidding DbContext/IRepositoryBase in Application

**Files:**
- Modify: `tests/architecture/Order.Architecture.UnitTests/OrderArchitectureTests.cs`
- Create: `tests/architecture/Catalog.Architecture.UnitTests/` (csproj mirroring Order’s + `CatalogArchitectureTests.cs`); add the project to `Teck.Platform.slnx`.

**Interfaces:**
- Consumes: `ArchUnitNET` fluent API; `ApplicationAssembly` (already defined in `OrderArchitectureTests`).
- Produces: a `[Fact]` per service asserting Application types depend on neither `Microsoft.EntityFrameworkCore.DbContext` (nor its subclasses) nor `Ardalis.Specification.IRepositoryBase<>`.

- [ ] **Step 1: Add the failing fact** to `OrderArchitectureTests.cs` (it will FAIL only if a handler still injects a context/`IRepositoryBase` — after Phase 2 it should already pass, so to see red, add it *before* finishing, or verify it guards by temporarily reverting one handler). Concrete rule:

```csharp
[Fact]
public void OrderApplication_ShouldNotDependOnDbContextOrAardalisRepository()
{
    Types()
        .That()
        .ResideInAssembly(ApplicationAssembly)
        .Should()
        .NotDependOnAny(Types().That().AreAssignableTo(typeof(Microsoft.EntityFrameworkCore.DbContext)))
        .AndShould()
        .NotDependOnAny(Types().That().HaveFullNameContaining("Ardalis.Specification.IRepositoryBase"))
        .Because("application handlers must use SharedKernel repository + unit-of-work abstractions, not a concrete DbContext or Ardalis IRepositoryBase")
        .Check(OrderArchitecture);
}
```

(Add `using static ArchUnitNET.Fluent.ArchRuleDefinition;` is already present.)

- [ ] **Step 2: Run, expect PASS** (handlers already migrated). To prove the guard bites, temporarily re-add `OrderDbContext db` to `GetOrderHandler`, run → FAIL, revert → PASS.

- [ ] **Step 3: Create `Catalog.Architecture.UnitTests`** mirroring `Order.Architecture.UnitTests` (same csproj refs pattern + `SharedTestBase`), with `CatalogArchitectureTests` loading Catalog Domain/Application/Host assemblies and the same fact (`CatalogApplication_ShouldNotDependOnDbContextOrAardalisRepository`). Register the project in `Teck.Platform.slnx` and Nx.

- [ ] **Step 4: Run both arch test projects, expect PASS**; full `dotnet build` + `nx affected -t test` green.

- [ ] **Step 5: Commit** — `git add -A && git commit -m "test(arch): forbid DbContext/IRepositoryBase dependencies in Application layer"`

### Task 12: Rewrite the persistence conventions in docs

**Files:**
- Modify: `src/services/AGENTS.md` (canonical), `CLAUDE.md`, `src/services/commerce/order/AGENTS.md`, `src/services/commerce/catalog/AGENTS.md` (whichever restate persistence).
- Modify: `/home/jacob/.claude/projects/-home-jacob-workspace-Infrastructure-repos-Teck-Monorepo/memory/catalog-service-design-and-plans.md` + `MEMORY.md` pointer.

- [ ] **Step 1:** In `src/services/AGENTS.md` "Code Style"/CQRS sections and `CLAUDE.md` "Architecture rules" + "Messaging & migrations", replace the guidance that says *"The DbContext is the unit of work — no IUnitOfWork abstraction"* and *direct-DbContext writes* with the new rule:
  - Handlers depend only on `IGenericReadRepository<T,TId>`, `IGenericWriteRepository<T,TId>`, `IUnitOfWork` (SharedKernel.Core.Database).
  - `IUnitOfWork.SaveChangesAsync` is the single commit point; repositories only track.
  - Each service: abstract `{Service}DbContextBase` (model once) + `{Service}DbContext` (write) + `{Service}ReadDbContext` (NoTracking); per-service `*WriteRepository`/`*ReadRepository` subclasses registered as open generics; `UnitOfWork<{Service}DbContext>` bound to the scoped write context.
  - Command handlers load-to-mutate **tracked** (`enableTracking: true`); creates use `AddAsync` + `SaveChangesAsync`.
  - Record the rationale (consistency/testability/explicit transactions/swappability) so a future agent does not "simplify" back to direct DbContext. Reference `docs/superpowers/specs/2026-06-26-repository-unitofwork-architecture-design.md`.

- [ ] **Step 2:** Update the catalog memory note to mark this plan done and point at the spec/plan files; keep `MEMORY.md` to one line.

- [ ] **Step 3:** No code change → verify docs render (skim) and `dotnet build` still green.

- [ ] **Step 4: Commit** — `git add -A && git commit -m "docs: mandate repository + unit-of-work persistence pattern; reverse prior DbContext-as-UoW guidance"`

---

## Self-Review

**Spec coverage:**
- Adopt `IGenericReadRepository`/`IGenericWriteRepository`/`IUnitOfWork` → Tasks 4, 6–10. ✓
- `IUnitOfWork` owns SaveChanges; remove from write repo → Task 2; UoW bound to scoped write context → Task 4/9. ✓
- `IBaseEntity : IReadModel` → Task 1. ✓
- Abstract base + sibling contexts; create `CatalogReadDbContext` → Tasks 3, 9. ✓
- Fix `Excecut*` typos → Task 2. ✓
- ArchUnit enforcement → Task 11; docs reversal → Task 12. ✓
- Tracking-on-load convention → Global Constraints + Task 7 (tracked load). ✓
- Context placement / layering (base+write in Application, read+repos in Host) → File Structure + Tasks 3/4/9. ✓
- Migrations source = write context → Task 4 (`migrationsAssembly: typeof(Program).Assembly`; write context unchanged as the EF target). ✓

**Placeholder scan:** No "TBD"/"add error handling". Handler bodies are shown in full or specified as exact param/spec/save deltas over code quoted in this plan. The two repetitive families (creates, load-mutates, queries) give one full worked handler + an explicit per-handler delta list — each is fully determined, not hand-waved.

**Type consistency:** `IGenericWriteRepository<TEntity, Guid>` / `IGenericReadRepository<TEntity, Guid>` / `IUnitOfWork` / `UnitOfWork<{Service}DbContext>` used consistently; repo subclasses `*WriteRepository`/`*ReadRepository` match the names `RepositoryRules` already scans for; `enableTracking: true` matches the `FirstOrDefaultAsync(ISpecification<T>, bool, CancellationToken)` overload that exists on both interface and impl.

**Risks / notes for the executor:**
- Runtime DI (`AddHybridMultiTenantDbContexts`) is *new* wiring (the repo had none). It compiles and is structurally correct; real DB verification is deferred to integration tests. Confirm the connection-string keys (`CatalogWrite`/`CatalogRead`, `OrderWrite`/`OrderRead`) against the deploy config when first run.
- The `CreateWithStubbedSave` tests depend on the write repo reading the same named in-memory store the seed wrote — preserved because the repo is built over the same context instance.
- If `Catalog.UnitTests` referencing `Catalog.Host` (Task 3 test) is undesirable, move the `ReadContext_UsesNoTracking` fact into the new `Catalog.Architecture.UnitTests`/a Host-level test instead; the rest of the catalog unit tests do **not** need a Host reference (they build repos from `SharedKernel.Infrastructure` generics over `CatalogDbContext`).
