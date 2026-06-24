# Catalog Service — Plan 2: Application Layer

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement a fully unit-tested `Catalog.Application` layer — DTOs, Mapperly mappers, Ardalis specifications, WolverineFx command/query handlers for the Products and Suppliers capabilities, the three integration events, and the write `CatalogDbContext` + EF configurations — leaving a green `Catalog.Application` assembly and a green `Catalog.UnitTests` suite.

**Architecture:** Mirrors the `order` reference service's Application layer (folder-per-capability, static WolverineFx handlers, `ICommand<T>`/`IQuery<T>` marker interfaces, `IRepositoryBase<T>` for reads, the concrete write `DbContext` for writes). Builds on Plan 1's Domain layer (`Product` aggregate + `Category`/`Supplier` roots + `Money` VO + domain events). The Host layer (read context, migrations, endpoints, DI, integration/arch tests, deploy) follows in Plan 3.

**Tech Stack:** .NET 10, C# (nullable, implicit usings), WolverineFx (mediator), Ardalis.Specification, Riok.Mapperly (compile-time mapping), ErrorOr, MemoryPack (integration events), EF Core (write context), xUnit v3 + NSubstitute + EF Core InMemory (tests).

## Global Constraints

- **Target framework:** `net10.0`; `Nullable=enable`; `ImplicitUsings=enable`.
- **Layer direction:** Application references **Domain + SharedKernel.Core/.Events/.Infrastructure only** — never Host. (Enforced by ArchUnitNET in Plan 3.)
- **CQRS markers:** commands are `sealed record … : ICommand<TResponse>`; queries are `sealed record … : IQuery<TResponse>` (`SharedKernel.Core.CQRS`). Both are **immutable** (records).
- **Handlers are static classes named `{Feature}Handler`** with a single `public static … Handle(…)` method, discovered by WolverineFx by convention. They do **not** implement `ICommandHandler<,>`/`IQueryHandler<,>` (matching `order`); the arch rules targeting those interfaces therefore do not apply.
- **Writes** inject the concrete `CatalogDbContext`; the DbContext is the unit of work — call `SaveChangesAsync()` once, no `IUnitOfWork`. **Pure-create** command handlers return the DTO directly (matching `order`); command handlers that **load an existing aggregate** (and can therefore miss) return `ErrorOr<T>` so absence is a clean `NotFound`.
- **Reads** inject `IRepositoryBase<TAggregateRoot>` (Ardalis); query handlers return `ErrorOr<T>` and use a `Specification` from `ReadModels/` — **no query LINQ in handlers**. (Navigating an already-loaded aggregate in memory — e.g. `product.Variants.First(...)` — is not query LINQ and is allowed in both command and query handlers and in mapper methods.)
- **Mapping via Mapperly only** (`[Mapper]` static partial classes in `Mapping/`); never hand-write entity↔DTO mapping in handlers or endpoints. (In-memory navigation of an already-loaded aggregate inside a mapper method is allowed.)
- **tenantId:** handlers pass `string.Empty` to the domain `Create` factories; the Host's tenant interceptor stamps the real tenant on `SaveChangesAsync` (matching `order`). No factory-level tenant guard.
- **Test naming:** `Method_WhenCondition_ExpectedResult`; Arrange-Act-Assert.
- **Commit cadence:** one commit per completed task. Conventional commits (`feat(catalog): …`). No git tags / no `nx release` from this branch.

### Reference build/test commands

```bash
# Build the Application layer (and its deps):
dotnet build src/services/commerce/catalog/Catalog.Application/Catalog.Application.csproj -v q -clp:ErrorsOnly
# Run the catalog unit tests:
dotnet test tests/unit/Catalog.UnitTests/Catalog.UnitTests.csproj -v q
```

---

## Deviations from the approved spec (read before starting)

These three choices diverge from `docs/superpowers/specs/2026-06-23-catalog-service-design.md`. Each is forced or strongly justified by what actually compiles against the reference `order` service; all are reversible and are flagged here for transparency.

1. **Write `CatalogDbContext` + EF configurations live in `Catalog.Application/Database/`, not `Catalog.Host`.** A C# `partial class` cannot span two assemblies, so the spec's "declared in Application, implemented in Host" is not compilable as written. `order` has two same-named `OrderDbContext` classes (one in Application, one in Host) — a `CS0433` collision that contributes to `order.Host` not building. Because write handlers must inject the **concrete** context (per the unit-of-work convention) and Application must not reference Host, the single canonical write context must live in Application. Catalog's owned-aggregate tree (`Product`→`Variant`→`VariantSupplier`→`SupplierPriceHistory`) and the `Money` value object also require EF configuration for the model to build at all (even under the InMemory test provider), so the configurations travel with the context. **Plan 3 adds `CatalogReadDbContext : CatalogDbContext`, Npgsql wiring, and migrations in Host — it must not redefine `CatalogDbContext`.**

2. **Command handlers publish integration events directly** after `SaveChangesAsync` (mirroring `order`'s `CreateOrderHandler`), rather than through a separate `EventHandlers/DomainEvents/` translation layer. The repo has no wired domain-event→Wolverine dispatch behavior, so a translation layer would either never fire or risk double-publishing alongside the direct publish. Domain events remain in-process aggregate signals (raised by the Domain, asserted in Plan 1's tests, and read by the command handler to build the integration event). An auto-dispatch refactor can be added in Plan 3/4 once the dispatch behavior is confirmed.

3. **`Money` is mapped to flat `…Amount`/`…Currency` columns** (owned type) and flattened to flat DTO fields by Mapperly's name convention (`SellPrice.Amount` → `SellPriceAmount`). The owned-tree EF configuration is validated for real against Postgres by Plan 3's migration + Testcontainers integration tests; Plan 2 only asserts the model builds and the handlers round-trip on the InMemory provider.

---

## File Structure (this plan)

```
src/services/commerce/catalog/Catalog.Application/
  Catalog.Application.csproj                 (Modify: add Microsoft.EntityFrameworkCore + Ardalis.Specification.EntityFrameworkCore if not transitive)
  Options/CatalogOptions.cs                  (Create)
  Database/CatalogDbContext.cs               (Create — : BaseDbContext, DbSets)
  Database/Configurations/ProductConfiguration.cs    (Create — owned tree + Money)
  Database/Configurations/CategoryConfiguration.cs   (Create)
  Database/Configurations/SupplierConfiguration.cs   (Create)
  Products/
    Responses/{ProductDto,VariantDto,VariantAttributeDto,CategoryDto,ProductSummaryDto}.cs
    Mapping/ProductMapper.cs
    ReadModels/{ProductByIdSpec,ProductsByCategorySpec}.cs
    IntegrationEvents/{ProductCreatedIntegrationEvent,VariantCreatedIntegrationEvent,ProductPriceChangedIntegrationEvent}.cs
    Features/CreateCategory/V1/{CreateCategoryCommand,CreateCategoryHandler}.cs
    Features/CreateProduct/V1/{CreateProductCommand,CreateProductHandler}.cs
    Features/AddVariant/V1/{AddVariantCommand,AddVariantHandler}.cs
    Features/UpdateSellPrice/V1/{UpdateSellPriceCommand,UpdateSellPriceHandler}.cs
    Features/GetProduct/V1/{GetProductQuery,GetProductHandler}.cs
    Features/ListProducts/V1/{ListProductsQuery,ListProductsHandler}.cs
  Suppliers/
    Responses/{SupplierDto,VariantSupplierDto,SupplierPriceHistoryDto}.cs
    Mapping/SupplierMapper.cs
    ReadModels/{SupplierByIdSpec,ProductByVariantSpec}.cs
    Features/CreateSupplier/V1/{CreateSupplierCommand,CreateSupplierHandler}.cs
    Features/GetSupplier/V1/{GetSupplierQuery,GetSupplierHandler}.cs
    Features/LinkVariantSupplier/V1/{LinkVariantSupplierCommand,LinkVariantSupplierHandler}.cs
    Features/UpdateSupplierCost/V1/{UpdateSupplierCostCommand,UpdateSupplierCostHandler}.cs
    Features/SetPreferredSupplier/V1/{SetPreferredSupplierCommand,SetPreferredSupplierHandler}.cs
    Features/GetSupplierPriceHistory/V1/{GetSupplierPriceHistoryQuery,GetSupplierPriceHistoryHandler}.cs

tests/unit/Catalog.UnitTests/
  TestContext/CatalogTestContext.cs          (Create — InMemory CatalogDbContext factory)
  Application/… test files (one per task)
```

> **Note on package references:** `Catalog.Application.csproj` needs **no** new packages — `Microsoft.EntityFrameworkCore`, `Ardalis.Specification(.EntityFrameworkCore)`, `Finbuckle.MultiTenant.EntityFrameworkCore`, and `MemoryPack` all arrive transitively through the `SharedKernel.Infrastructure` project reference (verified). `Microsoft.EntityFrameworkCore.InMemory` is already in `Catalog.UnitTests.csproj` from Plan 1. If, and only if, a `[MemoryPackable]` type fails to resolve `MemoryPack`, add `<PackageReference Include="MemoryPack" />` to `Catalog.Application.csproj` (version is centrally managed).

---

# Phase 1 — Application Plumbing (Options, DbContext, EF configurations)

### Task 1.1: CatalogOptions + write CatalogDbContext + EF configurations

**Files:**
- Create: `src/services/commerce/catalog/Catalog.Application/Options/CatalogOptions.cs`
- Create: `src/services/commerce/catalog/Catalog.Application/Database/CatalogDbContext.cs`
- Create: `src/services/commerce/catalog/Catalog.Application/Database/Configurations/ProductConfiguration.cs`
- Create: `src/services/commerce/catalog/Catalog.Application/Database/Configurations/CategoryConfiguration.cs`
- Create: `src/services/commerce/catalog/Catalog.Application/Database/Configurations/SupplierConfiguration.cs`
- Create: `tests/unit/Catalog.UnitTests/TestContext/CatalogTestContext.cs`
- Create: `tests/unit/Catalog.UnitTests/Application/CatalogDbContextTests.cs`

**Interfaces:**
- Produces:
  - `Catalog.Application.Options.CatalogOptions` — `const string SectionName = "Catalog"`; `string DefaultCurrency { get; set; } = "USD"`.
  - `Catalog.Application.Database.CatalogDbContext : BaseDbContext` — ctor `(DbContextOptions options, IMultiTenantContextAccessor<TenantDetails> tenantContextAccessor)`; `DbSet<Product> Products`, `DbSet<Category> Categories`, `DbSet<Supplier> Suppliers`; applies all `IEntityTypeConfiguration` from the Application assembly.
  - `Catalog.UnitTests.TestContext.CatalogTestContext.CreateInMemory(string? name = null)` → a real `CatalogDbContext` over the InMemory provider with a substituted tenant accessor (used by all later command-handler tests).

- [ ] **Step 1: Create `CatalogOptions`**

Create `src/services/commerce/catalog/Catalog.Application/Options/CatalogOptions.cs`:

```csharp
namespace Catalog.Application.Options;

/// <summary>Service configuration for the catalog (bound via the Options pattern).</summary>
public sealed class CatalogOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Catalog";

    /// <summary>The default ISO currency code used when none is supplied.</summary>
    public string DefaultCurrency { get; set; } = "USD";
}
```

- [ ] **Step 2: Create the write `CatalogDbContext`**

Create `src/services/commerce/catalog/Catalog.Application/Database/CatalogDbContext.cs`. It derives `BaseDbContext` (tenant filter + soft-delete + tenant enforcement on save) exactly like `order`'s Host context, but is the **single canonical** catalog context and lives in Application so handlers can inject it:

```csharp
using Catalog.Domain.Entities;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.MultiTenant;

namespace Catalog.Application.Database;

/// <summary>
/// The catalog write context (tracked). The DbContext is the unit of work.
/// Plan 3 adds <c>CatalogReadDbContext : CatalogDbContext</c> (NoTracking) + Npgsql + migrations in the Host.
/// </summary>
public class CatalogDbContext(DbContextOptions options, IMultiTenantContextAccessor<TenantDetails> tenantContextAccessor)
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
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);
    }
}
```

- [ ] **Step 3: Create `ProductConfiguration` (the owned-aggregate tree + Money)**

Create `src/services/commerce/catalog/Catalog.Application/Database/Configurations/ProductConfiguration.cs`. Relational settings (`ToTable`/`HasColumnName`/`HasPrecision`) are ignored by the InMemory test provider but define the real Postgres schema for Plan 3; the owned-type **structure** and field access are what the model build needs:

```csharp
using Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Application.Database.Configurations;

/// <summary>Maps the <see cref="Product"/> aggregate and its owned variant/supplier/history tree.</summary>
public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.TenantId).HasMaxLength(64);
        builder.Property(p => p.Name).HasMaxLength(256);
        builder.Property(p => p.Description).HasMaxLength(2048);
        builder.Ignore(p => p.DomainEvents);

        builder.OwnsMany(p => p.Variants, variant =>
        {
            variant.ToTable("Variants");
            variant.WithOwner().HasForeignKey("ProductId");
            variant.HasKey(v => v.Id);
            variant.Property(v => v.Sku).HasMaxLength(128);

            variant.OwnsOne(v => v.SellPrice, money =>
            {
                money.Property(m => m.Amount).HasColumnName("SellPriceAmount").HasPrecision(18, 2);
                money.Property(m => m.Currency).HasColumnName("SellPriceCurrency").HasMaxLength(3);
            });
            variant.Navigation(v => v.SellPrice).IsRequired();

            variant.OwnsMany(v => v.Attributes, attr =>
            {
                attr.ToTable("VariantAttributes");
                attr.WithOwner().HasForeignKey("VariantId");
                attr.Property(a => a.Name).HasMaxLength(128);
                attr.Property(a => a.Value).HasMaxLength(512);
            });
            variant.Navigation(v => v.Attributes).UsePropertyAccessMode(PropertyAccessMode.Field);

            variant.OwnsMany(v => v.Suppliers, link =>
            {
                link.ToTable("VariantSuppliers");
                link.WithOwner().HasForeignKey("VariantId");
                link.HasKey(l => l.Id);
                link.Property(l => l.SupplierSku).HasMaxLength(128);

                link.OwnsOne(l => l.CostPrice, money =>
                {
                    money.Property(m => m.Amount).HasColumnName("CostPriceAmount").HasPrecision(18, 2);
                    money.Property(m => m.Currency).HasColumnName("CostPriceCurrency").HasMaxLength(3);
                });
                link.Navigation(l => l.CostPrice).IsRequired();

                link.OwnsMany(l => l.PriceHistory, hist =>
                {
                    hist.ToTable("SupplierPriceHistory");
                    hist.WithOwner().HasForeignKey("VariantSupplierId");
                    hist.HasKey(h => h.Id);

                    hist.OwnsOne(h => h.CostPrice, money =>
                    {
                        money.Property(m => m.Amount).HasColumnName("CostPriceAmount").HasPrecision(18, 2);
                        money.Property(m => m.Currency).HasColumnName("CostPriceCurrency").HasMaxLength(3);
                    });
                    hist.Navigation(h => h.CostPrice).IsRequired();
                });
                link.Navigation(l => l.PriceHistory).UsePropertyAccessMode(PropertyAccessMode.Field);
            });
            variant.Navigation(v => v.Suppliers).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        builder.Navigation(p => p.Variants).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
```

- [ ] **Step 4: Create `CategoryConfiguration` and `SupplierConfiguration`**

Create `src/services/commerce/catalog/Catalog.Application/Database/Configurations/CategoryConfiguration.cs`:

```csharp
using Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Application.Database.Configurations;

/// <summary>Maps the <see cref="Category"/> aggregate root (self-referencing hierarchy).</summary>
public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.TenantId).HasMaxLength(64);
        builder.Property(c => c.Name).HasMaxLength(256);
        builder.Property(c => c.Slug).HasMaxLength(256);
        builder.HasIndex(c => new { c.TenantId, c.Slug });
        builder.Ignore(c => c.DomainEvents);
    }
}
```

Create `src/services/commerce/catalog/Catalog.Application/Database/Configurations/SupplierConfiguration.cs`:

```csharp
using Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Application.Database.Configurations;

/// <summary>Maps the <see cref="Supplier"/> aggregate root.</summary>
public sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("Suppliers");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.TenantId).HasMaxLength(64);
        builder.Property(s => s.Name).HasMaxLength(256);
        builder.Property(s => s.ContactEmail).HasMaxLength(320);
        builder.Property(s => s.ContactPhone).HasMaxLength(64);
        builder.Ignore(s => s.DomainEvents);
    }
}
```

- [ ] **Step 5: Create the test-context helper**

Create `tests/unit/Catalog.UnitTests/TestContext/CatalogTestContext.cs`:

```csharp
using Catalog.Application.Database;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using SharedKernel.Infrastructure.MultiTenant;

namespace Catalog.UnitTests.TestContext;

/// <summary>Builds a real <see cref="CatalogDbContext"/> over the EF Core InMemory provider for handler tests.</summary>
public static class CatalogTestContext
{
    /// <summary>Creates an isolated in-memory catalog context.</summary>
    public static CatalogDbContext CreateInMemory(string? name = null)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(name ?? $"catalog-{Guid.NewGuid()}")
            .Options;

        // BaseDbContext reads tenantAccessor?.MultiTenantContext.TenantInfo; NSubstitute's recursive
        // mocking returns a non-null context with a null TenantInfo, so TenantDetails resolves to null.
        // SaveChangesAsync's tenant enforcement is a no-op for entities not marked with Finbuckle's
        // [MultiTenant] attribute, so seeding and saving in tests works without a real tenant.
        var accessor = Substitute.For<IMultiTenantContextAccessor<TenantDetails>>();
        return new CatalogDbContext(options, accessor);
    }
}
```

- [ ] **Step 6: Write the failing model-build test**

Create `tests/unit/Catalog.UnitTests/Application/CatalogDbContextTests.cs`:

```csharp
using Catalog.Domain.Entities;
using Catalog.Domain.ValueObjects;
using Catalog.UnitTests.TestContext;
using Xunit;

namespace Catalog.UnitTests.Application;

public sealed class CatalogDbContextTests
{
    [Fact]
    public void Model_BuildsWithoutError()
    {
        using var db = CatalogTestContext.CreateInMemory();

        // Accessing the model forces EF to build the owned-aggregate tree + Money mappings.
        Assert.NotNull(db.Model);
        Assert.NotNull(db.Model.FindEntityType(typeof(Product)));
    }

    [Fact]
    public async Task SaveAndReload_RoundTripsProductAggregate()
    {
        var product = Product.Create("tenant-1", "Widget", "desc", null, "WIDGET-1", new Money(9.99m, "USD"));
        product.LinkSupplier(product.Variants[0].Id, Guid.NewGuid(), new Money(5m, "USD"), "ACME-1", 7, 10, isPreferred: true);

        using (var db = CatalogTestContext.CreateInMemory("roundtrip"))
        {
            db.Products.Add(product);
            await db.SaveChangesAsync();
        }

        using (var db = CatalogTestContext.CreateInMemory("roundtrip"))
        {
            var reloaded = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .FirstOrDefaultAsync(db.Products);
            Assert.NotNull(reloaded);
            var variant = Assert.Single(reloaded!.Variants);
            Assert.Equal("WIDGET-1", variant.Sku);
            Assert.Equal(9.99m, variant.SellPrice.Amount);
            var link = Assert.Single(variant.Suppliers);
            Assert.Single(link.PriceHistory);
        }
    }
}
```

- [ ] **Step 7: Run to verify failure**

Run: `dotnet test tests/unit/Catalog.UnitTests/Catalog.UnitTests.csproj -v q`
Expected: FAIL — `CatalogDbContext`/`CatalogOptions` do not exist (compile error).

- [ ] **Step 8: Run to verify pass**

After Steps 1-5 are in place, run: `dotnet test tests/unit/Catalog.UnitTests/Catalog.UnitTests.csproj -v q`
Expected: PASS (both new tests, plus all Plan 1 domain tests).

> If `Model_BuildsWithoutError` throws on the owned tree, the EF message names the exact navigation; the usual fix is an explicit `.HasKey(...)`/`.WithOwner().HasForeignKey(...)` or `.UsePropertyAccessMode(PropertyAccessMode.Field)` on the named navigation. The InMemory provider ignores the relational `ToTable`/column settings, so those are never the cause.

- [ ] **Step 9: Commit**

```bash
git add src/services/commerce/catalog/Catalog.Application/Options \
        src/services/commerce/catalog/Catalog.Application/Database \
        tests/unit/Catalog.UnitTests/TestContext \
        tests/unit/Catalog.UnitTests/Application/CatalogDbContextTests.cs
git commit -m "feat(catalog): add CatalogOptions, write CatalogDbContext, and EF configurations"
```

---

# Phase 2 — Products Capability

### Task 2.1: Product DTOs + ProductMapper

**Files:**
- Create: `…/Catalog.Application/Products/Responses/ProductDto.cs`
- Create: `…/Catalog.Application/Products/Responses/VariantDto.cs`
- Create: `…/Catalog.Application/Products/Responses/VariantAttributeDto.cs`
- Create: `…/Catalog.Application/Products/Responses/CategoryDto.cs`
- Create: `…/Catalog.Application/Products/Responses/ProductSummaryDto.cs`
- Create: `…/Catalog.Application/Products/Mapping/ProductMapper.cs`
- Create: `tests/unit/Catalog.UnitTests/Application/ProductMapperTests.cs`

**Interfaces:**
- Produces:
  - `ProductDto(Guid Id, string Name, string? Description, Guid? CategoryId, bool IsActive, IReadOnlyList<VariantDto> Variants)`
  - `VariantDto(Guid Id, string Sku, decimal SellPriceAmount, string SellPriceCurrency, bool IsDefault, bool IsActive, IReadOnlyList<VariantAttributeDto> Attributes)`
  - `VariantAttributeDto(string Name, string Value)`
  - `CategoryDto(Guid Id, string Name, string Slug, Guid? ParentId)`
  - `ProductSummaryDto(Guid Id, string Name, bool IsActive, Guid? CategoryId)`
  - `ProductMapper` (`[Mapper]`): `ProductDto ToDto(this Product)`, `VariantDto ToVariantDto(this Variant)`, `CategoryDto ToDto(this Category)`, `IReadOnlyList<ProductSummaryDto> ToSummaries(this IEnumerable<Product>)`. Mapperly auto-flattens `SellPrice.Amount` → `SellPriceAmount` and maps the owned collections.

- [ ] **Step 1: Create the DTOs**

`Products/Responses/ProductDto.cs`:
```csharp
namespace Catalog.Application.Products.Responses;

/// <summary>A product with its variants.</summary>
public sealed record ProductDto(
    Guid Id,
    string Name,
    string? Description,
    Guid? CategoryId,
    bool IsActive,
    IReadOnlyList<VariantDto> Variants);
```

`Products/Responses/VariantDto.cs`:
```csharp
namespace Catalog.Application.Products.Responses;

/// <summary>A sellable variant with its flattened sell price.</summary>
public sealed record VariantDto(
    Guid Id,
    string Sku,
    decimal SellPriceAmount,
    string SellPriceCurrency,
    bool IsDefault,
    bool IsActive,
    IReadOnlyList<VariantAttributeDto> Attributes);
```

`Products/Responses/VariantAttributeDto.cs`:
```csharp
namespace Catalog.Application.Products.Responses;

/// <summary>A name/value variant attribute.</summary>
public sealed record VariantAttributeDto(string Name, string Value);
```

`Products/Responses/CategoryDto.cs`:
```csharp
namespace Catalog.Application.Products.Responses;

/// <summary>A category in the hierarchy.</summary>
public sealed record CategoryDto(Guid Id, string Name, string Slug, Guid? ParentId);
```

`Products/Responses/ProductSummaryDto.cs`:
```csharp
namespace Catalog.Application.Products.Responses;

/// <summary>A lightweight product list item.</summary>
public sealed record ProductSummaryDto(Guid Id, string Name, bool IsActive, Guid? CategoryId);
```

- [ ] **Step 2: Write the failing mapper test**

`tests/unit/Catalog.UnitTests/Application/ProductMapperTests.cs`:
```csharp
using Catalog.Application.Products.Mapping;
using Catalog.Domain.Entities;
using Catalog.Domain.ValueObjects;
using Xunit;

namespace Catalog.UnitTests.Application;

public sealed class ProductMapperTests
{
    [Fact]
    public void ToDto_FlattensVariantSellPriceAndAttributes()
    {
        var product = Product.Create("tenant-1", "Widget", "desc", null, "WIDGET-1", new Money(9.99m, "USD"));
        product.AddVariant("WIDGET-2", new Money(12.50m, "USD"), [new VariantAttribute("Size", "Large")]);

        var dto = product.ToDto();

        Assert.Equal(product.Id, dto.Id);
        Assert.Equal("Widget", dto.Name);
        Assert.Equal(2, dto.Variants.Count);
        var defaultVariant = dto.Variants.Single(v => v.IsDefault);
        Assert.Equal("WIDGET-1", defaultVariant.Sku);
        Assert.Equal(9.99m, defaultVariant.SellPriceAmount);
        Assert.Equal("USD", defaultVariant.SellPriceCurrency);
        var added = dto.Variants.Single(v => !v.IsDefault);
        Assert.Equal("Large", Assert.Single(added.Attributes).Value);
    }

    [Fact]
    public void ToDto_MapsCategory()
    {
        var category = Category.Create("tenant-1", "Beverages", "beverages");

        var dto = category.ToDto();

        Assert.Equal(category.Id, dto.Id);
        Assert.Equal("Beverages", dto.Name);
        Assert.Equal("beverages", dto.Slug);
        Assert.Null(dto.ParentId);
    }

    [Fact]
    public void ToSummaries_MapsEachProduct()
    {
        var a = Product.Create("tenant-1", "A", null, null, "A-1", new Money(1m, "USD"));
        var b = Product.Create("tenant-1", "B", null, null, "B-1", new Money(2m, "USD"));

        var summaries = new[] { a, b }.ToSummaries();

        Assert.Equal(2, summaries.Count);
        Assert.Contains(summaries, s => s.Name == "A");
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/unit/Catalog.UnitTests/Catalog.UnitTests.csproj -v q`
Expected: FAIL — `ProductMapper`/DTOs do not exist.

- [ ] **Step 4: Implement the mapper**

`Products/Mapping/ProductMapper.cs`:
```csharp
using Catalog.Application.Products.Responses;
using Catalog.Domain.Entities;
using Riok.Mapperly.Abstractions;

namespace Catalog.Application.Products.Mapping;

/// <summary>Compile-time mapping for products, variants, and categories.</summary>
[Mapper]
public static partial class ProductMapper
{
    /// <summary>Maps a product (and its variant tree) to a DTO.</summary>
    public static partial ProductDto ToDto(this Product product);

    /// <summary>Maps a single variant to a DTO.</summary>
    public static partial VariantDto ToVariantDto(this Variant variant);

    /// <summary>Maps a category to a DTO.</summary>
    public static partial CategoryDto ToDto(this Category category);

    /// <summary>Maps products to lightweight summaries.</summary>
    public static partial IReadOnlyList<ProductSummaryDto> ToSummaries(this IEnumerable<Product> products);
}
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test tests/unit/Catalog.UnitTests/Catalog.UnitTests.csproj -v q`
Expected: PASS.

> If Mapperly emits `RMG020`/`RMG012` ("no matching member") for `SellPriceAmount`, the cause is a name mismatch — confirm the DTO property is exactly `SellPriceAmount`/`SellPriceCurrency` so Mapperly's flattening of `SellPrice` + `Amount` applies.

- [ ] **Step 6: Commit**

```bash
git add src/services/commerce/catalog/Catalog.Application/Products/Responses \
        src/services/commerce/catalog/Catalog.Application/Products/Mapping \
        tests/unit/Catalog.UnitTests/Application/ProductMapperTests.cs
git commit -m "feat(catalog): add product DTOs and ProductMapper"
```

---

### Task 2.2: Product specifications

**Files:**
- Create: `…/Catalog.Application/Products/ReadModels/ProductByIdSpec.cs`
- Create: `…/Catalog.Application/Products/ReadModels/ProductsByCategorySpec.cs`
- Create: `tests/unit/Catalog.UnitTests/Application/ProductSpecsTests.cs`

**Interfaces:**
- Produces: `ProductByIdSpec : Specification<Product>` (`Where(p => p.Id == productId)`); `ProductsByCategorySpec : Specification<Product>` (optional `CategoryId` filter, ordered by `Name`).
- Consumed by the GetProduct/ListProducts query handlers (Task 2.7) and the load-then-mutate command handlers (Tasks 2.5/2.6).

- [ ] **Step 1: Write the failing spec tests** (Ardalis specs evaluate in-memory via `.Evaluate`)

`tests/unit/Catalog.UnitTests/Application/ProductSpecsTests.cs`:
```csharp
using Ardalis.Specification;
using Catalog.Application.Products.ReadModels;
using Catalog.Domain.Entities;
using Catalog.Domain.ValueObjects;
using Xunit;

namespace Catalog.UnitTests.Application;

public sealed class ProductSpecsTests
{
    private static Product Make(string name, Guid? categoryId) =>
        Product.Create("tenant-1", name, null, categoryId, $"{name}-1", new Money(1m, "USD"));

    [Fact]
    public void ProductByIdSpec_MatchesOnlyTheTargetProduct()
    {
        var target = Make("A", null);
        var other = Make("B", null);

        var result = new ProductByIdSpec(target.Id).Evaluate(new[] { target, other }).ToList();

        Assert.Equal(target.Id, Assert.Single(result).Id);
    }

    [Fact]
    public void ProductsByCategorySpec_WithCategory_FiltersByCategory()
    {
        var categoryId = Guid.NewGuid();
        var inCategory = Make("A", categoryId);
        var outOfCategory = Make("B", Guid.NewGuid());

        var result = new ProductsByCategorySpec(categoryId).Evaluate(new[] { inCategory, outOfCategory }).ToList();

        Assert.Equal(inCategory.Id, Assert.Single(result).Id);
    }

    [Fact]
    public void ProductsByCategorySpec_WithoutCategory_ReturnsAllOrderedByName()
    {
        var b = Make("B", null);
        var a = Make("A", null);

        var result = new ProductsByCategorySpec(null).Evaluate(new[] { b, a }).ToList();

        Assert.Equal(2, result.Count);
        Assert.Equal("A", result[0].Name);
    }
}
```

- [ ] **Step 2: Run to verify failure** — `dotnet test … -v q` → FAIL (specs don't exist).

- [ ] **Step 3: Implement the specs**

`Products/ReadModels/ProductByIdSpec.cs`:
```csharp
using Ardalis.Specification;
using Catalog.Domain.Entities;

namespace Catalog.Application.Products.ReadModels;

/// <summary>Selects a single product by id (owned variants are loaded automatically).</summary>
public sealed class ProductByIdSpec : Specification<Product>
{
    /// <summary>Initializes the spec.</summary>
    public ProductByIdSpec(Guid productId) => Query.Where(p => p.Id == productId);
}
```

`Products/ReadModels/ProductsByCategorySpec.cs`:
```csharp
using Ardalis.Specification;
using Catalog.Domain.Entities;

namespace Catalog.Application.Products.ReadModels;

/// <summary>Lists products, optionally filtered by category, ordered by name.</summary>
public sealed class ProductsByCategorySpec : Specification<Product>
{
    /// <summary>Initializes the spec. A null <paramref name="categoryId"/> returns all products.</summary>
    public ProductsByCategorySpec(Guid? categoryId)
    {
        if (categoryId is not null)
        {
            Query.Where(p => p.CategoryId == categoryId);
        }

        Query.OrderBy(p => p.Name);
    }
}
```

- [ ] **Step 4: Run to verify pass** — `dotnet test … -v q` → PASS.

- [ ] **Step 5: Commit**
```bash
git add src/services/commerce/catalog/Catalog.Application/Products/ReadModels \
        tests/unit/Catalog.UnitTests/Application/ProductSpecsTests.cs
git commit -m "feat(catalog): add product specifications"
```

---

### Task 2.3: CreateCategory command + handler

**Files:**
- Create: `…/Catalog.Application/Products/Features/CreateCategory/V1/CreateCategoryCommand.cs`
- Create: `…/Catalog.Application/Products/Features/CreateCategory/V1/CreateCategoryHandler.cs`
- Create: `tests/unit/Catalog.UnitTests/Application/CreateCategoryHandlerTests.cs`

**Interfaces:**
- Produces: `CreateCategoryCommand(string Name, string Slug, Guid? ParentId) : ICommand<CategoryDto>`; `CreateCategoryHandler.Handle(CreateCategoryCommand, CatalogDbContext, CancellationToken) → Task<CategoryDto>`.

- [ ] **Step 1: Write the failing handler test**

`tests/unit/Catalog.UnitTests/Application/CreateCategoryHandlerTests.cs`:
```csharp
using Catalog.Application.Products.Features.CreateCategory.V1;
using Catalog.UnitTests.TestContext;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Catalog.UnitTests.Application;

public sealed class CreateCategoryHandlerTests
{
    [Fact]
    public async Task Handle_WithValidCommand_PersistsAndReturnsDto()
    {
        using var db = CatalogTestContext.CreateInMemory();
        var command = new CreateCategoryCommand("Beverages", "beverages", null);

        var dto = await CreateCategoryHandler.Handle(command, db, CancellationToken.None);

        Assert.Equal("Beverages", dto.Name);
        Assert.Equal("beverages", dto.Slug);
        Assert.NotEqual(Guid.Empty, dto.Id);
        Assert.Equal(1, await db.Categories.CountAsync());
    }
}
```

- [ ] **Step 2: Run to verify failure** — FAIL (command/handler don't exist).

- [ ] **Step 3: Implement the command + handler**

`Products/Features/CreateCategory/V1/CreateCategoryCommand.cs`:
```csharp
using Catalog.Application.Products.Responses;
using SharedKernel.Core.CQRS;

namespace Catalog.Application.Products.Features.CreateCategory.V1;

/// <summary>Creates a category.</summary>
public sealed record CreateCategoryCommand(string Name, string Slug, Guid? ParentId) : ICommand<CategoryDto>;
```

`Products/Features/CreateCategory/V1/CreateCategoryHandler.cs`:
```csharp
using Catalog.Application.Database;
using Catalog.Application.Products.Mapping;
using Catalog.Application.Products.Responses;
using Catalog.Domain.Entities;

namespace Catalog.Application.Products.Features.CreateCategory.V1;

/// <summary>Handles <see cref="CreateCategoryCommand"/>.</summary>
public static class CreateCategoryHandler
{
    /// <summary>Creates and persists a category. TenantId is stamped by the Host interceptor on save.</summary>
    public static async Task<CategoryDto> Handle(
        CreateCategoryCommand command,
        CatalogDbContext db,
        CancellationToken ct)
    {
        var category = Category.Create(string.Empty, command.Name, command.Slug, command.ParentId);
        db.Categories.Add(category);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return category.ToDto();
    }
}
```

- [ ] **Step 4: Run to verify pass** — PASS.

- [ ] **Step 5: Commit**
```bash
git add src/services/commerce/catalog/Catalog.Application/Products/Features/CreateCategory \
        tests/unit/Catalog.UnitTests/Application/CreateCategoryHandlerTests.cs
git commit -m "feat(catalog): add CreateCategory command and handler"
```

---

### Task 2.4: CreateProduct command + handler + ProductCreatedIntegrationEvent

**Files:**
- Create: `…/Products/IntegrationEvents/ProductCreatedIntegrationEvent.cs`
- Create: `…/Products/Features/CreateProduct/V1/CreateProductCommand.cs`
- Create: `…/Products/Features/CreateProduct/V1/CreateProductHandler.cs`
- Create: `tests/unit/Catalog.UnitTests/Application/CreateProductHandlerTests.cs`

**Interfaces:**
- Produces:
  - `ProductCreatedIntegrationEvent : IntegrationEvent` (`[MemoryPackable]`) — `Guid ProductId`, `string TenantId`, `string Name`, `List<Guid> VariantIds`; `[MemoryPackConstructor]` parameterless ctor + `ProductCreatedIntegrationEvent(Product product)`.
  - `CreateProductCommand(string Name, string? Description, Guid? CategoryId, string Sku, decimal SellPriceAmount, string SellPriceCurrency) : ICommand<ProductDto>`.
  - `CreateProductHandler.Handle(CreateProductCommand, CatalogDbContext, IMessageBus, CancellationToken) → Task<ProductDto>` — creates a product with one default variant, saves, publishes `ProductCreatedIntegrationEvent`.

- [ ] **Step 1: Write the failing handler test**

`tests/unit/Catalog.UnitTests/Application/CreateProductHandlerTests.cs`:
```csharp
using Catalog.Application.Products.Features.CreateProduct.V1;
using Catalog.Application.Products.IntegrationEvents;
using Catalog.UnitTests.TestContext;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wolverine;
using Xunit;

namespace Catalog.UnitTests.Application;

public sealed class CreateProductHandlerTests
{
    [Fact]
    public async Task Handle_WithValidCommand_PersistsDefaultVariantAndPublishesEvent()
    {
        using var db = CatalogTestContext.CreateInMemory();
        var bus = Substitute.For<IMessageBus>();
        var command = new CreateProductCommand("Widget", "A widget", null, "WIDGET-1", 9.99m, "USD");

        var dto = await CreateProductHandler.Handle(command, db, bus, CancellationToken.None);

        Assert.Equal("Widget", dto.Name);
        Assert.True(dto.IsActive);
        var variant = Assert.Single(dto.Variants);
        Assert.True(variant.IsDefault);
        Assert.Equal(9.99m, variant.SellPriceAmount);
        Assert.Equal(1, await db.Products.CountAsync());
        await bus.Received(1).PublishAsync(Arg.Any<ProductCreatedIntegrationEvent>());
    }
}
```

- [ ] **Step 2: Run to verify failure** — FAIL.

- [ ] **Step 3: Implement the integration event**

`Products/IntegrationEvents/ProductCreatedIntegrationEvent.cs`:
```csharp
using Catalog.Domain.Entities;
using MemoryPack;
using SharedKernel.Core.Events;

namespace Catalog.Application.Products.IntegrationEvents;

/// <summary>
/// Published when a product is created. Inventory-seam event (unconsumed in v1).
/// TenantId is informational; the message envelope's X-TenantId is authoritative.
/// </summary>
[MemoryPackable]
public partial class ProductCreatedIntegrationEvent : IntegrationEvent
{
    /// <summary>Gets or sets the product id.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Gets or sets the tenant id.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets or sets the product name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the initial variant ids.</summary>
    public List<Guid> VariantIds { get; set; } = [];

    /// <summary>Serialization constructor.</summary>
    [MemoryPackConstructor]
    public ProductCreatedIntegrationEvent()
    {
    }

    /// <summary>Builds the event from a created product.</summary>
    public ProductCreatedIntegrationEvent(Product product)
    {
        ProductId = product.Id;
        TenantId = product.TenantId;
        Name = product.Name;
        VariantIds = product.Variants.Select(v => v.Id).ToList();
    }
}
```

- [ ] **Step 4: Implement the command + handler**

`Products/Features/CreateProduct/V1/CreateProductCommand.cs`:
```csharp
using Catalog.Application.Products.Responses;
using SharedKernel.Core.CQRS;

namespace Catalog.Application.Products.Features.CreateProduct.V1;

/// <summary>Creates a product with a single default variant.</summary>
public sealed record CreateProductCommand(
    string Name,
    string? Description,
    Guid? CategoryId,
    string Sku,
    decimal SellPriceAmount,
    string SellPriceCurrency) : ICommand<ProductDto>;
```

`Products/Features/CreateProduct/V1/CreateProductHandler.cs`:
```csharp
using Catalog.Application.Database;
using Catalog.Application.Products.IntegrationEvents;
using Catalog.Application.Products.Mapping;
using Catalog.Application.Products.Responses;
using Catalog.Domain.Entities;
using Catalog.Domain.ValueObjects;
using Wolverine;

namespace Catalog.Application.Products.Features.CreateProduct.V1;

/// <summary>Handles <see cref="CreateProductCommand"/>.</summary>
public static class CreateProductHandler
{
    /// <summary>Creates the product, persists it, and publishes <see cref="ProductCreatedIntegrationEvent"/>.</summary>
    public static async Task<ProductDto> Handle(
        CreateProductCommand command,
        CatalogDbContext db,
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

        db.Products.Add(product);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await bus.PublishAsync(new ProductCreatedIntegrationEvent(product)).ConfigureAwait(false);

        return product.ToDto();
    }
}
```

- [ ] **Step 5: Run to verify pass** — PASS.

- [ ] **Step 6: Commit**
```bash
git add src/services/commerce/catalog/Catalog.Application/Products/IntegrationEvents/ProductCreatedIntegrationEvent.cs \
        src/services/commerce/catalog/Catalog.Application/Products/Features/CreateProduct \
        tests/unit/Catalog.UnitTests/Application/CreateProductHandlerTests.cs
git commit -m "feat(catalog): add CreateProduct command, handler, and ProductCreated integration event"
```

---

### Task 2.5: AddVariant command + handler + VariantCreatedIntegrationEvent

**Files:**
- Create: `…/Products/IntegrationEvents/VariantCreatedIntegrationEvent.cs`
- Create: `…/Products/Features/AddVariant/V1/AddVariantCommand.cs`
- Create: `…/Products/Features/AddVariant/V1/AddVariantHandler.cs`
- Create: `tests/unit/Catalog.UnitTests/Application/AddVariantHandlerTests.cs`

**Interfaces:**
- Produces:
  - `VariantCreatedIntegrationEvent : IntegrationEvent` (`[MemoryPackable]`) — `Guid ProductId`, `Guid VariantId`, `string Sku`; serialization ctor + `(Guid productId, Guid variantId, string sku)` ctor.
  - `VariantAttributeInput(string Name, string Value)` (command input record, in the command file).
  - `AddVariantCommand(Guid ProductId, string Sku, decimal SellPriceAmount, string SellPriceCurrency, IReadOnlyList<VariantAttributeInput> Attributes) : ICommand<ErrorOr<VariantDto>>`.
  - `AddVariantHandler.Handle(AddVariantCommand, CatalogDbContext, IMessageBus, CancellationToken) → Task<ErrorOr<VariantDto>>` — loads the product via `ProductByIdSpec` against the write context, adds the variant, saves, publishes `VariantCreatedIntegrationEvent`; returns `Error.NotFound` if the product is absent.

> **Convention note:** command handlers that **load an existing aggregate** (this task and 2.6, and Suppliers 3.5–3.7) return `ErrorOr<T>` so a missing aggregate is a clean `NotFound` rather than a thrown exception. Pure-create handlers (2.3, 2.4, 3.3) return the DTO directly, matching `order`.

- [ ] **Step 1: Write the failing handler test**

`tests/unit/Catalog.UnitTests/Application/AddVariantHandlerTests.cs`:
```csharp
using Catalog.Application.Products.Features.AddVariant.V1;
using Catalog.Application.Products.IntegrationEvents;
using Catalog.Domain.Entities;
using Catalog.Domain.ValueObjects;
using Catalog.UnitTests.TestContext;
using NSubstitute;
using Wolverine;
using Xunit;

namespace Catalog.UnitTests.Application;

public sealed class AddVariantHandlerTests
{
    [Fact]
    public async Task Handle_WithExistingProduct_AddsVariantAndPublishesEvent()
    {
        var product = Product.Create("tenant-1", "Widget", null, null, "WIDGET-1", new Money(9.99m, "USD"));
        using var db = CatalogTestContext.CreateInMemory("addvariant");
        db.Products.Add(product);
        await db.SaveChangesAsync();
        var bus = Substitute.For<IMessageBus>();
        var command = new AddVariantCommand(product.Id, "WIDGET-2", 12.50m, "USD",
            [new VariantAttributeInput("Size", "Large")]);

        var result = await AddVariantHandler.Handle(command, db, bus, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("WIDGET-2", result.Value.Sku);
        Assert.False(result.Value.IsDefault);
        Assert.Equal("Large", Assert.Single(result.Value.Attributes).Value);
        await bus.Received(1).PublishAsync(Arg.Any<VariantCreatedIntegrationEvent>());
    }

    [Fact]
    public async Task Handle_WithMissingProduct_ReturnsNotFound()
    {
        using var db = CatalogTestContext.CreateInMemory("addvariant-missing");
        var bus = Substitute.For<IMessageBus>();
        var command = new AddVariantCommand(Guid.NewGuid(), "X", 1m, "USD", []);

        var result = await AddVariantHandler.Handle(command, db, bus, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorOr.ErrorType.NotFound, result.FirstError.Type);
    }
}
```

- [ ] **Step 2: Run to verify failure** — FAIL.

- [ ] **Step 3: Implement the integration event**

`Products/IntegrationEvents/VariantCreatedIntegrationEvent.cs`:
```csharp
using MemoryPack;
using SharedKernel.Core.Events;

namespace Catalog.Application.Products.IntegrationEvents;

/// <summary>Published when a variant is added to an existing product. Inventory-seam event (unconsumed in v1).</summary>
[MemoryPackable]
public partial class VariantCreatedIntegrationEvent : IntegrationEvent
{
    /// <summary>Gets or sets the product id.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Gets or sets the variant id.</summary>
    public Guid VariantId { get; set; }

    /// <summary>Gets or sets the SKU.</summary>
    public string Sku { get; set; } = string.Empty;

    /// <summary>Serialization constructor.</summary>
    [MemoryPackConstructor]
    public VariantCreatedIntegrationEvent()
    {
    }

    /// <summary>Builds the event.</summary>
    public VariantCreatedIntegrationEvent(Guid productId, Guid variantId, string sku)
    {
        ProductId = productId;
        VariantId = variantId;
        Sku = sku;
    }
}
```

- [ ] **Step 4: Implement the command + handler**

`Products/Features/AddVariant/V1/AddVariantCommand.cs`:
```csharp
using Catalog.Application.Products.Responses;
using ErrorOr;
using SharedKernel.Core.CQRS;

namespace Catalog.Application.Products.Features.AddVariant.V1;

/// <summary>A variant attribute supplied on the request.</summary>
public sealed record VariantAttributeInput(string Name, string Value);

/// <summary>Adds a non-default variant to an existing product.</summary>
public sealed record AddVariantCommand(
    Guid ProductId,
    string Sku,
    decimal SellPriceAmount,
    string SellPriceCurrency,
    IReadOnlyList<VariantAttributeInput> Attributes) : ICommand<ErrorOr<VariantDto>>;
```

`Products/Features/AddVariant/V1/AddVariantHandler.cs`:
```csharp
using Ardalis.Specification.EntityFrameworkCore;
using Catalog.Application.Database;
using Catalog.Application.Products.IntegrationEvents;
using Catalog.Application.Products.Mapping;
using Catalog.Application.Products.ReadModels;
using Catalog.Application.Products.Responses;
using Catalog.Domain.ValueObjects;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Wolverine;

namespace Catalog.Application.Products.Features.AddVariant.V1;

/// <summary>Handles <see cref="AddVariantCommand"/>.</summary>
public static class AddVariantHandler
{
    /// <summary>Loads the product, adds the variant, saves, and publishes <see cref="VariantCreatedIntegrationEvent"/>.</summary>
    public static async Task<ErrorOr<VariantDto>> Handle(
        AddVariantCommand command,
        CatalogDbContext db,
        IMessageBus bus,
        CancellationToken ct)
    {
        var product = await db.Products
            .WithSpecification(new ProductByIdSpec(command.ProductId))
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (product is null)
        {
            return Error.NotFound(description: $"Product '{command.ProductId}' was not found.");
        }

        var attributes = command.Attributes.Select(a => new VariantAttribute(a.Name, a.Value));
        var variantId = product.AddVariant(command.Sku, new Money(command.SellPriceAmount, command.SellPriceCurrency), attributes);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await bus.PublishAsync(new VariantCreatedIntegrationEvent(product.Id, variantId, command.Sku)).ConfigureAwait(false);

        return product.Variants.Single(v => v.Id == variantId).ToVariantDto();
    }
}
```

- [ ] **Step 5: Run to verify pass** — PASS.

- [ ] **Step 6: Commit**
```bash
git add src/services/commerce/catalog/Catalog.Application/Products/IntegrationEvents/VariantCreatedIntegrationEvent.cs \
        src/services/commerce/catalog/Catalog.Application/Products/Features/AddVariant \
        tests/unit/Catalog.UnitTests/Application/AddVariantHandlerTests.cs
git commit -m "feat(catalog): add AddVariant command, handler, and VariantCreated integration event"
```

---

### Task 2.6: UpdateSellPrice command + handler + ProductPriceChangedIntegrationEvent

**Files:**
- Create: `…/Products/IntegrationEvents/ProductPriceChangedIntegrationEvent.cs`
- Create: `…/Products/Features/UpdateSellPrice/V1/UpdateSellPriceCommand.cs`
- Create: `…/Products/Features/UpdateSellPrice/V1/UpdateSellPriceHandler.cs`
- Create: `tests/unit/Catalog.UnitTests/Application/UpdateSellPriceHandlerTests.cs`

**Interfaces:**
- Produces:
  - `ProductPriceChangedIntegrationEvent : IntegrationEvent` (`[MemoryPackable]`) — `Guid ProductId`, `Guid VariantId`, `decimal OldAmount`, `decimal NewAmount`, `string Currency`, `string TenantId`; serialization ctor + `(VariantSellPriceChanged domainEvent, string tenantId)` ctor.
  - `UpdateSellPriceCommand(Guid ProductId, Guid VariantId, decimal Amount, string Currency) : ICommand<ErrorOr<VariantDto>>`.
  - `UpdateSellPriceHandler.Handle(…) → Task<ErrorOr<VariantDto>>` — loads product, changes the variant sell price, saves, and publishes `ProductPriceChangedIntegrationEvent` **only when the price actually changed** (driven by whether the domain raised `VariantSellPriceChanged`).

- [ ] **Step 1: Write the failing handler test**

`tests/unit/Catalog.UnitTests/Application/UpdateSellPriceHandlerTests.cs`:
```csharp
using Catalog.Application.Products.Features.UpdateSellPrice.V1;
using Catalog.Application.Products.IntegrationEvents;
using Catalog.Domain.Entities;
using Catalog.Domain.ValueObjects;
using Catalog.UnitTests.TestContext;
using NSubstitute;
using Wolverine;
using Xunit;

namespace Catalog.UnitTests.Application;

public sealed class UpdateSellPriceHandlerTests
{
    private static async Task<(Catalog.Application.Database.CatalogDbContext Db, Product Product)> SeedAsync(string name)
    {
        var product = Product.Create("tenant-1", "Widget", null, null, "WIDGET-1", new Money(9.99m, "USD"));
        var db = CatalogTestContext.CreateInMemory(name);
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return (db, product);
    }

    [Fact]
    public async Task Handle_WithNewPrice_UpdatesAndPublishes()
    {
        var (db, product) = await SeedAsync("price-change");
        using var _ = db;
        var bus = Substitute.For<IMessageBus>();
        var command = new UpdateSellPriceCommand(product.Id, product.Variants[0].Id, 14.00m, "USD");

        var result = await UpdateSellPriceHandler.Handle(command, db, bus, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(14.00m, result.Value.SellPriceAmount);
        await bus.Received(1).PublishAsync(Arg.Any<ProductPriceChangedIntegrationEvent>());
    }

    [Fact]
    public async Task Handle_WithSamePrice_DoesNotPublish()
    {
        var (db, product) = await SeedAsync("price-same");
        using var _ = db;
        var bus = Substitute.For<IMessageBus>();
        var command = new UpdateSellPriceCommand(product.Id, product.Variants[0].Id, 9.99m, "USD");

        var result = await UpdateSellPriceHandler.Handle(command, db, bus, CancellationToken.None);

        Assert.False(result.IsError);
        await bus.DidNotReceive().PublishAsync(Arg.Any<ProductPriceChangedIntegrationEvent>());
    }

    [Fact]
    public async Task Handle_WithMissingProduct_ReturnsNotFound()
    {
        using var db = CatalogTestContext.CreateInMemory("price-missing");
        var bus = Substitute.For<IMessageBus>();
        var command = new UpdateSellPriceCommand(Guid.NewGuid(), Guid.NewGuid(), 1m, "USD");

        var result = await UpdateSellPriceHandler.Handle(command, db, bus, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorOr.ErrorType.NotFound, result.FirstError.Type);
    }
}
```

- [ ] **Step 2: Run to verify failure** — FAIL.

- [ ] **Step 3: Implement the integration event**

`Products/IntegrationEvents/ProductPriceChangedIntegrationEvent.cs`:
```csharp
using Catalog.Domain.DomainEvents;
using MemoryPack;
using SharedKernel.Core.Events;

namespace Catalog.Application.Products.IntegrationEvents;

/// <summary>Published when a variant's sell price changes. Consumed by basket/order (v1).</summary>
[MemoryPackable]
public partial class ProductPriceChangedIntegrationEvent : IntegrationEvent
{
    /// <summary>Gets or sets the product id.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Gets or sets the variant id.</summary>
    public Guid VariantId { get; set; }

    /// <summary>Gets or sets the previous amount.</summary>
    public decimal OldAmount { get; set; }

    /// <summary>Gets or sets the new amount.</summary>
    public decimal NewAmount { get; set; }

    /// <summary>Gets or sets the ISO currency code.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Gets or sets the tenant id (informational; envelope X-TenantId is authoritative).</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Serialization constructor.</summary>
    [MemoryPackConstructor]
    public ProductPriceChangedIntegrationEvent()
    {
    }

    /// <summary>Builds the event from the domain event.</summary>
    public ProductPriceChangedIntegrationEvent(VariantSellPriceChanged domainEvent, string tenantId)
    {
        ProductId = domainEvent.ProductId;
        VariantId = domainEvent.VariantId;
        OldAmount = domainEvent.OldAmount;
        NewAmount = domainEvent.NewAmount;
        Currency = domainEvent.Currency;
        TenantId = tenantId;
    }
}
```

- [ ] **Step 4: Implement the command + handler**

`Products/Features/UpdateSellPrice/V1/UpdateSellPriceCommand.cs`:
```csharp
using Catalog.Application.Products.Responses;
using ErrorOr;
using SharedKernel.Core.CQRS;

namespace Catalog.Application.Products.Features.UpdateSellPrice.V1;

/// <summary>Changes a variant's sell price.</summary>
public sealed record UpdateSellPriceCommand(Guid ProductId, Guid VariantId, decimal Amount, string Currency)
    : ICommand<ErrorOr<VariantDto>>;
```

`Products/Features/UpdateSellPrice/V1/UpdateSellPriceHandler.cs`:
```csharp
using Ardalis.Specification.EntityFrameworkCore;
using Catalog.Application.Database;
using Catalog.Application.Products.IntegrationEvents;
using Catalog.Application.Products.Mapping;
using Catalog.Application.Products.ReadModels;
using Catalog.Application.Products.Responses;
using Catalog.Domain.DomainEvents;
using Catalog.Domain.ValueObjects;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Wolverine;

namespace Catalog.Application.Products.Features.UpdateSellPrice.V1;

/// <summary>Handles <see cref="UpdateSellPriceCommand"/>.</summary>
public static class UpdateSellPriceHandler
{
    /// <summary>Changes the sell price; publishes <see cref="ProductPriceChangedIntegrationEvent"/> only on a real change.</summary>
    public static async Task<ErrorOr<VariantDto>> Handle(
        UpdateSellPriceCommand command,
        CatalogDbContext db,
        IMessageBus bus,
        CancellationToken ct)
    {
        var product = await db.Products
            .WithSpecification(new ProductByIdSpec(command.ProductId))
            .FirstOrDefaultAsync(ct)
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
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var priceChange = product.DomainEvents.OfType<VariantSellPriceChanged>().LastOrDefault();
        if (priceChange is not null)
        {
            await bus.PublishAsync(new ProductPriceChangedIntegrationEvent(priceChange, product.TenantId)).ConfigureAwait(false);
        }

        return variant.ToVariantDto();
    }
}
```

- [ ] **Step 5: Run to verify pass** — PASS.

- [ ] **Step 6: Commit**
```bash
git add src/services/commerce/catalog/Catalog.Application/Products/IntegrationEvents/ProductPriceChangedIntegrationEvent.cs \
        src/services/commerce/catalog/Catalog.Application/Products/Features/UpdateSellPrice \
        tests/unit/Catalog.UnitTests/Application/UpdateSellPriceHandlerTests.cs
git commit -m "feat(catalog): add UpdateSellPrice command, handler, and ProductPriceChanged integration event"
```

---

### Task 2.7: GetProduct + ListProducts query handlers

**Files:**
- Create: `…/Products/Features/GetProduct/V1/GetProductQuery.cs`
- Create: `…/Products/Features/GetProduct/V1/GetProductHandler.cs`
- Create: `…/Products/Features/ListProducts/V1/ListProductsQuery.cs`
- Create: `…/Products/Features/ListProducts/V1/ListProductsHandler.cs`
- Create: `tests/unit/Catalog.UnitTests/Application/ProductQueryHandlerTests.cs`

**Interfaces:**
- Produces:
  - `GetProductQuery(Guid ProductId) : IQuery<ProductDto>`; `GetProductHandler.Handle(GetProductQuery, IRepositoryBase<Product>, CancellationToken) → Task<ErrorOr<ProductDto>>` (uses `ProductByIdSpec`).
  - `ListProductsQuery(Guid? CategoryId) : IQuery<IReadOnlyList<ProductSummaryDto>>`; `ListProductsHandler.Handle(…, IRepositoryBase<Product>, …) → Task<ErrorOr<IReadOnlyList<ProductSummaryDto>>>` (uses `ProductsByCategorySpec`).

- [ ] **Step 1: Write the failing query tests** (mock `IRepositoryBase<Product>`, mirroring `GetOrderHandlerTests`)

`tests/unit/Catalog.UnitTests/Application/ProductQueryHandlerTests.cs`:
```csharp
using Ardalis.Specification;
using Catalog.Application.Products.Features.GetProduct.V1;
using Catalog.Application.Products.Features.ListProducts.V1;
using Catalog.Domain.Entities;
using Catalog.Domain.ValueObjects;
using NSubstitute;
using Xunit;

namespace Catalog.UnitTests.Application;

public sealed class ProductQueryHandlerTests
{
    [Fact]
    public async Task GetProduct_WhenFound_ReturnsDto()
    {
        var product = Product.Create("tenant-1", "Widget", null, null, "WIDGET-1", new Money(9.99m, "USD"));
        var repository = Substitute.For<IRepositoryBase<Product>>();
        repository.FirstOrDefaultAsync(Arg.Any<ISpecification<Product>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Product?>(product));

        var result = await GetProductHandler.Handle(new GetProductQuery(product.Id), repository, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(product.Id, result.Value.Id);
    }

    [Fact]
    public async Task GetProduct_WhenMissing_ReturnsNotFound()
    {
        var repository = Substitute.For<IRepositoryBase<Product>>();
        repository.FirstOrDefaultAsync(Arg.Any<ISpecification<Product>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Product?>(null));

        var result = await GetProductHandler.Handle(new GetProductQuery(Guid.NewGuid()), repository, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorOr.ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task ListProducts_ReturnsSummaries()
    {
        var a = Product.Create("tenant-1", "A", null, null, "A-1", new Money(1m, "USD"));
        var b = Product.Create("tenant-1", "B", null, null, "B-1", new Money(2m, "USD"));
        var repository = Substitute.For<IRepositoryBase<Product>>();
        repository.ListAsync(Arg.Any<ISpecification<Product>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<List<Product>>([a, b]));

        var result = await ListProductsHandler.Handle(new ListProductsQuery(null), repository, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.Count);
    }
}
```

- [ ] **Step 2: Run to verify failure** — FAIL.

- [ ] **Step 3: Implement GetProduct**

`Products/Features/GetProduct/V1/GetProductQuery.cs`:
```csharp
using Catalog.Application.Products.Responses;
using SharedKernel.Core.CQRS;

namespace Catalog.Application.Products.Features.GetProduct.V1;

/// <summary>Fetches a product by id.</summary>
public sealed record GetProductQuery(Guid ProductId) : IQuery<ProductDto>;
```

`Products/Features/GetProduct/V1/GetProductHandler.cs`:
```csharp
using Ardalis.Specification;
using Catalog.Application.Products.Mapping;
using Catalog.Application.Products.ReadModels;
using Catalog.Application.Products.Responses;
using Catalog.Domain.Entities;
using ErrorOr;

namespace Catalog.Application.Products.Features.GetProduct.V1;

/// <summary>Handles <see cref="GetProductQuery"/>.</summary>
public static class GetProductHandler
{
    /// <summary>Returns the product DTO or a NotFound error.</summary>
    public static async Task<ErrorOr<ProductDto>> Handle(
        GetProductQuery query,
        IRepositoryBase<Product> repository,
        CancellationToken ct)
    {
        var product = await repository.FirstOrDefaultAsync(new ProductByIdSpec(query.ProductId), ct).ConfigureAwait(false);

        return product is null
            ? Error.NotFound(description: $"Product '{query.ProductId}' was not found.")
            : product.ToDto();
    }
}
```

- [ ] **Step 4: Implement ListProducts**

`Products/Features/ListProducts/V1/ListProductsQuery.cs`:
```csharp
using Catalog.Application.Products.Responses;
using SharedKernel.Core.CQRS;

namespace Catalog.Application.Products.Features.ListProducts.V1;

/// <summary>Lists products, optionally filtered by category.</summary>
public sealed record ListProductsQuery(Guid? CategoryId) : IQuery<IReadOnlyList<ProductSummaryDto>>;
```

`Products/Features/ListProducts/V1/ListProductsHandler.cs`:
```csharp
using Ardalis.Specification;
using Catalog.Application.Products.Mapping;
using Catalog.Application.Products.ReadModels;
using Catalog.Application.Products.Responses;
using Catalog.Domain.Entities;
using ErrorOr;

namespace Catalog.Application.Products.Features.ListProducts.V1;

/// <summary>Handles <see cref="ListProductsQuery"/>.</summary>
public static class ListProductsHandler
{
    /// <summary>Returns product summaries.</summary>
    public static async Task<ErrorOr<IReadOnlyList<ProductSummaryDto>>> Handle(
        ListProductsQuery query,
        IRepositoryBase<Product> repository,
        CancellationToken ct)
    {
        var products = await repository.ListAsync(new ProductsByCategorySpec(query.CategoryId), ct).ConfigureAwait(false);
        return products.ToSummaries().ToErrorOr();
    }
}
```

> `ToErrorOr()` (ErrorOr's extension) wraps the list as a successful `ErrorOr<IReadOnlyList<ProductSummaryDto>>`. If preferred, `return products.ToSummaries();` also works via implicit conversion — keep whichever compiles cleanly.

- [ ] **Step 5: Run to verify pass** — PASS.

- [ ] **Step 6: Commit**
```bash
git add src/services/commerce/catalog/Catalog.Application/Products/Features/GetProduct \
        src/services/commerce/catalog/Catalog.Application/Products/Features/ListProducts \
        tests/unit/Catalog.UnitTests/Application/ProductQueryHandlerTests.cs
git commit -m "feat(catalog): add GetProduct and ListProducts query handlers"
```

---

# Phase 3 — Suppliers Capability

### Task 3.1: Supplier DTOs + SupplierMapper

**Files:**
- Create: `…/Suppliers/Responses/SupplierDto.cs`
- Create: `…/Suppliers/Responses/VariantSupplierDto.cs`
- Create: `…/Suppliers/Responses/SupplierPriceHistoryDto.cs`
- Create: `…/Suppliers/Mapping/SupplierMapper.cs`
- Create: `tests/unit/Catalog.UnitTests/Application/SupplierMapperTests.cs`

**Interfaces:**
- Produces:
  - `SupplierDto(Guid Id, string Name, string? ContactEmail, string? ContactPhone, bool IsActive)`
  - `VariantSupplierDto(Guid Id, Guid SupplierId, decimal CostPriceAmount, string CostPriceCurrency, string SupplierSku, int LeadTimeDays, int MinOrderQuantity, bool IsPreferred)`
  - `SupplierPriceHistoryDto(decimal CostPriceAmount, string CostPriceCurrency, DateTimeOffset EffectiveFrom)`
  - `SupplierMapper` (`[Mapper]`): `SupplierDto ToDto(this Supplier)`, `VariantSupplierDto ToDto(this VariantSupplier)`, `SupplierPriceHistoryDto ToHistoryDto(this SupplierPriceHistory)`, `IReadOnlyList<SupplierPriceHistoryDto> ToPriceHistory(this IEnumerable<SupplierPriceHistory>)`.

- [ ] **Step 1: Create the DTOs**

`Suppliers/Responses/SupplierDto.cs`:
```csharp
namespace Catalog.Application.Suppliers.Responses;

/// <summary>A supplier.</summary>
public sealed record SupplierDto(Guid Id, string Name, string? ContactEmail, string? ContactPhone, bool IsActive);
```

`Suppliers/Responses/VariantSupplierDto.cs`:
```csharp
namespace Catalog.Application.Suppliers.Responses;

/// <summary>A variant↔supplier sourcing link with its flattened cost price.</summary>
public sealed record VariantSupplierDto(
    Guid Id,
    Guid SupplierId,
    decimal CostPriceAmount,
    string CostPriceCurrency,
    string SupplierSku,
    int LeadTimeDays,
    int MinOrderQuantity,
    bool IsPreferred);
```

`Suppliers/Responses/SupplierPriceHistoryDto.cs`:
```csharp
namespace Catalog.Application.Suppliers.Responses;

/// <summary>An effective-dated supplier cost record.</summary>
public sealed record SupplierPriceHistoryDto(decimal CostPriceAmount, string CostPriceCurrency, DateTimeOffset EffectiveFrom);
```

- [ ] **Step 2: Write the failing mapper test**

`tests/unit/Catalog.UnitTests/Application/SupplierMapperTests.cs`:
```csharp
using Catalog.Application.Suppliers.Mapping;
using Catalog.Domain.Entities;
using Catalog.Domain.ValueObjects;
using Xunit;

namespace Catalog.UnitTests.Application;

public sealed class SupplierMapperTests
{
    [Fact]
    public void ToDto_MapsSupplier()
    {
        var supplier = Supplier.Create("tenant-1", "Acme", "sales@acme.test", "+1-555-0100");

        var dto = supplier.ToDto();

        Assert.Equal(supplier.Id, dto.Id);
        Assert.Equal("Acme", dto.Name);
        Assert.Equal("sales@acme.test", dto.ContactEmail);
        Assert.True(dto.IsActive);
    }

    [Fact]
    public void ToDto_FlattensVariantSupplierCostPrice()
    {
        var product = Product.Create("tenant-1", "Widget", null, null, "WIDGET-1", new Money(9.99m, "USD"));
        var supplierId = Guid.NewGuid();
        product.LinkSupplier(product.Variants[0].Id, supplierId, new Money(5m, "USD"), "ACME-9", 7, 10, isPreferred: true);
        var link = product.Variants[0].Suppliers[0];

        var dto = link.ToDto();

        Assert.Equal(supplierId, dto.SupplierId);
        Assert.Equal(5m, dto.CostPriceAmount);
        Assert.Equal("USD", dto.CostPriceCurrency);
        Assert.Equal("ACME-9", dto.SupplierSku);
        Assert.True(dto.IsPreferred);
    }

    [Fact]
    public void ToPriceHistory_MapsEachRow()
    {
        var product = Product.Create("tenant-1", "Widget", null, null, "WIDGET-1", new Money(9.99m, "USD"));
        var supplierId = Guid.NewGuid();
        product.LinkSupplier(product.Variants[0].Id, supplierId, new Money(5m, "USD"), "A", 7, 1, isPreferred: true);
        product.ChangeSupplierCost(product.Variants[0].Id, supplierId, new Money(6.50m, "USD"));
        var link = product.Variants[0].Suppliers[0];

        var history = link.PriceHistory.ToPriceHistory();

        Assert.Equal(2, history.Count);
        Assert.Contains(history, h => h.CostPriceAmount == 6.50m);
    }
}
```

- [ ] **Step 3: Run to verify failure** — FAIL.

- [ ] **Step 4: Implement the mapper**

`Suppliers/Mapping/SupplierMapper.cs`:
```csharp
using Catalog.Application.Suppliers.Responses;
using Catalog.Domain.Entities;
using Riok.Mapperly.Abstractions;

namespace Catalog.Application.Suppliers.Mapping;

/// <summary>Compile-time mapping for suppliers, links, and price history.</summary>
[Mapper]
public static partial class SupplierMapper
{
    /// <summary>Maps a supplier to a DTO.</summary>
    public static partial SupplierDto ToDto(this Supplier supplier);

    /// <summary>Maps a variant↔supplier link to a DTO.</summary>
    public static partial VariantSupplierDto ToDto(this VariantSupplier link);

    /// <summary>Maps a single price-history row to a DTO.</summary>
    public static partial SupplierPriceHistoryDto ToHistoryDto(this SupplierPriceHistory history);

    /// <summary>Maps price-history rows to DTOs.</summary>
    public static partial IReadOnlyList<SupplierPriceHistoryDto> ToPriceHistory(this IEnumerable<SupplierPriceHistory> history);
}
```

- [ ] **Step 5: Run to verify pass** — PASS.

- [ ] **Step 6: Commit**
```bash
git add src/services/commerce/catalog/Catalog.Application/Suppliers/Responses \
        src/services/commerce/catalog/Catalog.Application/Suppliers/Mapping \
        tests/unit/Catalog.UnitTests/Application/SupplierMapperTests.cs
git commit -m "feat(catalog): add supplier DTOs and SupplierMapper"
```

---

### Task 3.2: Supplier specifications

**Files:**
- Create: `…/Suppliers/ReadModels/SupplierByIdSpec.cs`
- Create: `…/Suppliers/ReadModels/ProductByVariantSpec.cs`
- Create: `tests/unit/Catalog.UnitTests/Application/SupplierSpecsTests.cs`

**Interfaces:**
- Produces: `SupplierByIdSpec : Specification<Supplier>` (`Where(s => s.Id == supplierId)`); `ProductByVariantSpec : Specification<Product>` (`Where(p => p.Variants.Any(v => v.Id == variantId))`) — finds the product that owns a given variant. Used by all variant↔supplier command handlers (3.5–3.7) and the price-history query (3.8).

- [ ] **Step 1: Write the failing spec tests**

`tests/unit/Catalog.UnitTests/Application/SupplierSpecsTests.cs`:
```csharp
using Ardalis.Specification;
using Catalog.Application.Suppliers.ReadModels;
using Catalog.Domain.Entities;
using Catalog.Domain.ValueObjects;
using Xunit;

namespace Catalog.UnitTests.Application;

public sealed class SupplierSpecsTests
{
    [Fact]
    public void SupplierByIdSpec_MatchesOnlyTheTarget()
    {
        var target = Supplier.Create("tenant-1", "Acme");
        var other = Supplier.Create("tenant-1", "Other");

        var result = new SupplierByIdSpec(target.Id).Evaluate(new[] { target, other }).ToList();

        Assert.Equal(target.Id, Assert.Single(result).Id);
    }

    [Fact]
    public void ProductByVariantSpec_FindsOwningProduct()
    {
        var product = Product.Create("tenant-1", "Widget", null, null, "WIDGET-1", new Money(9.99m, "USD"));
        var variantId = product.Variants[0].Id;
        var other = Product.Create("tenant-1", "Other", null, null, "OTHER-1", new Money(1m, "USD"));

        var result = new ProductByVariantSpec(variantId).Evaluate(new[] { product, other }).ToList();

        Assert.Equal(product.Id, Assert.Single(result).Id);
    }
}
```

- [ ] **Step 2: Run to verify failure** — FAIL.

- [ ] **Step 3: Implement the specs**

`Suppliers/ReadModels/SupplierByIdSpec.cs`:
```csharp
using Ardalis.Specification;
using Catalog.Domain.Entities;

namespace Catalog.Application.Suppliers.ReadModels;

/// <summary>Selects a single supplier by id.</summary>
public sealed class SupplierByIdSpec : Specification<Supplier>
{
    /// <summary>Initializes the spec.</summary>
    public SupplierByIdSpec(Guid supplierId) => Query.Where(s => s.Id == supplierId);
}
```

`Suppliers/ReadModels/ProductByVariantSpec.cs`:
```csharp
using Ardalis.Specification;
using Catalog.Domain.Entities;

namespace Catalog.Application.Suppliers.ReadModels;

/// <summary>Selects the product that owns the given variant (owned tree loaded automatically).</summary>
public sealed class ProductByVariantSpec : Specification<Product>
{
    /// <summary>Initializes the spec.</summary>
    public ProductByVariantSpec(Guid variantId) => Query.Where(p => p.Variants.Any(v => v.Id == variantId));
}
```

- [ ] **Step 4: Run to verify pass** — PASS.

- [ ] **Step 5: Commit**
```bash
git add src/services/commerce/catalog/Catalog.Application/Suppliers/ReadModels \
        tests/unit/Catalog.UnitTests/Application/SupplierSpecsTests.cs
git commit -m "feat(catalog): add supplier specifications"
```

---

### Task 3.3: CreateSupplier command + handler

**Files:**
- Create: `…/Suppliers/Features/CreateSupplier/V1/CreateSupplierCommand.cs`
- Create: `…/Suppliers/Features/CreateSupplier/V1/CreateSupplierHandler.cs`
- Create: `tests/unit/Catalog.UnitTests/Application/CreateSupplierHandlerTests.cs`

**Interfaces:**
- Produces: `CreateSupplierCommand(string Name, string? ContactEmail, string? ContactPhone) : ICommand<SupplierDto>`; `CreateSupplierHandler.Handle(CreateSupplierCommand, CatalogDbContext, CancellationToken) → Task<SupplierDto>`.

- [ ] **Step 1: Write the failing handler test**

`tests/unit/Catalog.UnitTests/Application/CreateSupplierHandlerTests.cs`:
```csharp
using Catalog.Application.Suppliers.Features.CreateSupplier.V1;
using Catalog.UnitTests.TestContext;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Catalog.UnitTests.Application;

public sealed class CreateSupplierHandlerTests
{
    [Fact]
    public async Task Handle_WithValidCommand_PersistsAndReturnsDto()
    {
        using var db = CatalogTestContext.CreateInMemory();
        var command = new CreateSupplierCommand("Acme", "sales@acme.test", "+1-555-0100");

        var dto = await CreateSupplierHandler.Handle(command, db, CancellationToken.None);

        Assert.Equal("Acme", dto.Name);
        Assert.True(dto.IsActive);
        Assert.Equal(1, await db.Suppliers.CountAsync());
    }
}
```

- [ ] **Step 2: Run to verify failure** — FAIL.

- [ ] **Step 3: Implement the command + handler**

`Suppliers/Features/CreateSupplier/V1/CreateSupplierCommand.cs`:
```csharp
using Catalog.Application.Suppliers.Responses;
using SharedKernel.Core.CQRS;

namespace Catalog.Application.Suppliers.Features.CreateSupplier.V1;

/// <summary>Creates a supplier.</summary>
public sealed record CreateSupplierCommand(string Name, string? ContactEmail, string? ContactPhone) : ICommand<SupplierDto>;
```

`Suppliers/Features/CreateSupplier/V1/CreateSupplierHandler.cs`:
```csharp
using Catalog.Application.Database;
using Catalog.Application.Suppliers.Mapping;
using Catalog.Application.Suppliers.Responses;
using Catalog.Domain.Entities;

namespace Catalog.Application.Suppliers.Features.CreateSupplier.V1;

/// <summary>Handles <see cref="CreateSupplierCommand"/>.</summary>
public static class CreateSupplierHandler
{
    /// <summary>Creates and persists a supplier. TenantId is stamped by the Host interceptor on save.</summary>
    public static async Task<SupplierDto> Handle(
        CreateSupplierCommand command,
        CatalogDbContext db,
        CancellationToken ct)
    {
        var supplier = Supplier.Create(string.Empty, command.Name, command.ContactEmail, command.ContactPhone);
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return supplier.ToDto();
    }
}
```

- [ ] **Step 4: Run to verify pass** — PASS.

- [ ] **Step 5: Commit**
```bash
git add src/services/commerce/catalog/Catalog.Application/Suppliers/Features/CreateSupplier \
        tests/unit/Catalog.UnitTests/Application/CreateSupplierHandlerTests.cs
git commit -m "feat(catalog): add CreateSupplier command and handler"
```

---

### Task 3.4: GetSupplier query handler

**Files:**
- Create: `…/Suppliers/Features/GetSupplier/V1/GetSupplierQuery.cs`
- Create: `…/Suppliers/Features/GetSupplier/V1/GetSupplierHandler.cs`
- Create: `tests/unit/Catalog.UnitTests/Application/GetSupplierHandlerTests.cs`

**Interfaces:**
- Produces: `GetSupplierQuery(Guid SupplierId) : IQuery<SupplierDto>`; `GetSupplierHandler.Handle(GetSupplierQuery, IRepositoryBase<Supplier>, CancellationToken) → Task<ErrorOr<SupplierDto>>` (uses `SupplierByIdSpec`).

- [ ] **Step 1: Write the failing handler test**

`tests/unit/Catalog.UnitTests/Application/GetSupplierHandlerTests.cs`:
```csharp
using Ardalis.Specification;
using Catalog.Application.Suppliers.Features.GetSupplier.V1;
using Catalog.Domain.Entities;
using NSubstitute;
using Xunit;

namespace Catalog.UnitTests.Application;

public sealed class GetSupplierHandlerTests
{
    [Fact]
    public async Task Handle_WhenFound_ReturnsDto()
    {
        var supplier = Supplier.Create("tenant-1", "Acme");
        var repository = Substitute.For<IRepositoryBase<Supplier>>();
        repository.FirstOrDefaultAsync(Arg.Any<ISpecification<Supplier>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Supplier?>(supplier));

        var result = await GetSupplierHandler.Handle(new GetSupplierQuery(supplier.Id), repository, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(supplier.Id, result.Value.Id);
    }

    [Fact]
    public async Task Handle_WhenMissing_ReturnsNotFound()
    {
        var repository = Substitute.For<IRepositoryBase<Supplier>>();
        repository.FirstOrDefaultAsync(Arg.Any<ISpecification<Supplier>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Supplier?>(null));

        var result = await GetSupplierHandler.Handle(new GetSupplierQuery(Guid.NewGuid()), repository, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorOr.ErrorType.NotFound, result.FirstError.Type);
    }
}
```

- [ ] **Step 2: Run to verify failure** — FAIL.

- [ ] **Step 3: Implement the query + handler**

`Suppliers/Features/GetSupplier/V1/GetSupplierQuery.cs`:
```csharp
using Catalog.Application.Suppliers.Responses;
using SharedKernel.Core.CQRS;

namespace Catalog.Application.Suppliers.Features.GetSupplier.V1;

/// <summary>Fetches a supplier by id.</summary>
public sealed record GetSupplierQuery(Guid SupplierId) : IQuery<SupplierDto>;
```

`Suppliers/Features/GetSupplier/V1/GetSupplierHandler.cs`:
```csharp
using Ardalis.Specification;
using Catalog.Application.Suppliers.Mapping;
using Catalog.Application.Suppliers.ReadModels;
using Catalog.Application.Suppliers.Responses;
using Catalog.Domain.Entities;
using ErrorOr;

namespace Catalog.Application.Suppliers.Features.GetSupplier.V1;

/// <summary>Handles <see cref="GetSupplierQuery"/>.</summary>
public static class GetSupplierHandler
{
    /// <summary>Returns the supplier DTO or a NotFound error.</summary>
    public static async Task<ErrorOr<SupplierDto>> Handle(
        GetSupplierQuery query,
        IRepositoryBase<Supplier> repository,
        CancellationToken ct)
    {
        var supplier = await repository.FirstOrDefaultAsync(new SupplierByIdSpec(query.SupplierId), ct).ConfigureAwait(false);

        return supplier is null
            ? Error.NotFound(description: $"Supplier '{query.SupplierId}' was not found.")
            : supplier.ToDto();
    }
}
```

- [ ] **Step 4: Run to verify pass** — PASS.

- [ ] **Step 5: Commit**
```bash
git add src/services/commerce/catalog/Catalog.Application/Suppliers/Features/GetSupplier \
        tests/unit/Catalog.UnitTests/Application/GetSupplierHandlerTests.cs
git commit -m "feat(catalog): add GetSupplier query handler"
```

---

### Task 3.5: LinkVariantSupplier command + handler

**Files:**
- Create: `…/Suppliers/Features/LinkVariantSupplier/V1/LinkVariantSupplierCommand.cs`
- Create: `…/Suppliers/Features/LinkVariantSupplier/V1/LinkVariantSupplierHandler.cs`
- Create: `tests/unit/Catalog.UnitTests/Application/LinkVariantSupplierHandlerTests.cs`

**Interfaces:**
- Produces: `LinkVariantSupplierCommand(Guid VariantId, Guid SupplierId, decimal CostAmount, string CostCurrency, string SupplierSku, int LeadTimeDays, int MinOrderQuantity, bool IsPreferred) : ICommand<ErrorOr<VariantSupplierDto>>`; `LinkVariantSupplierHandler.Handle(…, CatalogDbContext, …) → Task<ErrorOr<VariantSupplierDto>>` — loads the owning product via `ProductByVariantSpec`, links the supplier, saves, returns the link DTO. No integration event (supplier cost is buy-side/internal per the design).

- [ ] **Step 1: Write the failing handler test**

`tests/unit/Catalog.UnitTests/Application/LinkVariantSupplierHandlerTests.cs`:
```csharp
using Catalog.Application.Suppliers.Features.LinkVariantSupplier.V1;
using Catalog.Domain.Entities;
using Catalog.Domain.ValueObjects;
using Catalog.UnitTests.TestContext;
using Xunit;

namespace Catalog.UnitTests.Application;

public sealed class LinkVariantSupplierHandlerTests
{
    [Fact]
    public async Task Handle_WithExistingVariant_LinksSupplierWithInitialHistory()
    {
        var product = Product.Create("tenant-1", "Widget", null, null, "WIDGET-1", new Money(9.99m, "USD"));
        using var db = CatalogTestContext.CreateInMemory("link");
        db.Products.Add(product);
        await db.SaveChangesAsync();
        var supplierId = Guid.NewGuid();
        var command = new LinkVariantSupplierCommand(product.Variants[0].Id, supplierId, 5m, "USD", "ACME-9", 7, 10, true);

        var result = await LinkVariantSupplierHandler.Handle(command, db, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(supplierId, result.Value.SupplierId);
        Assert.Equal(5m, result.Value.CostPriceAmount);
        Assert.True(result.Value.IsPreferred);
    }

    [Fact]
    public async Task Handle_WithMissingVariant_ReturnsNotFound()
    {
        using var db = CatalogTestContext.CreateInMemory("link-missing");
        var command = new LinkVariantSupplierCommand(Guid.NewGuid(), Guid.NewGuid(), 5m, "USD", "X", 1, 1, false);

        var result = await LinkVariantSupplierHandler.Handle(command, db, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorOr.ErrorType.NotFound, result.FirstError.Type);
    }
}
```

- [ ] **Step 2: Run to verify failure** — FAIL.

- [ ] **Step 3: Implement the command + handler**

`Suppliers/Features/LinkVariantSupplier/V1/LinkVariantSupplierCommand.cs`:
```csharp
using Catalog.Application.Suppliers.Responses;
using ErrorOr;
using SharedKernel.Core.CQRS;

namespace Catalog.Application.Suppliers.Features.LinkVariantSupplier.V1;

/// <summary>Links a supplier to a variant with sourcing details.</summary>
public sealed record LinkVariantSupplierCommand(
    Guid VariantId,
    Guid SupplierId,
    decimal CostAmount,
    string CostCurrency,
    string SupplierSku,
    int LeadTimeDays,
    int MinOrderQuantity,
    bool IsPreferred) : ICommand<ErrorOr<VariantSupplierDto>>;
```

`Suppliers/Features/LinkVariantSupplier/V1/LinkVariantSupplierHandler.cs`:
```csharp
using Ardalis.Specification.EntityFrameworkCore;
using Catalog.Application.Database;
using Catalog.Application.Suppliers.Mapping;
using Catalog.Application.Suppliers.ReadModels;
using Catalog.Application.Suppliers.Responses;
using Catalog.Domain.ValueObjects;
using ErrorOr;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Application.Suppliers.Features.LinkVariantSupplier.V1;

/// <summary>Handles <see cref="LinkVariantSupplierCommand"/>.</summary>
public static class LinkVariantSupplierHandler
{
    /// <summary>Loads the owning product, links the supplier, and saves.</summary>
    public static async Task<ErrorOr<VariantSupplierDto>> Handle(
        LinkVariantSupplierCommand command,
        CatalogDbContext db,
        CancellationToken ct)
    {
        var product = await db.Products
            .WithSpecification(new ProductByVariantSpec(command.VariantId))
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (product is null)
        {
            return Error.NotFound(description: $"Variant '{command.VariantId}' was not found.");
        }

        var linkId = product.LinkSupplier(
            command.VariantId,
            command.SupplierId,
            new Money(command.CostAmount, command.CostCurrency),
            command.SupplierSku,
            command.LeadTimeDays,
            command.MinOrderQuantity,
            command.IsPreferred);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var link = product.Variants
            .Single(v => v.Id == command.VariantId).Suppliers
            .Single(s => s.Id == linkId);

        return link.ToDto();
    }
}
```

- [ ] **Step 4: Run to verify pass** — PASS.

- [ ] **Step 5: Commit**
```bash
git add src/services/commerce/catalog/Catalog.Application/Suppliers/Features/LinkVariantSupplier \
        tests/unit/Catalog.UnitTests/Application/LinkVariantSupplierHandlerTests.cs
git commit -m "feat(catalog): add LinkVariantSupplier command and handler"
```

---

### Task 3.6: UpdateSupplierCost command + handler

**Files:**
- Create: `…/Suppliers/Features/UpdateSupplierCost/V1/UpdateSupplierCostCommand.cs`
- Create: `…/Suppliers/Features/UpdateSupplierCost/V1/UpdateSupplierCostHandler.cs`
- Create: `tests/unit/Catalog.UnitTests/Application/UpdateSupplierCostHandlerTests.cs`

**Interfaces:**
- Produces: `UpdateSupplierCostCommand(Guid VariantId, Guid SupplierId, decimal CostAmount, string CostCurrency) : ICommand<ErrorOr<VariantSupplierDto>>`; `UpdateSupplierCostHandler.Handle(…, CatalogDbContext, …) → Task<ErrorOr<VariantSupplierDto>>` — appends a price-history row (via the domain) and saves. The cost change stays **internal** (no integration event published); the `SupplierCostPriceChanged` domain event remains an in-process signal.

- [ ] **Step 1: Write the failing handler test**

`tests/unit/Catalog.UnitTests/Application/UpdateSupplierCostHandlerTests.cs`:
```csharp
using Catalog.Application.Suppliers.Features.UpdateSupplierCost.V1;
using Catalog.Domain.Entities;
using Catalog.Domain.ValueObjects;
using Catalog.UnitTests.TestContext;
using Xunit;

namespace Catalog.UnitTests.Application;

public sealed class UpdateSupplierCostHandlerTests
{
    [Fact]
    public async Task Handle_WithLinkedSupplier_UpdatesCostAndAppendsHistory()
    {
        var product = Product.Create("tenant-1", "Widget", null, null, "WIDGET-1", new Money(9.99m, "USD"));
        var supplierId = Guid.NewGuid();
        product.LinkSupplier(product.Variants[0].Id, supplierId, new Money(5m, "USD"), "A", 7, 1, isPreferred: true);
        using var db = CatalogTestContext.CreateInMemory("cost");
        db.Products.Add(product);
        await db.SaveChangesAsync();
        var command = new UpdateSupplierCostCommand(product.Variants[0].Id, supplierId, 6.50m, "USD");

        var result = await UpdateSupplierCostHandler.Handle(command, db, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(6.50m, result.Value.CostPriceAmount);
    }

    [Fact]
    public async Task Handle_WithUnlinkedSupplier_ReturnsNotFound()
    {
        var product = Product.Create("tenant-1", "Widget", null, null, "WIDGET-1", new Money(9.99m, "USD"));
        using var db = CatalogTestContext.CreateInMemory("cost-missing");
        db.Products.Add(product);
        await db.SaveChangesAsync();
        var command = new UpdateSupplierCostCommand(product.Variants[0].Id, Guid.NewGuid(), 6.50m, "USD");

        var result = await UpdateSupplierCostHandler.Handle(command, db, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorOr.ErrorType.NotFound, result.FirstError.Type);
    }
}
```

- [ ] **Step 2: Run to verify failure** — FAIL.

- [ ] **Step 3: Implement the command + handler**

`Suppliers/Features/UpdateSupplierCost/V1/UpdateSupplierCostCommand.cs`:
```csharp
using Catalog.Application.Suppliers.Responses;
using ErrorOr;
using SharedKernel.Core.CQRS;

namespace Catalog.Application.Suppliers.Features.UpdateSupplierCost.V1;

/// <summary>Changes a variant↔supplier cost price (recorded in history).</summary>
public sealed record UpdateSupplierCostCommand(Guid VariantId, Guid SupplierId, decimal CostAmount, string CostCurrency)
    : ICommand<ErrorOr<VariantSupplierDto>>;
```

`Suppliers/Features/UpdateSupplierCost/V1/UpdateSupplierCostHandler.cs`:
```csharp
using Ardalis.Specification.EntityFrameworkCore;
using Catalog.Application.Database;
using Catalog.Application.Suppliers.Mapping;
using Catalog.Application.Suppliers.ReadModels;
using Catalog.Application.Suppliers.Responses;
using Catalog.Domain.ValueObjects;
using ErrorOr;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Application.Suppliers.Features.UpdateSupplierCost.V1;

/// <summary>Handles <see cref="UpdateSupplierCostCommand"/>.</summary>
public static class UpdateSupplierCostHandler
{
    /// <summary>Changes the cost (appending history via the domain) and saves. Cost stays internal — no event published.</summary>
    public static async Task<ErrorOr<VariantSupplierDto>> Handle(
        UpdateSupplierCostCommand command,
        CatalogDbContext db,
        CancellationToken ct)
    {
        var product = await db.Products
            .WithSpecification(new ProductByVariantSpec(command.VariantId))
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (product is null)
        {
            return Error.NotFound(description: $"Variant '{command.VariantId}' was not found.");
        }

        var variant = product.Variants.Single(v => v.Id == command.VariantId);
        var link = variant.Suppliers.FirstOrDefault(s => s.SupplierId == command.SupplierId);
        if (link is null)
        {
            return Error.NotFound(description: $"Supplier '{command.SupplierId}' is not linked to variant '{command.VariantId}'.");
        }

        product.ChangeSupplierCost(command.VariantId, command.SupplierId, new Money(command.CostAmount, command.CostCurrency));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return variant.Suppliers.Single(s => s.SupplierId == command.SupplierId).ToDto();
    }
}
```

- [ ] **Step 4: Run to verify pass** — PASS.

- [ ] **Step 5: Commit**
```bash
git add src/services/commerce/catalog/Catalog.Application/Suppliers/Features/UpdateSupplierCost \
        tests/unit/Catalog.UnitTests/Application/UpdateSupplierCostHandlerTests.cs
git commit -m "feat(catalog): add UpdateSupplierCost command and handler"
```

---

### Task 3.7: SetPreferredSupplier command + handler

**Files:**
- Create: `…/Suppliers/Features/SetPreferredSupplier/V1/SetPreferredSupplierCommand.cs`
- Create: `…/Suppliers/Features/SetPreferredSupplier/V1/SetPreferredSupplierHandler.cs`
- Create: `tests/unit/Catalog.UnitTests/Application/SetPreferredSupplierHandlerTests.cs`

**Interfaces:**
- Produces: `SetPreferredSupplierCommand(Guid VariantId, Guid SupplierId) : ICommand<ErrorOr<Success>>`; `SetPreferredSupplierHandler.Handle(…, CatalogDbContext, …) → Task<ErrorOr<Success>>` — enforces the single-preferred-supplier invariant via the domain, saves, returns `Result.Success`.

- [ ] **Step 1: Write the failing handler test**

`tests/unit/Catalog.UnitTests/Application/SetPreferredSupplierHandlerTests.cs`:
```csharp
using Catalog.Application.Suppliers.Features.SetPreferredSupplier.V1;
using Catalog.Domain.Entities;
using Catalog.Domain.ValueObjects;
using Catalog.UnitTests.TestContext;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Catalog.UnitTests.Application;

public sealed class SetPreferredSupplierHandlerTests
{
    [Fact]
    public async Task Handle_MakesExactlyOnePreferred()
    {
        var product = Product.Create("tenant-1", "Widget", null, null, "WIDGET-1", new Money(9.99m, "USD"));
        var variantId = product.Variants[0].Id;
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        product.LinkSupplier(variantId, a, new Money(5m, "USD"), "A", 7, 1, isPreferred: true);
        product.LinkSupplier(variantId, b, new Money(6m, "USD"), "B", 7, 1, isPreferred: false);
        using var db = CatalogTestContext.CreateInMemory("preferred");
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var result = await SetPreferredSupplierHandler.Handle(
            new SetPreferredSupplierCommand(variantId, b), db, CancellationToken.None);

        Assert.False(result.IsError);
        var reloaded = await db.Products.FirstAsync();
        var suppliers = reloaded.Variants[0].Suppliers;
        Assert.Equal(1, suppliers.Count(s => s.IsPreferred));
        Assert.True(suppliers.Single(s => s.SupplierId == b).IsPreferred);
    }

    [Fact]
    public async Task Handle_WithUnlinkedSupplier_ReturnsNotFound()
    {
        var product = Product.Create("tenant-1", "Widget", null, null, "WIDGET-1", new Money(9.99m, "USD"));
        using var db = CatalogTestContext.CreateInMemory("preferred-missing");
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var result = await SetPreferredSupplierHandler.Handle(
            new SetPreferredSupplierCommand(product.Variants[0].Id, Guid.NewGuid()), db, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorOr.ErrorType.NotFound, result.FirstError.Type);
    }
}
```

- [ ] **Step 2: Run to verify failure** — FAIL.

- [ ] **Step 3: Implement the command + handler**

`Suppliers/Features/SetPreferredSupplier/V1/SetPreferredSupplierCommand.cs`:
```csharp
using ErrorOr;
using SharedKernel.Core.CQRS;

namespace Catalog.Application.Suppliers.Features.SetPreferredSupplier.V1;

/// <summary>Sets the single preferred supplier for a variant.</summary>
public sealed record SetPreferredSupplierCommand(Guid VariantId, Guid SupplierId) : ICommand<ErrorOr<Success>>;
```

`Suppliers/Features/SetPreferredSupplier/V1/SetPreferredSupplierHandler.cs`:
```csharp
using Ardalis.Specification.EntityFrameworkCore;
using Catalog.Application.Database;
using Catalog.Application.Suppliers.ReadModels;
using ErrorOr;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Application.Suppliers.Features.SetPreferredSupplier.V1;

/// <summary>Handles <see cref="SetPreferredSupplierCommand"/>.</summary>
public static class SetPreferredSupplierHandler
{
    /// <summary>Enforces the single-preferred invariant via the domain and saves.</summary>
    public static async Task<ErrorOr<Success>> Handle(
        SetPreferredSupplierCommand command,
        CatalogDbContext db,
        CancellationToken ct)
    {
        var product = await db.Products
            .WithSpecification(new ProductByVariantSpec(command.VariantId))
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (product is null)
        {
            return Error.NotFound(description: $"Variant '{command.VariantId}' was not found.");
        }

        var variant = product.Variants.Single(v => v.Id == command.VariantId);
        if (variant.Suppliers.All(s => s.SupplierId != command.SupplierId))
        {
            return Error.NotFound(description: $"Supplier '{command.SupplierId}' is not linked to variant '{command.VariantId}'.");
        }

        product.SetPreferredSupplier(command.VariantId, command.SupplierId);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return Result.Success;
    }
}
```

- [ ] **Step 4: Run to verify pass** — PASS.

- [ ] **Step 5: Commit**
```bash
git add src/services/commerce/catalog/Catalog.Application/Suppliers/Features/SetPreferredSupplier \
        tests/unit/Catalog.UnitTests/Application/SetPreferredSupplierHandlerTests.cs
git commit -m "feat(catalog): add SetPreferredSupplier command and handler"
```

---

### Task 3.8: GetSupplierPriceHistory query handler

**Files:**
- Create: `…/Suppliers/Features/GetSupplierPriceHistory/V1/GetSupplierPriceHistoryQuery.cs`
- Create: `…/Suppliers/Features/GetSupplierPriceHistory/V1/GetSupplierPriceHistoryHandler.cs`
- Create: `tests/unit/Catalog.UnitTests/Application/GetSupplierPriceHistoryHandlerTests.cs`

**Interfaces:**
- Produces: `GetSupplierPriceHistoryQuery(Guid VariantId, Guid SupplierId) : IQuery<IReadOnlyList<SupplierPriceHistoryDto>>`; `GetSupplierPriceHistoryHandler.Handle(…, IRepositoryBase<Product>, …) → Task<ErrorOr<IReadOnlyList<SupplierPriceHistoryDto>>>` — loads the owning product via `ProductByVariantSpec`, navigates to the link, maps its history. Returns `NotFound` if the product or link is absent.

- [ ] **Step 1: Write the failing handler test**

`tests/unit/Catalog.UnitTests/Application/GetSupplierPriceHistoryHandlerTests.cs`:
```csharp
using Ardalis.Specification;
using Catalog.Application.Suppliers.Features.GetSupplierPriceHistory.V1;
using Catalog.Domain.Entities;
using Catalog.Domain.ValueObjects;
using NSubstitute;
using Xunit;

namespace Catalog.UnitTests.Application;

public sealed class GetSupplierPriceHistoryHandlerTests
{
    [Fact]
    public async Task Handle_WithLinkedSupplier_ReturnsHistory()
    {
        var product = Product.Create("tenant-1", "Widget", null, null, "WIDGET-1", new Money(9.99m, "USD"));
        var variantId = product.Variants[0].Id;
        var supplierId = Guid.NewGuid();
        product.LinkSupplier(variantId, supplierId, new Money(5m, "USD"), "A", 7, 1, isPreferred: true);
        product.ChangeSupplierCost(variantId, supplierId, new Money(6.50m, "USD"));

        var repository = Substitute.For<IRepositoryBase<Product>>();
        repository.FirstOrDefaultAsync(Arg.Any<ISpecification<Product>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Product?>(product));

        var result = await GetSupplierPriceHistoryHandler.Handle(
            new GetSupplierPriceHistoryQuery(variantId, supplierId), repository, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.Count);
    }

    [Fact]
    public async Task Handle_WithMissingProduct_ReturnsNotFound()
    {
        var repository = Substitute.For<IRepositoryBase<Product>>();
        repository.FirstOrDefaultAsync(Arg.Any<ISpecification<Product>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Product?>(null));

        var result = await GetSupplierPriceHistoryHandler.Handle(
            new GetSupplierPriceHistoryQuery(Guid.NewGuid(), Guid.NewGuid()), repository, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorOr.ErrorType.NotFound, result.FirstError.Type);
    }
}
```

- [ ] **Step 2: Run to verify failure** — FAIL.

- [ ] **Step 3: Implement the query + handler**

`Suppliers/Features/GetSupplierPriceHistory/V1/GetSupplierPriceHistoryQuery.cs`:
```csharp
using Catalog.Application.Suppliers.Responses;
using SharedKernel.Core.CQRS;

namespace Catalog.Application.Suppliers.Features.GetSupplierPriceHistory.V1;

/// <summary>Fetches the cost-price history for a variant↔supplier link.</summary>
public sealed record GetSupplierPriceHistoryQuery(Guid VariantId, Guid SupplierId)
    : IQuery<IReadOnlyList<SupplierPriceHistoryDto>>;
```

`Suppliers/Features/GetSupplierPriceHistory/V1/GetSupplierPriceHistoryHandler.cs`:
```csharp
using Ardalis.Specification;
using Catalog.Application.Suppliers.Mapping;
using Catalog.Application.Suppliers.ReadModels;
using Catalog.Application.Suppliers.Responses;
using Catalog.Domain.Entities;
using ErrorOr;

namespace Catalog.Application.Suppliers.Features.GetSupplierPriceHistory.V1;

/// <summary>Handles <see cref="GetSupplierPriceHistoryQuery"/>.</summary>
public static class GetSupplierPriceHistoryHandler
{
    /// <summary>Loads the owning product, navigates to the link, and maps its history.</summary>
    public static async Task<ErrorOr<IReadOnlyList<SupplierPriceHistoryDto>>> Handle(
        GetSupplierPriceHistoryQuery query,
        IRepositoryBase<Product> repository,
        CancellationToken ct)
    {
        var product = await repository.FirstOrDefaultAsync(new ProductByVariantSpec(query.VariantId), ct).ConfigureAwait(false);
        if (product is null)
        {
            return Error.NotFound(description: $"Variant '{query.VariantId}' was not found.");
        }

        var variant = product.Variants.FirstOrDefault(v => v.Id == query.VariantId);
        var link = variant?.Suppliers.FirstOrDefault(s => s.SupplierId == query.SupplierId);
        if (link is null)
        {
            return Error.NotFound(description: $"Supplier '{query.SupplierId}' is not linked to variant '{query.VariantId}'.");
        }

        return link.PriceHistory.ToPriceHistory().ToErrorOr();
    }
}
```

- [ ] **Step 4: Run to verify pass** — PASS.

- [ ] **Step 5: Commit**
```bash
git add src/services/commerce/catalog/Catalog.Application/Suppliers/Features/GetSupplierPriceHistory \
        tests/unit/Catalog.UnitTests/Application/GetSupplierPriceHistoryHandlerTests.cs
git commit -m "feat(catalog): add GetSupplierPriceHistory query handler"
```

---

## Plan 2 Done — Verification

Run the full Application build + the catalog unit-test suite:

```bash
dotnet build src/services/commerce/catalog/Catalog.Application/Catalog.Application.csproj -v q -clp:ErrorsOnly
dotnet test tests/unit/Catalog.UnitTests/Catalog.UnitTests.csproj -v q
```
Expected: `Build succeeded. 0 Error(s)`; all Plan 1 domain tests **and** all Plan 2 application tests pass.

**Deliverable:** a green, fully unit-tested `Catalog.Application` layer — the write `CatalogDbContext` + EF configurations, `CatalogOptions`, Products and Suppliers DTOs/mappers/specifications, eight commands and four queries (twelve WolverineFx static handlers), and the three integration events (`ProductCreated`, `VariantCreated`, `ProductPriceChanged`).

**Next:** Plan 3 (Host) — `CatalogReadDbContext : CatalogDbContext` (NoTracking) + Npgsql wiring + DI; FastEndpoints endpoints + `Validator<TRequest>` for all twelve features; `Program.cs` (`AddTeckService` + `UseWolverine` + Keycloak); the `Initial` EF migration (`Host/Database/Migrations/`, run on `--migrate`); ArchUnitNET + Testcontainers integration tests (which validate the owned-tree EF mapping against real Postgres); and `deploy/catalog/base/` manifests + the auto-generated `specs/catalog-v1-public.json`.

---

## Self-Review (run after the plan is written, before execution)

1. **Spec coverage** — every Application-layer item in design §4 and §6 maps to a task: DbContext (1.1), Options (1.1), Products DTOs/Mapper (2.1), Products ReadModels (2.2), CreateProduct/AddVariant/UpdateSellPrice/CreateCategory/GetProduct/ListProducts (2.3–2.7), the three integration events (2.4/2.5/2.6), Suppliers DTOs/Mapper (3.1), Suppliers ReadModels (3.2), CreateSupplier/GetSupplier/LinkVariantSupplier/UpdateSupplierCost/SetPreferredSupplier/GetSupplierPriceHistory (3.3–3.8). Supplier cost intentionally stays internal (design §6/§10) — no integration event, confirmed in 3.6. The `EventHandlers/DomainEvents/` translation layer from §4 is intentionally folded into direct command-handler publishing (Deviation #2).
2. **Type consistency** — `CatalogDbContext` (Application) used identically by every command handler; `ProductByIdSpec`/`ProductsByCategorySpec`/`SupplierByIdSpec`/`ProductByVariantSpec` names match across producer and consumer tasks; `ProductMapper.ToVariantDto`/`ToDto`/`ToSummaries` and `SupplierMapper.ToDto`/`ToHistoryDto`/`ToPriceHistory` names match their call sites; integration-event ctors match their handler call sites. Command return-type rule (creates → DTO; load-mutate → `ErrorOr<T>`) applied consistently.
3. **Placeholder scan** — no TBD/TODO; every code step shows complete, compilable code; each EF/Mapperly/ErrorOr edge has an inline fallback note.

> **Execution note for the implementer:** the highest-risk task is **1.1** (the owned-aggregate EF model building under the InMemory provider). Do it first and get its two tests green before proceeding — every later handler test depends on `CatalogTestContext`. If a Mapperly or ErrorOr API differs slightly by version (e.g. `ToErrorOr()` vs implicit conversion), prefer whichever compiles; the shapes here match the versions in `Directory.Packages.props` (Mapperly 4.3.1, ErrorOr 2.0.1, Ardalis.Specification 9.3.1).
