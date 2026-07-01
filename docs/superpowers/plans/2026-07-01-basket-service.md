# Basket Service Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the `basket` commerce microservice (cart + checkout, anonymous + customer identity with merge-on-login) that publishes `BasketCheckedOut`, which the existing `order` service consumes to create an order — a full cross-service loop.

**Architecture:** Clean architecture (`Basket.Domain → Basket.Application → Basket.Host`), mirroring the complete `order` reference service exactly. Three-context CQRS (abstract base + write leaf in Application, NoTracking read leaf in Host). Handlers are static WolverineFx methods depending only on `IGenericReadRepository`/`IGenericWriteRepository`/`IUnitOfWork`. Cross-service event contract lives in `SharedKernel.Events`.

**Tech Stack:** .NET 10, EF Core (Npgsql), FastEndpoints, WolverineFx, Ardalis.Specification + SmartEnum, Riok.Mapperly, Finbuckle multi-tenancy, ErrorOr, FluentValidation, xunit.v3, NSubstitute, ArchUnitNET, Testcontainers.

**Spec:** `docs/superpowers/specs/2026-07-01-basket-service-design.md`
**Reference service (mirror it):** `src/services/commerce/order/`

## Global Constraints

- **Namespaces use the PLURAL convention**, matching `order`: project files are `Basket.Domain.csproj` / `Basket.Application.csproj` / `Basket.Host.csproj`, but their `RootNamespace` is `Baskets.Domain` / `Baskets.Application` / `Baskets.Host`. The Application capability folder is `Baskets/`. (Order = folder `order`, csproj `Order.*`, namespace `Orders.*` — mirror precisely.)
- **`TreatWarningsAsErrors=true` + analyzers-as-errors.** The root `.editorconfig` is an allowlist enforcing StyleCop: usings ordered (System first), file-scoped namespaces, one type per file, file name = type name, member ordering, and **XML docs on every public type/member**. Every public class/record/method/property needs a `<summary>` (and `<param>`/`<returns>`/`<typeparam>` where applicable). Test projects are exempt from StyleCop `SA*` but not from formatting/IDE rules.
- **Repository/UoW rule (build-failing ArchUnit test):** Application types must NOT depend on any `DbContext` or `Ardalis.Specification.IRepositoryBase`. Handlers inject `IGenericReadRepository<T,Guid>` / `IGenericWriteRepository<T,Guid>` / `IUnitOfWork` only. `IUnitOfWork.SaveChangesAsync(ct)` is the single commit point, called exactly once per command.
- **Load-to-mutate requires `enableTracking: true`:** `repository.FirstOrDefaultAsync(spec, enableTracking: true, ct)` — the default spec overloads are `AsNoTracking`, so without it mutations never persist.
- **`IMessageBus.PublishAsync(evt)` takes NO CancellationToken.** `InvokeAsync<T>(msg, ct)` does.
- **Every new project must be registered in `Teck.Platform.slnx`** (Nx `@nx/dotnet` infers projects from `.csproj`; no `project.json`).
- **`TId` is `System.Guid`** for all entities. Money is `decimal`. `TenantId` is `string` (max length 64).
- **Commit after every task** using conventional commits (`feat(basket): ...`). Commits are GPG-signed automatically — never pass `--no-gpg-sign`; if signing fails, stop and surface it.

---

### Task 1: Scaffold the three projects and register them

**Files:**
- Create: `src/services/commerce/basket/Basket.Domain/Basket.Domain.csproj`
- Create: `src/services/commerce/basket/Basket.Application/Basket.Application.csproj`
- Create: `src/services/commerce/basket/Basket.Host/Basket.Host.csproj`
- Create: `src/services/commerce/basket/Directory.Build.props`
- Create: `src/services/commerce/basket/Basket.Host/Program.cs` (temporary minimal, replaced in Task 12)
- Modify: `Teck.Platform.slnx`

**Interfaces:**
- Produces: three buildable projects with namespaces `Baskets.Domain`, `Baskets.Application`, `Baskets.Host`.

- [ ] **Step 1: Create `Basket.Domain/Basket.Domain.csproj`** (copy of Order.Domain.csproj with the namespace changed)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>Baskets.Domain</RootNamespace>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\..\shared\SharedKernel.Core\SharedKernel.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Ardalis.SmartEnum" />
    <PackageReference Include="Ardalis.Specification" />
    <PackageReference Include="ErrorOr" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create `Basket.Application/Basket.Application.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>Baskets.Application</RootNamespace>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Basket.Domain\Basket.Domain.csproj" />
    <ProjectReference Include="..\..\..\..\shared\SharedKernel.Core\SharedKernel.Core.csproj" />
    <ProjectReference Include="..\..\..\..\shared\SharedKernel.Events\SharedKernel.Events.csproj" />
    <ProjectReference Include="..\..\..\..\shared\SharedKernel.Infrastructure\SharedKernel.Infrastructure.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="ErrorOr" />
    <PackageReference Include="FluentValidation" />
    <PackageReference Include="Riok.Mapperly" ExcludeAssets="runtime" PrivateAssets="all">
      <IncludeAssets>compile; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="WolverineFx" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Create `Basket.Host/Basket.Host.csproj`** (copy of Order.Host.csproj, namespaces changed)

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>Baskets.Host</RootNamespace>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\..\aspire\Teck.ServiceDefaults\Teck.ServiceDefaults.csproj" />
    <ProjectReference Include="..\..\..\..\shared\SharedKernel.Core\SharedKernel.Core.csproj" />
    <ProjectReference Include="..\..\..\..\shared\SharedKernel.Events\SharedKernel.Events.csproj" />
    <ProjectReference Include="..\..\..\..\shared\SharedKernel.Grpc.Contracts\SharedKernel.Grpc.Contracts.csproj" />
    <ProjectReference Include="..\..\..\..\shared\SharedKernel.Infrastructure\SharedKernel.Infrastructure.csproj" />
    <ProjectReference Include="..\Basket.Application\Basket.Application.csproj" />
    <ProjectReference Include="..\Basket.Domain\Basket.Domain.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="FastEndpoints" />
    <PackageReference Include="Microsoft.EntityFrameworkCore" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
    <PackageReference Include="WolverineFx" />
    <PackageReference Include="WolverineFx.EntityFrameworkCore" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Create `Basket/Directory.Build.props`** (identical to order's)

```xml
<Project>
  <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />
</Project>
```

- [ ] **Step 5: Create a temporary minimal `Basket.Host/Program.cs`** so the web project builds (replaced in Task 12)

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
await app.RunAsync();
```

- [ ] **Step 6: Register all three projects in `Teck.Platform.slnx`** — add a folder block next to the order block (around line 23):

```xml
  <Folder Name="/src/services/commerce/basket/">
    <Project Path="src/services/commerce/basket/Basket.Application/Basket.Application.csproj" />
    <Project Path="src/services/commerce/basket/Basket.Domain/Basket.Domain.csproj" />
    <Project Path="src/services/commerce/basket/Basket.Host/Basket.Host.csproj" />
  </Folder>
```

- [ ] **Step 7: Build to verify the scaffolding compiles**

Run: `dotnet build src/services/commerce/basket/Basket.Host/Basket.Host.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 8: Commit**

```bash
git add src/services/commerce/basket Teck.Platform.slnx
git commit -m "feat(basket): scaffold Domain/Application/Host projects"
```

---

### Task 2: Domain value types (`BasketStatus`, `BasketItem`, `BasketPricingService`)

**Files:**
- Create: `src/services/commerce/basket/Basket.Domain/ValueObjects/BasketStatus.cs`
- Create: `src/services/commerce/basket/Basket.Domain/ValueObjects/BasketItem.cs`
- Create: `src/services/commerce/basket/Basket.Domain/Services/BasketPricingService.cs`
- Create: `tests/unit/Basket.UnitTests/Basket.UnitTests.csproj`
- Create: `tests/unit/Basket.UnitTests/BasketPricingServiceTests.cs`
- Modify: `Teck.Platform.slnx`

**Interfaces:**
- Produces: `BasketStatus` (SmartEnum: `Active=1`, `CheckedOut=2`, `Abandoned=3`, `Merged=4`); `sealed record BasketItem(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity)` with `decimal LineTotal`; `static decimal BasketPricingService.CalculateSubtotal(IEnumerable<BasketItem> items)`.

- [ ] **Step 1: Create the `Basket.UnitTests` project** (mirror `tests/unit/Order.UnitTests/Order.UnitTests.csproj`)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>Baskets.UnitTests</RootNamespace>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" />
    <PackageReference Include="NSubstitute" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\src\services\commerce\basket\Basket.Domain\Basket.Domain.csproj" />
    <ProjectReference Include="..\..\..\src\services\commerce\basket\Basket.Application\Basket.Application.csproj" />
  </ItemGroup>
</Project>
```

Register it in `Teck.Platform.slnx` near the other test projects:
```xml
    <Project Path="tests/unit/Basket.UnitTests/Basket.UnitTests.csproj" />
```

- [ ] **Step 2: Write the failing test** `tests/unit/Basket.UnitTests/BasketPricingServiceTests.cs`

```csharp
using Baskets.Domain.Services;
using Baskets.Domain.ValueObjects;
using Xunit;

namespace Baskets.UnitTests;

public sealed class BasketPricingServiceTests
{
    [Fact]
    public void CalculateSubtotal_WithMultipleItems_ReturnsSumOfLineTotals()
    {
        BasketItem[] items =
        [
            new(Guid.NewGuid(), "A", 10m, 2),
            new(Guid.NewGuid(), "B", 5m, 3),
        ];

        decimal subtotal = BasketPricingService.CalculateSubtotal(items);

        Assert.Equal(35m, subtotal);
    }

    [Fact]
    public void CalculateSubtotal_WithNegativeQuantity_Throws()
    {
        BasketItem[] items = [new(Guid.NewGuid(), "A", 10m, -1)];

        Assert.Throws<ArgumentOutOfRangeException>(() => BasketPricingService.CalculateSubtotal(items));
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/unit/Basket.UnitTests/Basket.UnitTests.csproj`
Expected: FAIL — `BasketPricingService`/`BasketItem` do not exist (compile error).

- [ ] **Step 4: Create `BasketStatus.cs`** (mirror `OrderStatus`)

```csharp
using Ardalis.SmartEnum;

namespace Baskets.Domain.ValueObjects;

/// <summary>
/// Represents the lifecycle status of a basket.
/// </summary>
public sealed class BasketStatus : SmartEnum<BasketStatus>
{
    /// <summary>The basket is open and accepting changes.</summary>
    public static readonly BasketStatus Active = new(nameof(Active), 1);

    /// <summary>The basket has been checked out and converted to an order.</summary>
    public static readonly BasketStatus CheckedOut = new(nameof(CheckedOut), 2);

    /// <summary>The basket was abandoned without checkout.</summary>
    public static readonly BasketStatus Abandoned = new(nameof(Abandoned), 3);

    /// <summary>The basket was merged into another basket on login.</summary>
    public static readonly BasketStatus Merged = new(nameof(Merged), 4);

    private BasketStatus(string name, int value)
        : base(name, value)
    {
    }
}
```

- [ ] **Step 5: Create `BasketItem.cs`** (mirror `OrderLine`)

```csharp
namespace Baskets.Domain.ValueObjects;

/// <summary>
/// Represents a single line within a basket as an immutable value object.
/// </summary>
/// <param name="ProductId">The identifier of the product.</param>
/// <param name="ProductName">The name of the product captured at add-time.</param>
/// <param name="UnitPrice">The price per unit captured at add-time.</param>
/// <param name="Quantity">The quantity of the product in the basket.</param>
public sealed record BasketItem(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity)
{
    /// <summary>Gets the total monetary amount for this line.</summary>
    public decimal LineTotal => UnitPrice * Quantity;
}
```

- [ ] **Step 6: Create `BasketPricingService.cs`** (mirror `OrderPricingService`)

```csharp
using Baskets.Domain.ValueObjects;

namespace Baskets.Domain.Services;

/// <summary>
/// Provides pricing calculations for baskets.
/// </summary>
public static class BasketPricingService
{
    /// <summary>
    /// Calculates the subtotal for the specified basket items.
    /// </summary>
    /// <param name="items">The basket items to total.</param>
    /// <returns>The sum of all line totals.</returns>
    public static decimal CalculateSubtotal(IEnumerable<BasketItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        decimal subtotal = 0;

        foreach (BasketItem item in items)
        {
            if (item.Quantity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(items), "Basket item quantity cannot be negative.");
            }

            if (item.UnitPrice < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(items), "Basket item unit price cannot be negative.");
            }

            subtotal += item.LineTotal;
        }

        return subtotal;
    }
}
```

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet test tests/unit/Basket.UnitTests/Basket.UnitTests.csproj`
Expected: PASS (2 tests).

- [ ] **Step 8: Commit**

```bash
git add src/services/commerce/basket/Basket.Domain tests/unit/Basket.UnitTests Teck.Platform.slnx
git commit -m "feat(basket): add BasketStatus, BasketItem, BasketPricingService"
```

---

### Task 3: `Basket` aggregate — creation + item management

**Files:**
- Create: `src/services/commerce/basket/Basket.Domain/Entities/Basket.cs`
- Create: `tests/unit/Basket.UnitTests/BasketItemManagementTests.cs`

**Interfaces:**
- Consumes: `BasketStatus`, `BasketItem`, `BasketPricingService`.
- Produces: `Basket` aggregate with `static Basket CreateForCustomer(Guid customerId, string tenantId)`, `static Basket CreateAnonymous(Guid anonymousToken, string tenantId)`, `AddItem(Guid productId, string productName, decimal unitPrice, int quantity)`, `UpdateItemQuantity(Guid productId, int quantity)`, `RemoveItem(Guid productId)`, `Clear()`. Properties: `Guid? CustomerId`, `Guid? AnonymousToken`, `string TenantId`, `BasketStatus Status`, `IReadOnlyList<BasketItem> Items`, `decimal Subtotal`. (Lifecycle methods `Checkout`/`MergeFrom`/`AssignToCustomer` are added in Task 4.)

- [ ] **Step 1: Write the failing test** `tests/unit/Basket.UnitTests/BasketItemManagementTests.cs`

```csharp
using Baskets.Domain.Entities;
using Baskets.Domain.ValueObjects;
using Xunit;

namespace Baskets.UnitTests;

public sealed class BasketItemManagementTests
{
    private static readonly Guid Product = Guid.NewGuid();

    [Fact]
    public void CreateForCustomer_StartsActiveAndEmpty()
    {
        var basket = Basket.CreateForCustomer(Guid.NewGuid(), "tenant-1");

        Assert.Equal(BasketStatus.Active, basket.Status);
        Assert.Empty(basket.Items);
        Assert.Equal(0m, basket.Subtotal);
        Assert.Null(basket.AnonymousToken);
    }

    [Fact]
    public void AddItem_SameProductTwice_MergesAndSumsQuantity()
    {
        var basket = Basket.CreateAnonymous(Guid.NewGuid(), "tenant-1");

        basket.AddItem(Product, "Widget", 10m, 2);
        basket.AddItem(Product, "Widget", 10m, 3);

        BasketItem line = Assert.Single(basket.Items);
        Assert.Equal(5, line.Quantity);
        Assert.Equal(50m, basket.Subtotal);
    }

    [Fact]
    public void UpdateItemQuantity_ToZero_RemovesLine()
    {
        var basket = Basket.CreateForCustomer(Guid.NewGuid(), "tenant-1");
        basket.AddItem(Product, "Widget", 10m, 2);

        basket.UpdateItemQuantity(Product, 0);

        Assert.Empty(basket.Items);
    }

    [Fact]
    public void RemoveItem_RemovesTheMatchingLine()
    {
        var basket = Basket.CreateForCustomer(Guid.NewGuid(), "tenant-1");
        basket.AddItem(Product, "Widget", 10m, 2);

        basket.RemoveItem(Product);

        Assert.Empty(basket.Items);
    }

    [Fact]
    public void AddItem_WithNonPositiveQuantity_Throws()
    {
        var basket = Basket.CreateForCustomer(Guid.NewGuid(), "tenant-1");

        Assert.Throws<ArgumentOutOfRangeException>(() => basket.AddItem(Product, "Widget", 10m, 0));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/unit/Basket.UnitTests/Basket.UnitTests.csproj --filter BasketItemManagementTests`
Expected: FAIL — `Basket` does not exist.

- [ ] **Step 3: Create `Basket.cs`** (item-management portion)

```csharp
using Baskets.Domain.Services;
using Baskets.Domain.ValueObjects;
using SharedKernel.Core.Domain;

namespace Baskets.Domain.Entities;

/// <summary>
/// Represents a shopping basket aggregate root. A basket is owned either by an authenticated
/// customer (<see cref="CustomerId"/>) or, for guests, by an opaque <see cref="AnonymousToken"/>.
/// </summary>
public sealed class Basket : BaseEntity, IAggregateRoot, ITenantScoped
{
    private readonly List<BasketItem> _items = [];

    private Basket()
    {
    }

    /// <summary>Gets the identifier of the owning customer, or null for a guest basket.</summary>
    public Guid? CustomerId { get; private set; }

    /// <summary>Gets the opaque token identifying a guest basket, or null once owned by a customer.</summary>
    public Guid? AnonymousToken { get; private set; }

    /// <inheritdoc/>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets the current lifecycle status of the basket.</summary>
    public BasketStatus Status { get; private set; } = BasketStatus.Active;

    /// <summary>Gets the items currently in the basket.</summary>
    public IReadOnlyList<BasketItem> Items => _items;

    /// <summary>Gets the basket subtotal (sum of line totals).</summary>
    public decimal Subtotal { get; private set; }

    /// <summary>Creates a new active basket owned by a customer.</summary>
    /// <param name="customerId">The owning customer identifier.</param>
    /// <param name="tenantId">The owning tenant identifier.</param>
    /// <returns>The new basket.</returns>
    public static Basket CreateForCustomer(Guid customerId, string tenantId) => new()
    {
        CustomerId = customerId,
        TenantId = tenantId,
        Status = BasketStatus.Active,
    };

    /// <summary>Creates a new active guest basket identified by an anonymous token.</summary>
    /// <param name="anonymousToken">The opaque guest token.</param>
    /// <param name="tenantId">The owning tenant identifier.</param>
    /// <returns>The new basket.</returns>
    public static Basket CreateAnonymous(Guid anonymousToken, string tenantId) => new()
    {
        AnonymousToken = anonymousToken,
        TenantId = tenantId,
        Status = BasketStatus.Active,
    };

    /// <summary>Adds an item, merging by product identifier and summing quantities.</summary>
    /// <param name="productId">The product identifier.</param>
    /// <param name="productName">The product name captured at add-time.</param>
    /// <param name="unitPrice">The unit price captured at add-time.</param>
    /// <param name="quantity">The quantity to add (must be positive).</param>
    public void AddItem(Guid productId, string productName, decimal unitPrice, int quantity)
    {
        EnsureActive();
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
        }

        int index = _items.FindIndex(item => item.ProductId == productId);
        if (index >= 0)
        {
            BasketItem existing = _items[index];
            _items[index] = existing with { Quantity = existing.Quantity + quantity };
        }
        else
        {
            _items.Add(new BasketItem(productId, productName, unitPrice, quantity));
        }

        Recalculate();
    }

    /// <summary>Sets the quantity for a product; a non-positive quantity removes the line.</summary>
    /// <param name="productId">The product identifier.</param>
    /// <param name="quantity">The new quantity; zero or less removes the line.</param>
    public void UpdateItemQuantity(Guid productId, int quantity)
    {
        EnsureActive();
        int index = _items.FindIndex(item => item.ProductId == productId);
        if (index < 0)
        {
            throw new InvalidOperationException($"Product '{productId}' is not in the basket.");
        }

        if (quantity <= 0)
        {
            _items.RemoveAt(index);
        }
        else
        {
            _items[index] = _items[index] with { Quantity = quantity };
        }

        Recalculate();
    }

    /// <summary>Removes the line for the specified product, if present.</summary>
    /// <param name="productId">The product identifier.</param>
    public void RemoveItem(Guid productId)
    {
        EnsureActive();
        _items.RemoveAll(item => item.ProductId == productId);
        Recalculate();
    }

    /// <summary>Removes all items from the basket.</summary>
    public void Clear()
    {
        EnsureActive();
        _items.Clear();
        Recalculate();
    }

    private void EnsureActive()
    {
        if (Status != BasketStatus.Active)
        {
            throw new InvalidOperationException($"Basket is '{Status.Name}' and can no longer be modified.");
        }
    }

    private void Recalculate() => Subtotal = BasketPricingService.CalculateSubtotal(_items);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/unit/Basket.UnitTests/Basket.UnitTests.csproj --filter BasketItemManagementTests`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add src/services/commerce/basket/Basket.Domain tests/unit/Basket.UnitTests
git commit -m "feat(basket): add Basket aggregate item management"
```

---

### Task 4: `Basket` lifecycle — checkout, merge, assign + `BasketCheckedOut` domain event

**Files:**
- Create: `src/services/commerce/basket/Basket.Domain/DomainEvents/BasketCheckedOut.cs`
- Modify: `src/services/commerce/basket/Basket.Domain/Entities/Basket.cs` (add `Checkout`, `MergeFrom`, `AssignToCustomer`)
- Create: `tests/unit/Basket.UnitTests/BasketLifecycleTests.cs`

**Interfaces:**
- Consumes: item-management `Basket`.
- Produces: `BasketCheckedOut : DomainEvent` (props: `Guid BasketId`, `Guid? CustomerId`, `string TenantId`, `decimal Subtotal`, `IReadOnlyList<BasketItem> Items`, `DateTimeOffset CheckedOutAt`); `Basket.Checkout()`, `Basket.MergeFrom(Basket source)`, `Basket.AssignToCustomer(Guid customerId)`.

- [ ] **Step 1: Write the failing test** `tests/unit/Basket.UnitTests/BasketLifecycleTests.cs`

```csharp
using Baskets.Domain.DomainEvents;
using Baskets.Domain.Entities;
using Baskets.Domain.ValueObjects;
using Xunit;

namespace Baskets.UnitTests;

public sealed class BasketLifecycleTests
{
    [Fact]
    public void Checkout_WithItems_SetsStatusAndRaisesEvent()
    {
        var basket = Basket.CreateForCustomer(Guid.NewGuid(), "tenant-1");
        basket.AddItem(Guid.NewGuid(), "Widget", 10m, 2);

        basket.Checkout();

        Assert.Equal(BasketStatus.CheckedOut, basket.Status);
        Assert.Contains(basket.DomainEvents, e => e is BasketCheckedOut);
    }

    [Fact]
    public void Checkout_EmptyBasket_Throws()
    {
        var basket = Basket.CreateForCustomer(Guid.NewGuid(), "tenant-1");

        Assert.Throws<InvalidOperationException>(() => basket.Checkout());
    }

    [Fact]
    public void MergeFrom_CombinesItemsAndMarksSourceMerged()
    {
        var shared = Guid.NewGuid();
        var target = Basket.CreateForCustomer(Guid.NewGuid(), "tenant-1");
        target.AddItem(shared, "Widget", 10m, 1);
        var source = Basket.CreateAnonymous(Guid.NewGuid(), "tenant-1");
        source.AddItem(shared, "Widget", 10m, 2);
        source.AddItem(Guid.NewGuid(), "Gadget", 5m, 1);

        target.MergeFrom(source);

        Assert.Equal(BasketStatus.Merged, source.Status);
        Assert.Equal(2, target.Items.Count);
        Assert.Equal(3, target.Items.First(i => i.ProductId == shared).Quantity);
    }

    [Fact]
    public void AssignToCustomer_TransfersOwnership()
    {
        var customerId = Guid.NewGuid();
        var basket = Basket.CreateAnonymous(Guid.NewGuid(), "tenant-1");

        basket.AssignToCustomer(customerId);

        Assert.Equal(customerId, basket.CustomerId);
        Assert.Null(basket.AnonymousToken);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/unit/Basket.UnitTests/Basket.UnitTests.csproj --filter BasketLifecycleTests`
Expected: FAIL — `Checkout`/`MergeFrom`/`AssignToCustomer`/`BasketCheckedOut` do not exist.

- [ ] **Step 3: Create `BasketCheckedOut.cs`** (mirror `OrderPlaced`)

```csharp
using Baskets.Domain.ValueObjects;
using SharedKernel.Core.Events;

namespace Baskets.Domain.DomainEvents;

/// <summary>
/// Domain event raised when a basket has been checked out.
/// </summary>
public sealed class BasketCheckedOut : DomainEvent
{
    /// <summary>Initializes a new instance of the <see cref="BasketCheckedOut"/> class.</summary>
    /// <param name="basketId">The checked-out basket identifier.</param>
    /// <param name="customerId">The owning customer identifier.</param>
    /// <param name="tenantId">The owning tenant identifier.</param>
    /// <param name="subtotal">The basket subtotal.</param>
    /// <param name="items">The items at checkout time.</param>
    /// <param name="checkedOutAt">The checkout timestamp.</param>
    public BasketCheckedOut(Guid basketId, Guid? customerId, string tenantId, decimal subtotal, IReadOnlyList<BasketItem> items, DateTimeOffset checkedOutAt)
    {
        BasketId = basketId;
        CustomerId = customerId;
        TenantId = tenantId;
        Subtotal = subtotal;
        Items = items;
        CheckedOutAt = checkedOutAt;
    }

    /// <summary>Gets the checked-out basket identifier.</summary>
    public Guid BasketId { get; }

    /// <summary>Gets the owning customer identifier.</summary>
    public Guid? CustomerId { get; }

    /// <summary>Gets the owning tenant identifier.</summary>
    public string TenantId { get; }

    /// <summary>Gets the basket subtotal.</summary>
    public decimal Subtotal { get; }

    /// <summary>Gets the items at checkout time.</summary>
    public IReadOnlyList<BasketItem> Items { get; }

    /// <summary>Gets the checkout timestamp.</summary>
    public DateTimeOffset CheckedOutAt { get; }
}
```

- [ ] **Step 4: Add lifecycle methods to `Basket.cs`** — insert after `Clear()` and before `EnsureActive()`. Add `using Baskets.Domain.DomainEvents;` to the top of the file (keep usings ordered).

```csharp
    /// <summary>Marks the basket as checked out and raises <see cref="BasketCheckedOut"/>.</summary>
    public void Checkout()
    {
        EnsureActive();
        if (_items.Count == 0)
        {
            throw new InvalidOperationException("Cannot check out an empty basket.");
        }

        Status = BasketStatus.CheckedOut;
        AddDomainEvent(new BasketCheckedOut(
            Id,
            CustomerId,
            TenantId,
            Subtotal,
            _items.ToList(),
            DateTimeOffset.UtcNow));
    }

    /// <summary>Absorbs the items of another basket (merge by product, summing quantities) and marks it merged.</summary>
    /// <param name="source">The basket to merge into this one.</param>
    public void MergeFrom(Basket source)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureActive();

        foreach (BasketItem item in source._items)
        {
            AddItem(item.ProductId, item.ProductName, item.UnitPrice, item.Quantity);
        }

        source.Status = BasketStatus.Merged;
    }

    /// <summary>Transfers ownership of a guest basket to a customer.</summary>
    /// <param name="customerId">The customer taking ownership.</param>
    public void AssignToCustomer(Guid customerId)
    {
        CustomerId = customerId;
        AnonymousToken = null;
    }
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/unit/Basket.UnitTests/Basket.UnitTests.csproj --filter BasketLifecycleTests`
Expected: PASS (4 tests).

- [ ] **Step 6: Commit**

```bash
git add src/services/commerce/basket/Basket.Domain tests/unit/Basket.UnitTests
git commit -m "feat(basket): add checkout, merge, and ownership transfer"
```

---

### Task 5: Persistence — DbContext trio, configuration, repositories, DI

**Files:**
- Create: `src/services/commerce/basket/Basket.Application/Database/BasketDbContextBase.cs`
- Create: `src/services/commerce/basket/Basket.Application/Database/BasketDbContext.cs`
- Create: `src/services/commerce/basket/Basket.Application/Database/Configurations/BasketConfiguration.cs`
- Create: `src/services/commerce/basket/Basket.Host/Database/BasketReadDbContext.cs`
- Create: `src/services/commerce/basket/Basket.Host/Database/BasketWriteRepository.cs`
- Create: `src/services/commerce/basket/Basket.Host/Database/BasketReadRepository.cs`
- Create: `src/services/commerce/basket/Basket.Host/Database/BasketPersistenceExtensions.cs`
- Create: `src/services/commerce/basket/Basket.Host/Database/BasketDbContextDesignTimeFactory.cs`
- Create: `tests/unit/Basket.UnitTests/BasketDbContextTests.cs`

**Interfaces:**
- Produces: `BasketDbContext` (write leaf, migration target), `BasketReadDbContext` (NoTracking), `AddBasketPersistence(this WebApplicationBuilder)`.

- [ ] **Step 1: Create `BasketDbContextBase.cs`** (mirror `OrderDbContextBase`)

```csharp
using Baskets.Domain.Entities;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.MultiTenant;

namespace Baskets.Application.Database;

/// <summary>
/// Abstract basket context that defines the entity model exactly once. The write and read
/// contexts derive from it.
/// </summary>
/// <param name="options">The options used to configure the context.</param>
/// <param name="tenantContextAccessor">The accessor used to resolve the current tenant.</param>
public abstract class BasketDbContextBase(DbContextOptions options, IMultiTenantContextAccessor<TenantDetails> tenantContextAccessor)
    : BaseDbContext(options, tenantAccessor: tenantContextAccessor)
{
    /// <summary>Gets the set of tracked baskets.</summary>
    public DbSet<Basket> Baskets => Set<Basket>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Owned-type configuration must run before base.OnModelCreating so Finbuckle does not
        // discover Basket.Items (OwnsMany) as a plain entity before it is marked owned.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BasketDbContextBase).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
```

- [ ] **Step 2: Create `BasketDbContext.cs`** (write leaf)

```csharp
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Infrastructure.MultiTenant;

namespace Baskets.Application.Database;

/// <summary>
/// The basket write context (change tracking enabled). Owns EF Core migrations.
/// </summary>
/// <param name="options">The options used to configure the context.</param>
/// <param name="tenantContextAccessor">The accessor used to resolve the current tenant.</param>
public class BasketDbContext(DbContextOptions options, IMultiTenantContextAccessor<TenantDetails> tenantContextAccessor)
    : BasketDbContextBase(options, tenantContextAccessor);
```

- [ ] **Step 3: Create `Configurations/BasketConfiguration.cs`** (mirror `OrderConfiguration`, owned items keyed by owner + product; index the lookup columns)

```csharp
using Baskets.Domain.Entities;
using Baskets.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Baskets.Application.Database.Configurations;

/// <summary>
/// Configures the EF Core mapping for the <see cref="Basket"/> aggregate and its owned items.
/// </summary>
public sealed class BasketConfiguration : IEntityTypeConfiguration<Basket>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Basket> builder)
    {
        builder.ToTable("Baskets");
        builder.HasKey(basket => basket.Id);
        builder.Property(basket => basket.TenantId).HasMaxLength(64);
        builder.Ignore(basket => basket.DomainEvents);

        builder.Property(basket => basket.Status)
            .HasConversion(status => status.Value, value => BasketStatus.FromValue(value));

        // Lookups used by get-or-create and merge.
        builder.HasIndex(basket => new { basket.TenantId, basket.CustomerId, basket.Status });
        builder.HasIndex(basket => new { basket.TenantId, basket.AnonymousToken, basket.Status });

        builder.OwnsMany(basket => basket.Items, items =>
        {
            items.ToTable("BasketItems");
            items.WithOwner().HasForeignKey("BasketId");
            items.HasKey("BasketId", nameof(BasketItem.ProductId));
            items.Property(item => item.ProductName).HasMaxLength(512);
            items.Ignore(item => item.LineTotal);
        });
    }
}
```

> Note: `Basket.Items` is exposed as `IReadOnlyList<BasketItem>` backed by a `List<BasketItem>` field. EF Core maps the backing field for the owned collection — this matches how EF handles read-only navigation exposure. If EF cannot resolve the backing field automatically, add `builder.Navigation(b => b.Items).HasField("_items");` before `OwnsMany`.

- [ ] **Step 4: Create `Basket.Host/Database/BasketReadDbContext.cs`** (mirror `OrderReadDbContext`)

```csharp
using Baskets.Application.Database;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Infrastructure.MultiTenant;

namespace Baskets.Host.Database;

/// <summary>
/// The basket read context (change tracking disabled).
/// </summary>
/// <param name="options">The options used to configure the context.</param>
/// <param name="tenantContextAccessor">The accessor providing the current tenant context.</param>
public class BasketReadDbContext(DbContextOptions options, IMultiTenantContextAccessor<TenantDetails> tenantContextAccessor)
    : BasketDbContextBase(options, tenantContextAccessor)
{
    /// <inheritdoc/>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    }
}
```

- [ ] **Step 5: Create `BasketWriteRepository.cs` and `BasketReadRepository.cs`** (mirror order's)

```csharp
// BasketWriteRepository.cs
using Baskets.Application.Database;
using SharedKernel.Core.Domain;
using SharedKernel.Infrastructure.Database.EFCore;

namespace Baskets.Host.Database;

/// <summary>Basket write repository bound to <see cref="BasketDbContext"/>, registered as an open generic.</summary>
/// <typeparam name="TEntity">The aggregate entity type.</typeparam>
/// <typeparam name="TId">The entity identifier type.</typeparam>
/// <param name="dbContext">The basket write context.</param>
/// <param name="httpContextAccessor">The HTTP context accessor used for audit stamping on bulk deletes.</param>
public sealed class BasketWriteRepository<TEntity, TId>(BasketDbContext dbContext, IHttpContextAccessor httpContextAccessor)
    : GenericWriteRepository<TEntity, TId, BasketDbContext>(dbContext, httpContextAccessor)
    where TEntity : BaseEntity;
```

```csharp
// BasketReadRepository.cs
using SharedKernel.Core.Domain;
using SharedKernel.Infrastructure.Database.EFCore;

namespace Baskets.Host.Database;

/// <summary>Basket read repository bound to <see cref="BasketReadDbContext"/> (NoTracking), registered as an open generic.</summary>
/// <typeparam name="TReadModel">The read model type.</typeparam>
/// <typeparam name="TId">The identifier type.</typeparam>
/// <param name="dbContext">The basket read context.</param>
public sealed class BasketReadRepository<TReadModel, TId>(BasketReadDbContext dbContext)
    : GenericReadRepository<TReadModel, TId, BasketReadDbContext>(dbContext)
    where TReadModel : class, IReadModel<TId>;
```

- [ ] **Step 6: Create `BasketPersistenceExtensions.cs`** (mirror `OrderPersistenceExtensions`)

```csharp
using Baskets.Application.Database;
using SharedKernel.Core.Database;
using SharedKernel.Infrastructure.Database;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.Database.MultiTenant;

namespace Baskets.Host.Database;

/// <summary>
/// Registers the basket persistence stack: tenant-aware read/write contexts, generic
/// repositories, and the unit of work.
/// </summary>
public static class BasketPersistenceExtensions
{
    /// <summary>Adds the basket read/write contexts, repositories and unit of work to the host.</summary>
    /// <param name="builder">The web application builder.</param>
    /// <returns>The same builder for chaining.</returns>
    public static WebApplicationBuilder AddBasketPersistence(this WebApplicationBuilder builder)
    {
        var write = CodegenConnectionString.ResolveRequired(builder.Configuration, "BasketWrite", "Default");
        var read = builder.Configuration.GetConnectionString("BasketRead") ?? write;

        builder.AddHybridMultiTenantDbContexts<BasketDbContext, BasketReadDbContext>(
            migrationsAssembly: typeof(Program).Assembly,
            defaultWriteConnectionString: write,
            defaultReadConnectionString: read,
            serviceName: "basket");

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped(typeof(IGenericReadRepository<,>), typeof(BasketReadRepository<,>));
        builder.Services.AddScoped(typeof(IGenericWriteRepository<,>), typeof(BasketWriteRepository<,>));
        builder.Services.AddScoped<IUnitOfWork>(sp => new UnitOfWork<BasketDbContext>(sp.GetRequiredService<BasketDbContext>()));

        return builder;
    }
}
```

- [ ] **Step 7: Create `BasketDbContextDesignTimeFactory.cs`** (mirror order's)

```csharp
using Baskets.Application.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Baskets.Host.Database;

/// <summary>
/// Design-time factory for <see cref="BasketDbContext"/> used by EF Core migrations tooling.
/// </summary>
public sealed class BasketDbContextDesignTimeFactory : IDesignTimeDbContextFactory<BasketDbContext>
{
    /// <inheritdoc/>
    public BasketDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("BASKET_DESIGN_TIME_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=basket_design;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<BasketDbContext>();
        optionsBuilder.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsAssembly(typeof(BasketDbContextDesignTimeFactory).Assembly.FullName));

        return new BasketDbContext(optionsBuilder.Options, tenantContextAccessor: null!);
    }
}
```

- [ ] **Step 8: Write the context test** `tests/unit/Basket.UnitTests/BasketDbContextTests.cs` (mirror `OrderDbContextTests` — verify the model builds against the InMemory provider; adjust if the reference test uses a different assertion)

```csharp
using Baskets.Application.Database;
using Baskets.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Baskets.UnitTests;

public sealed class BasketDbContextTests
{
    [Fact]
    public void Model_IncludesBasketWithOwnedItems()
    {
        var options = new DbContextOptionsBuilder<BasketDbContext>()
            .UseInMemoryDatabase("basket-model-test")
            .Options;

        using var context = new BasketDbContext(options, tenantContextAccessor: null!);

        var entity = context.Model.FindEntityType(typeof(Basket));
        Assert.NotNull(entity);
        Assert.NotNull(entity!.FindNavigation(nameof(Basket.Items)));
    }
}
```

> If the InMemory provider cannot construct the context because `BaseDbContext.OnConfiguring`/tenant plumbing requires a non-null accessor, mirror exactly what `tests/unit/Order.UnitTests/OrderDbContextTests.cs` does (read that file first) — it is the authoritative pattern for constructing the context under test.

- [ ] **Step 9: Build + run the context test**

Run: `dotnet test tests/unit/Basket.UnitTests/Basket.UnitTests.csproj --filter BasketDbContextTests`
Expected: PASS.

- [ ] **Step 10: Commit**

```bash
git add src/services/commerce/basket tests/unit/Basket.UnitTests
git commit -m "feat(basket): add read/write DbContexts, configuration, repositories, DI"
```

---

### Task 6: Response DTOs + Mapperly mapper

**Files:**
- Create: `src/services/commerce/basket/Basket.Application/Baskets/Responses/BasketDto.cs`
- Create: `src/services/commerce/basket/Basket.Application/Baskets/Responses/BasketItemDto.cs`
- Create: `src/services/commerce/basket/Basket.Application/Baskets/Mapping/BasketMapper.cs`
- Create: `tests/unit/Basket.UnitTests/BasketMapperTests.cs`

**Interfaces:**
- Produces: `BasketDto(Guid Id, Guid? CustomerId, Guid? AnonymousToken, string Status, IReadOnlyList<BasketItemDto> Items, decimal Subtotal)`; `BasketItemDto(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal)`; `static BasketDto BasketMapper.ToDto(this Basket entity)`.

- [ ] **Step 1: Write the failing test** `tests/unit/Basket.UnitTests/BasketMapperTests.cs`

```csharp
using Baskets.Application.Baskets.Mapping;
using Baskets.Domain.Entities;
using Xunit;

namespace Baskets.UnitTests;

public sealed class BasketMapperTests
{
    [Fact]
    public void ToDto_MapsStatusNameAndItems()
    {
        var basket = Basket.CreateForCustomer(Guid.NewGuid(), "tenant-1");
        basket.AddItem(Guid.NewGuid(), "Widget", 10m, 2);

        var dto = BasketMapper.ToDto(basket);

        Assert.Equal("Active", dto.Status);
        Assert.Equal(20m, dto.Subtotal);
        Assert.Single(dto.Items);
        Assert.Equal(20m, dto.Items[0].LineTotal);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/unit/Basket.UnitTests/Basket.UnitTests.csproj --filter BasketMapperTests`
Expected: FAIL — mapper/DTOs missing.

- [ ] **Step 3: Create `BasketItemDto.cs`**

```csharp
namespace Baskets.Application.Baskets.Responses;

/// <summary>Represents a single basket line in API responses.</summary>
/// <param name="ProductId">The product identifier.</param>
/// <param name="ProductName">The product name.</param>
/// <param name="UnitPrice">The unit price.</param>
/// <param name="Quantity">The quantity.</param>
/// <param name="LineTotal">The line total.</param>
public sealed record BasketItemDto(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal);
```

- [ ] **Step 4: Create `BasketDto.cs`**

```csharp
namespace Baskets.Application.Baskets.Responses;

/// <summary>Represents a basket together with its items in API responses.</summary>
/// <param name="Id">The basket identifier.</param>
/// <param name="CustomerId">The owning customer identifier, or null for a guest basket.</param>
/// <param name="AnonymousToken">The guest token, or null once owned by a customer.</param>
/// <param name="Status">The basket status name.</param>
/// <param name="Items">The basket items.</param>
/// <param name="Subtotal">The basket subtotal.</param>
public sealed record BasketDto(
    Guid Id,
    Guid? CustomerId,
    Guid? AnonymousToken,
    string Status,
    IReadOnlyList<BasketItemDto> Items,
    decimal Subtotal);
```

- [ ] **Step 5: Create `BasketMapper.cs`** (mirror `OrderMapper`; map `Status.Name`)

```csharp
using Baskets.Application.Baskets.Responses;
using Baskets.Domain.Entities;
using Riok.Mapperly.Abstractions;

namespace Baskets.Application.Baskets.Mapping;

/// <summary>Mapperly-generated mappings between basket entities and their DTOs.</summary>
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class BasketMapper
{
    /// <summary>Maps a <see cref="Basket"/> entity to a <see cref="BasketDto"/>.</summary>
    /// <param name="entity">The basket entity to map.</param>
    /// <returns>The mapped basket response.</returns>
    [MapProperty("Status.Name", nameof(BasketDto.Status))]
    public static partial BasketDto ToDto(this Basket entity);

    /// <summary>Maps a basket item value object to its DTO.</summary>
    /// <param name="item">The item to map.</param>
    /// <returns>The mapped item DTO.</returns>
    public static partial BasketItemDto ToDto(this Baskets.Domain.ValueObjects.BasketItem item);
}
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/unit/Basket.UnitTests/Basket.UnitTests.csproj --filter BasketMapperTests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/services/commerce/basket/Basket.Application tests/unit/Basket.UnitTests
git commit -m "feat(basket): add response DTOs and Mapperly mapper"
```

---

### Task 7: Specifications

**Files:**
- Create: `src/services/commerce/basket/Basket.Application/Baskets/ReadModels/BasketByIdSpec.cs`
- Create: `src/services/commerce/basket/Basket.Application/Baskets/ReadModels/ActiveBasketByCustomerSpec.cs`
- Create: `src/services/commerce/basket/Basket.Application/Baskets/ReadModels/ActiveBasketByTokenSpec.cs`

**Interfaces:**
- Produces: three `Specification<Basket>` classes that `.Include` items and filter appropriately.

- [ ] **Step 1: Create `BasketByIdSpec.cs`**

```csharp
using Ardalis.Specification;
using Baskets.Domain.Entities;

namespace Baskets.Application.Baskets.ReadModels;

/// <summary>Selects a single basket by its identifier, including its items.</summary>
public sealed class BasketByIdSpec : Specification<Basket>
{
    /// <summary>Initializes a new instance of the <see cref="BasketByIdSpec"/> class.</summary>
    /// <param name="basketId">The basket identifier to match.</param>
    public BasketByIdSpec(Guid basketId) => Query.Where(basket => basket.Id == basketId).Include(basket => basket.Items);
}
```

- [ ] **Step 2: Create `ActiveBasketByCustomerSpec.cs`**

```csharp
using Ardalis.Specification;
using Baskets.Domain.Entities;
using Baskets.Domain.ValueObjects;

namespace Baskets.Application.Baskets.ReadModels;

/// <summary>Selects the active basket owned by a customer, including its items.</summary>
public sealed class ActiveBasketByCustomerSpec : Specification<Basket>
{
    /// <summary>Initializes a new instance of the <see cref="ActiveBasketByCustomerSpec"/> class.</summary>
    /// <param name="customerId">The owning customer identifier.</param>
    public ActiveBasketByCustomerSpec(Guid customerId) =>
        Query.Where(basket => basket.CustomerId == customerId && basket.Status == BasketStatus.Active)
            .Include(basket => basket.Items);
}
```

- [ ] **Step 3: Create `ActiveBasketByTokenSpec.cs`**

```csharp
using Ardalis.Specification;
using Baskets.Domain.Entities;
using Baskets.Domain.ValueObjects;

namespace Baskets.Application.Baskets.ReadModels;

/// <summary>Selects the active guest basket identified by an anonymous token, including its items.</summary>
public sealed class ActiveBasketByTokenSpec : Specification<Basket>
{
    /// <summary>Initializes a new instance of the <see cref="ActiveBasketByTokenSpec"/> class.</summary>
    /// <param name="anonymousToken">The guest token.</param>
    public ActiveBasketByTokenSpec(Guid anonymousToken) =>
        Query.Where(basket => basket.AnonymousToken == anonymousToken && basket.Status == BasketStatus.Active)
            .Include(basket => basket.Items);
}
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build src/services/commerce/basket/Basket.Application/Basket.Application.csproj`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/services/commerce/basket/Basket.Application/Baskets/ReadModels
git commit -m "feat(basket): add basket specifications"
```

---

### Task 8: Shared cross-service contract `BasketCheckedOutIntegrationEvent`

**Files:**
- Create: `src/shared/SharedKernel.Events/BasketCheckedOutIntegrationEvent.cs`
- Create: `src/shared/SharedKernel.Events/BasketCheckedOutLine.cs`

**Interfaces:**
- Produces: `BasketCheckedOutIntegrationEvent : IntegrationEvent` (MemoryPackable) with `Guid BasketId`, `Guid? CustomerId`, `string TenantId`, `decimal Subtotal`, `List<BasketCheckedOutLine> Items`, `DateTimeOffset CheckedOutAt`; `BasketCheckedOutLine(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal)`.
- Consumed by: Task 11 (publisher) and Task 14 (Order consumer).

> Placed in `SharedKernel.Events` (not `Basket.Application`) so `Order` can consume it without referencing `basket`. `SharedKernel.Events` already references `MemoryPack` and `SharedKernel.Core`.

- [ ] **Step 1: Create `BasketCheckedOutLine.cs`**

```csharp
using MemoryPack;

namespace SharedKernel.Events;

/// <summary>A single line carried by <see cref="BasketCheckedOutIntegrationEvent"/>.</summary>
[MemoryPackable]
public partial class BasketCheckedOutLine
{
    /// <summary>Initializes a new instance of the <see cref="BasketCheckedOutLine"/> class.</summary>
    public BasketCheckedOutLine()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="BasketCheckedOutLine"/> class.</summary>
    /// <param name="productId">The product identifier.</param>
    /// <param name="productName">The product name.</param>
    /// <param name="unitPrice">The unit price.</param>
    /// <param name="quantity">The quantity.</param>
    /// <param name="lineTotal">The line total.</param>
    [MemoryPackConstructor]
    public BasketCheckedOutLine(Guid productId, string productName, decimal unitPrice, int quantity, decimal lineTotal)
    {
        ProductId = productId;
        ProductName = productName;
        UnitPrice = unitPrice;
        Quantity = quantity;
        LineTotal = lineTotal;
    }

    /// <summary>Gets or sets the product identifier.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Gets or sets the product name.</summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>Gets or sets the unit price.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Gets or sets the quantity.</summary>
    public int Quantity { get; set; }

    /// <summary>Gets or sets the line total.</summary>
    public decimal LineTotal { get; set; }
}
```

- [ ] **Step 2: Create `BasketCheckedOutIntegrationEvent.cs`** (mirror `OrderPlacedIntegrationEvent` structure; the constructor-from-domain-event lives here but takes primitives to avoid referencing `Basket.Domain` from SharedKernel — see note)

```csharp
using MemoryPack;
using SharedKernel.Core.Events;

namespace SharedKernel.Events;

/// <summary>
/// Integration event published when a basket has been checked out. Consumed by the order service
/// to create an order.
/// </summary>
[MemoryPackable]
public partial class BasketCheckedOutIntegrationEvent : IntegrationEvent
{
    /// <summary>Initializes a new instance of the <see cref="BasketCheckedOutIntegrationEvent"/> class.</summary>
    [MemoryPackConstructor]
    public BasketCheckedOutIntegrationEvent()
    {
    }

    /// <summary>Gets or sets the checked-out basket identifier.</summary>
    public Guid BasketId { get; set; }

    /// <summary>Gets or sets the owning customer identifier.</summary>
    public Guid? CustomerId { get; set; }

    /// <summary>Gets or sets the owning tenant identifier.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets or sets the basket subtotal.</summary>
    public decimal Subtotal { get; set; }

    /// <summary>Gets or sets the checkout timestamp.</summary>
    public DateTimeOffset CheckedOutAt { get; set; }

    /// <summary>Gets or sets the lines at checkout time.</summary>
    public List<BasketCheckedOutLine> Items { get; set; } = [];
}
```

> The domain→event translation (mapping `BasketCheckedOut` to this type) is done in the `Basket.Application` handler in Task 11, keeping `SharedKernel.Events` free of any `Basket.Domain` reference.

- [ ] **Step 3: Build**

Run: `dotnet build src/shared/SharedKernel.Events/SharedKernel.Events.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/shared/SharedKernel.Events
git commit -m "feat(events): add BasketCheckedOut integration event contract"
```

---

### Task 9: Identity accessor + `GetOrCreateBasket` command/handler

**Files:**
- Create: `src/services/commerce/basket/Basket.Application/Baskets/IBasketIdentityAccessor.cs`
- Create: `src/services/commerce/basket/Basket.Application/Baskets/BasketOptions.cs`
- Create: `src/services/commerce/basket/Basket.Application/Baskets/Features/GetOrCreateBasket/V1/GetOrCreateBasketCommand.cs`
- Create: `src/services/commerce/basket/Basket.Application/Baskets/Features/GetOrCreateBasket/V1/GetOrCreateBasketHandler.cs`
- Create: `tests/unit/Basket.UnitTests/GetOrCreateBasketHandlerTests.cs`

**Interfaces:**
- Produces: `IBasketIdentityAccessor { Guid? CustomerId; Guid? AnonymousToken; Guid EnsureAnonymousToken(); }`; `GetOrCreateBasketCommand() : ICommand<BasketDto>`; `static Task<BasketDto> GetOrCreateBasketHandler.Handle(GetOrCreateBasketCommand, IGenericWriteRepository<Basket,Guid>, IUnitOfWork, IBasketIdentityAccessor, ITenantInfo, CancellationToken)`.
- Consumes: specs from Task 7, `BasketMapper`, `BasketDto`.

- [ ] **Step 1: Create `IBasketIdentityAccessor.cs`**

```csharp
namespace Baskets.Application.Baskets;

/// <summary>
/// Resolves the current basket owner identity: an authenticated customer, or a guest token.
/// Implemented in the host over the HTTP context.
/// </summary>
public interface IBasketIdentityAccessor
{
    /// <summary>Gets the authenticated customer identifier, or null for a guest.</summary>
    Guid? CustomerId { get; }

    /// <summary>Gets the guest basket token from the request, or null if absent.</summary>
    Guid? AnonymousToken { get; }

    /// <summary>Returns the existing guest token or mints a new one when absent.</summary>
    /// <returns>A guest basket token.</returns>
    Guid EnsureAnonymousToken();
}
```

- [ ] **Step 2: Create `BasketOptions.cs`**

```csharp
namespace Baskets.Application.Baskets;

/// <summary>Configuration options for the basket service.</summary>
public sealed class BasketOptions
{
    /// <summary>Gets the maximum number of distinct lines allowed in a basket.</summary>
    public int MaxItemsPerBasket { get; init; } = 100;

    /// <summary>Gets the maximum quantity allowed on a single line.</summary>
    public int MaxQuantityPerLine { get; init; } = 999;
}
```

- [ ] **Step 3: Create `GetOrCreateBasketCommand.cs`**

```csharp
using Baskets.Application.Baskets.Responses;
using SharedKernel.Core.CQRS;

namespace Baskets.Application.Baskets.Features.GetOrCreateBasket.V1;

/// <summary>Command that returns the caller's active basket, creating one if none exists.</summary>
public sealed record GetOrCreateBasketCommand : ICommand<BasketDto>;
```

- [ ] **Step 4: Write the failing test** `tests/unit/Basket.UnitTests/GetOrCreateBasketHandlerTests.cs`

```csharp
using Ardalis.Specification;
using Baskets.Application.Baskets;
using Baskets.Application.Baskets.Features.GetOrCreateBasket.V1;
using Baskets.Domain.Entities;
using Finbuckle.MultiTenant.Abstractions;
using NSubstitute;
using SharedKernel.Core.Database;
using Xunit;

namespace Baskets.UnitTests;

public sealed class GetOrCreateBasketHandlerTests
{
    [Fact]
    public async Task Handle_CustomerWithNoBasket_CreatesAndCommits()
    {
        var repository = Substitute.For<IGenericWriteRepository<Basket, Guid>>();
        repository.FirstOrDefaultAsync(Arg.Any<ISpecification<Basket>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Basket?>(null));
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var identity = Substitute.For<IBasketIdentityAccessor>();
        identity.CustomerId.Returns(Guid.NewGuid());
        var tenant = Substitute.For<ITenantInfo>();
        tenant.Id.Returns("tenant-1");

        var dto = await GetOrCreateBasketHandler.Handle(
            new GetOrCreateBasketCommand(), repository, unitOfWork, identity, tenant, CancellationToken.None);

        await repository.Received(1).AddAsync(Arg.Any<Basket>(), Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        Assert.Equal("Active", dto.Status);
    }
}
```

> Verify `ITenantInfo` lives in `Finbuckle.MultiTenant.Abstractions` and exposes `Id` — read `src/services/commerce/order/Order.Application/.../*Handler.cs` tenant usage or `src/services/commerce/AGENTS.md` ("Tenant Context") to confirm the exact interface/namespace before finalizing; adjust the using if needed.

- [ ] **Step 5: Run test to verify it fails**

Run: `dotnet test tests/unit/Basket.UnitTests/Basket.UnitTests.csproj --filter GetOrCreateBasketHandlerTests`
Expected: FAIL — handler missing.

- [ ] **Step 6: Create `GetOrCreateBasketHandler.cs`**

```csharp
using Baskets.Application.Baskets.Mapping;
using Baskets.Application.Baskets.ReadModels;
using Baskets.Application.Baskets.Responses;
using Baskets.Domain.Entities;
using Finbuckle.MultiTenant.Abstractions;
using SharedKernel.Core.Database;

namespace Baskets.Application.Baskets.Features.GetOrCreateBasket.V1;

/// <summary>Handles <see cref="GetOrCreateBasketCommand"/> with get-or-create semantics.</summary>
public static class GetOrCreateBasketHandler
{
    /// <summary>Returns the caller's active basket, creating and committing a new one on miss.</summary>
    /// <param name="command">The command.</param>
    /// <param name="repository">The write repository.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    /// <param name="identity">The basket identity accessor.</param>
    /// <param name="tenant">The current tenant.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The active basket as a <see cref="BasketDto"/>.</returns>
    public static async Task<BasketDto> Handle(
        GetOrCreateBasketCommand command,
        IGenericWriteRepository<Basket, Guid> repository,
        IUnitOfWork unitOfWork,
        IBasketIdentityAccessor identity,
        ITenantInfo tenant,
        CancellationToken ct)
    {
        Basket? basket = identity.CustomerId is Guid customerId
            ? await repository.FirstOrDefaultAsync(new ActiveBasketByCustomerSpec(customerId), enableTracking: true, ct).ConfigureAwait(false)
            : await repository.FirstOrDefaultAsync(new ActiveBasketByTokenSpec(identity.EnsureAnonymousToken()), enableTracking: true, ct).ConfigureAwait(false);

        if (basket is null)
        {
            basket = identity.CustomerId is Guid ownerId
                ? Basket.CreateForCustomer(ownerId, tenant.Id ?? string.Empty)
                : Basket.CreateAnonymous(identity.EnsureAnonymousToken(), tenant.Id ?? string.Empty);

            await repository.AddAsync(basket, ct).ConfigureAwait(false);
            await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return BasketMapper.ToDto(basket);
    }
}
```

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet test tests/unit/Basket.UnitTests/Basket.UnitTests.csproj --filter GetOrCreateBasketHandlerTests`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/services/commerce/basket/Basket.Application tests/unit/Basket.UnitTests
git commit -m "feat(basket): add identity accessor, options, get-or-create handler"
```

---

### Task 10: Mutation commands/handlers (AddItem, UpdateItemQuantity, RemoveItem, ClearBasket)

**Files:**
- Create (per use case under `Basket.Application/Baskets/Features/{UseCase}/V1/`): `{UseCase}Command.cs`, `{UseCase}Handler.cs` for `AddItem`, `UpdateItemQuantity`, `RemoveItem`, `ClearBasket`.
- Create: `tests/unit/Basket.UnitTests/AddItemHandlerTests.cs`

**Interfaces:**
- Each command carries `Guid BasketId` plus its payload and returns `ICommand<BasketDto>`. Each handler load-to-mutates (`enableTracking: true`), calls the matching `Basket` method, commits once, returns `BasketMapper.ToDto`.

- [ ] **Step 1: Write the failing test** `tests/unit/Basket.UnitTests/AddItemHandlerTests.cs`

```csharp
using Ardalis.Specification;
using Baskets.Application.Baskets.Features.AddItem.V1;
using Baskets.Domain.Entities;
using NSubstitute;
using SharedKernel.Core.Database;
using Xunit;

namespace Baskets.UnitTests;

public sealed class AddItemHandlerTests
{
    [Fact]
    public async Task Handle_AddsItemAndCommits()
    {
        var basket = Basket.CreateForCustomer(Guid.NewGuid(), "tenant-1");
        var repository = Substitute.For<IGenericWriteRepository<Basket, Guid>>();
        repository.FirstOrDefaultAsync(Arg.Any<ISpecification<Basket>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Basket?>(basket));
        var unitOfWork = Substitute.For<IUnitOfWork>();

        var command = new AddItemCommand(basket.Id, Guid.NewGuid(), "Widget", 10m, 2);
        var dto = await AddItemHandler.Handle(command, repository, unitOfWork, CancellationToken.None);

        Assert.Single(dto.Items);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/unit/Basket.UnitTests/Basket.UnitTests.csproj --filter AddItemHandlerTests`
Expected: FAIL.

- [ ] **Step 3: Create `AddItem/V1/AddItemCommand.cs`**

```csharp
using Baskets.Application.Baskets.Responses;
using SharedKernel.Core.CQRS;

namespace Baskets.Application.Baskets.Features.AddItem.V1;

/// <summary>Command that adds an item to a basket.</summary>
/// <param name="BasketId">The target basket identifier.</param>
/// <param name="ProductId">The product identifier.</param>
/// <param name="ProductName">The product name.</param>
/// <param name="UnitPrice">The unit price.</param>
/// <param name="Quantity">The quantity to add.</param>
public sealed record AddItemCommand(Guid BasketId, Guid ProductId, string ProductName, decimal UnitPrice, int Quantity) : ICommand<BasketDto>;
```

- [ ] **Step 4: Create `AddItem/V1/AddItemHandler.cs`**

```csharp
using Baskets.Application.Baskets.Mapping;
using Baskets.Application.Baskets.ReadModels;
using Baskets.Application.Baskets.Responses;
using Baskets.Domain.Entities;
using SharedKernel.Core.Database;

namespace Baskets.Application.Baskets.Features.AddItem.V1;

/// <summary>Handles <see cref="AddItemCommand"/>.</summary>
public static class AddItemHandler
{
    /// <summary>Adds an item to the basket and commits.</summary>
    /// <param name="command">The command.</param>
    /// <param name="repository">The write repository.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The updated basket.</returns>
    public static async Task<BasketDto> Handle(
        AddItemCommand command,
        IGenericWriteRepository<Basket, Guid> repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var basket = await repository.FirstOrDefaultAsync(new BasketByIdSpec(command.BasketId), enableTracking: true, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Basket '{command.BasketId}' was not found.");

        basket.AddItem(command.ProductId, command.ProductName, command.UnitPrice, command.Quantity);
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return BasketMapper.ToDto(basket);
    }
}
```

- [ ] **Step 5: Create `UpdateItemQuantity/V1/UpdateItemQuantityCommand.cs`**

```csharp
using Baskets.Application.Baskets.Responses;
using SharedKernel.Core.CQRS;

namespace Baskets.Application.Baskets.Features.UpdateItemQuantity.V1;

/// <summary>Command that sets the quantity of a basket line.</summary>
/// <param name="BasketId">The target basket identifier.</param>
/// <param name="ProductId">The product identifier.</param>
/// <param name="Quantity">The new quantity (zero or less removes the line).</param>
public sealed record UpdateItemQuantityCommand(Guid BasketId, Guid ProductId, int Quantity) : ICommand<BasketDto>;
```

- [ ] **Step 6: Create `UpdateItemQuantity/V1/UpdateItemQuantityHandler.cs`**

```csharp
using Baskets.Application.Baskets.Mapping;
using Baskets.Application.Baskets.ReadModels;
using Baskets.Application.Baskets.Responses;
using Baskets.Domain.Entities;
using SharedKernel.Core.Database;

namespace Baskets.Application.Baskets.Features.UpdateItemQuantity.V1;

/// <summary>Handles <see cref="UpdateItemQuantityCommand"/>.</summary>
public static class UpdateItemQuantityHandler
{
    /// <summary>Updates a line quantity and commits.</summary>
    /// <param name="command">The command.</param>
    /// <param name="repository">The write repository.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The updated basket.</returns>
    public static async Task<BasketDto> Handle(
        UpdateItemQuantityCommand command,
        IGenericWriteRepository<Basket, Guid> repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var basket = await repository.FirstOrDefaultAsync(new BasketByIdSpec(command.BasketId), enableTracking: true, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Basket '{command.BasketId}' was not found.");

        basket.UpdateItemQuantity(command.ProductId, command.Quantity);
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return BasketMapper.ToDto(basket);
    }
}
```

- [ ] **Step 7: Create `RemoveItem/V1/RemoveItemCommand.cs`**

```csharp
using Baskets.Application.Baskets.Responses;
using SharedKernel.Core.CQRS;

namespace Baskets.Application.Baskets.Features.RemoveItem.V1;

/// <summary>Command that removes a line from a basket.</summary>
/// <param name="BasketId">The target basket identifier.</param>
/// <param name="ProductId">The product identifier to remove.</param>
public sealed record RemoveItemCommand(Guid BasketId, Guid ProductId) : ICommand<BasketDto>;
```

- [ ] **Step 8: Create `RemoveItem/V1/RemoveItemHandler.cs`**

```csharp
using Baskets.Application.Baskets.Mapping;
using Baskets.Application.Baskets.ReadModels;
using Baskets.Application.Baskets.Responses;
using Baskets.Domain.Entities;
using SharedKernel.Core.Database;

namespace Baskets.Application.Baskets.Features.RemoveItem.V1;

/// <summary>Handles <see cref="RemoveItemCommand"/>.</summary>
public static class RemoveItemHandler
{
    /// <summary>Removes a line and commits.</summary>
    /// <param name="command">The command.</param>
    /// <param name="repository">The write repository.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The updated basket.</returns>
    public static async Task<BasketDto> Handle(
        RemoveItemCommand command,
        IGenericWriteRepository<Basket, Guid> repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var basket = await repository.FirstOrDefaultAsync(new BasketByIdSpec(command.BasketId), enableTracking: true, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Basket '{command.BasketId}' was not found.");

        basket.RemoveItem(command.ProductId);
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return BasketMapper.ToDto(basket);
    }
}
```

- [ ] **Step 9: Create `ClearBasket/V1/ClearBasketCommand.cs` and `ClearBasketHandler.cs`**

```csharp
// ClearBasketCommand.cs
using Baskets.Application.Baskets.Responses;
using SharedKernel.Core.CQRS;

namespace Baskets.Application.Baskets.Features.ClearBasket.V1;

/// <summary>Command that removes all items from a basket.</summary>
/// <param name="BasketId">The target basket identifier.</param>
public sealed record ClearBasketCommand(Guid BasketId) : ICommand<BasketDto>;
```

```csharp
// ClearBasketHandler.cs
using Baskets.Application.Baskets.Mapping;
using Baskets.Application.Baskets.ReadModels;
using Baskets.Application.Baskets.Responses;
using Baskets.Domain.Entities;
using SharedKernel.Core.Database;

namespace Baskets.Application.Baskets.Features.ClearBasket.V1;

/// <summary>Handles <see cref="ClearBasketCommand"/>.</summary>
public static class ClearBasketHandler
{
    /// <summary>Clears the basket and commits.</summary>
    /// <param name="command">The command.</param>
    /// <param name="repository">The write repository.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The cleared basket.</returns>
    public static async Task<BasketDto> Handle(
        ClearBasketCommand command,
        IGenericWriteRepository<Basket, Guid> repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var basket = await repository.FirstOrDefaultAsync(new BasketByIdSpec(command.BasketId), enableTracking: true, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Basket '{command.BasketId}' was not found.");

        basket.Clear();
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return BasketMapper.ToDto(basket);
    }
}
```

- [ ] **Step 10: Run the test to verify it passes**

Run: `dotnet test tests/unit/Basket.UnitTests/Basket.UnitTests.csproj --filter AddItemHandlerTests`
Expected: PASS.

- [ ] **Step 11: Commit**

```bash
git add src/services/commerce/basket/Basket.Application tests/unit/Basket.UnitTests
git commit -m "feat(basket): add item mutation command handlers"
```

---

### Task 11: MergeBasket + Checkout handlers + domain-event publisher

**Files:**
- Create: `src/services/commerce/basket/Basket.Application/Baskets/Features/MergeBasket/V1/MergeBasketCommand.cs`
- Create: `src/services/commerce/basket/Basket.Application/Baskets/Features/MergeBasket/V1/MergeBasketHandler.cs`
- Create: `src/services/commerce/basket/Basket.Application/Baskets/Features/Checkout/V1/CheckoutCommand.cs`
- Create: `src/services/commerce/basket/Basket.Application/Baskets/Features/Checkout/V1/CheckoutHandler.cs`
- Create: `src/services/commerce/basket/Basket.Application/Baskets/EventHandlers/DomainEvents/BasketCheckedOutHandler.cs`
- Create: `tests/unit/Basket.UnitTests/CheckoutHandlerTests.cs`

**Interfaces:**
- `MergeBasketCommand(Guid AnonymousToken) : ICommand<BasketDto>` (customer resolved via identity accessor).
- `CheckoutCommand(Guid BasketId) : ICommand<BasketDto>`.
- `BasketCheckedOutHandler.Handle(BasketCheckedOut, IMessageBus)` publishes `BasketCheckedOutIntegrationEvent`.

- [ ] **Step 1: Write the failing test** `tests/unit/Basket.UnitTests/CheckoutHandlerTests.cs`

```csharp
using Ardalis.Specification;
using Baskets.Application.Baskets.Features.Checkout.V1;
using Baskets.Domain.Entities;
using NSubstitute;
using SharedKernel.Core.Database;
using Xunit;

namespace Baskets.UnitTests;

public sealed class CheckoutHandlerTests
{
    [Fact]
    public async Task Handle_ChecksOutBasketAndCommits()
    {
        var basket = Basket.CreateForCustomer(Guid.NewGuid(), "tenant-1");
        basket.AddItem(Guid.NewGuid(), "Widget", 10m, 1);
        var repository = Substitute.For<IGenericWriteRepository<Basket, Guid>>();
        repository.FirstOrDefaultAsync(Arg.Any<ISpecification<Basket>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Basket?>(basket));
        var unitOfWork = Substitute.For<IUnitOfWork>();

        var dto = await CheckoutHandler.Handle(new CheckoutCommand(basket.Id), repository, unitOfWork, CancellationToken.None);

        Assert.Equal("CheckedOut", dto.Status);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/unit/Basket.UnitTests/Basket.UnitTests.csproj --filter CheckoutHandlerTests`
Expected: FAIL.

- [ ] **Step 3: Create `Checkout/V1/CheckoutCommand.cs`**

```csharp
using Baskets.Application.Baskets.Responses;
using SharedKernel.Core.CQRS;

namespace Baskets.Application.Baskets.Features.Checkout.V1;

/// <summary>Command that checks out a basket, converting it toward an order.</summary>
/// <param name="BasketId">The basket to check out.</param>
public sealed record CheckoutCommand(Guid BasketId) : ICommand<BasketDto>;
```

- [ ] **Step 4: Create `Checkout/V1/CheckoutHandler.cs`** — the domain event raised by `Checkout()` is dispatched by the UoW (WolverineFx outbox), which triggers `BasketCheckedOutHandler`. The handler commits once.

```csharp
using Baskets.Application.Baskets.Mapping;
using Baskets.Application.Baskets.ReadModels;
using Baskets.Application.Baskets.Responses;
using Baskets.Domain.Entities;
using SharedKernel.Core.Database;

namespace Baskets.Application.Baskets.Features.Checkout.V1;

/// <summary>Handles <see cref="CheckoutCommand"/>.</summary>
public static class CheckoutHandler
{
    /// <summary>Checks out the basket (raising the domain event) and commits.</summary>
    /// <param name="command">The command.</param>
    /// <param name="repository">The write repository.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The checked-out basket.</returns>
    public static async Task<BasketDto> Handle(
        CheckoutCommand command,
        IGenericWriteRepository<Basket, Guid> repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var basket = await repository.FirstOrDefaultAsync(new BasketByIdSpec(command.BasketId), enableTracking: true, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Basket '{command.BasketId}' was not found.");

        basket.Checkout();
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return BasketMapper.ToDto(basket);
    }
}
```

- [ ] **Step 5: Create `EventHandlers/DomainEvents/BasketCheckedOutHandler.cs`** (mirror `OrderPlacedHandler`; map domain event → integration contract here)

```csharp
using Baskets.Domain.DomainEvents;
using SharedKernel.Events;
using Wolverine;

namespace Baskets.Application.Baskets.EventHandlers.DomainEvents;

/// <summary>Publishes the integration event when a basket is checked out.</summary>
public static class BasketCheckedOutHandler
{
    /// <summary>Publishes a <see cref="BasketCheckedOutIntegrationEvent"/> for the domain event.</summary>
    /// <param name="domainEvent">The domain event.</param>
    /// <param name="bus">The message bus.</param>
    /// <returns>A task representing the publish operation.</returns>
    public static async Task Handle(BasketCheckedOut domainEvent, IMessageBus bus)
    {
        var integrationEvent = new BasketCheckedOutIntegrationEvent
        {
            BasketId = domainEvent.BasketId,
            CustomerId = domainEvent.CustomerId,
            TenantId = domainEvent.TenantId,
            Subtotal = domainEvent.Subtotal,
            CheckedOutAt = domainEvent.CheckedOutAt,
            Items = domainEvent.Items
                .Select(item => new BasketCheckedOutLine(item.ProductId, item.ProductName, item.UnitPrice, item.Quantity, item.LineTotal))
                .ToList(),
        };

        await bus.PublishAsync(integrationEvent).ConfigureAwait(false);
    }
}
```

- [ ] **Step 6: Create `MergeBasket/V1/MergeBasketCommand.cs`**

```csharp
using Baskets.Application.Baskets.Responses;
using SharedKernel.Core.CQRS;

namespace Baskets.Application.Baskets.Features.MergeBasket.V1;

/// <summary>Command that merges a guest basket into the authenticated customer's active basket.</summary>
/// <param name="AnonymousToken">The guest basket token to merge from.</param>
public sealed record MergeBasketCommand(Guid AnonymousToken) : ICommand<BasketDto>;
```

- [ ] **Step 7: Create `MergeBasket/V1/MergeBasketHandler.cs`**

```csharp
using Baskets.Application.Baskets.Mapping;
using Baskets.Application.Baskets.ReadModels;
using Baskets.Application.Baskets.Responses;
using Baskets.Domain.Entities;
using Finbuckle.MultiTenant.Abstractions;
using SharedKernel.Core.Database;

namespace Baskets.Application.Baskets.Features.MergeBasket.V1;

/// <summary>Handles <see cref="MergeBasketCommand"/>.</summary>
public static class MergeBasketHandler
{
    /// <summary>Merges the guest basket into the customer's active basket (creating it if needed) and commits.</summary>
    /// <param name="command">The command.</param>
    /// <param name="repository">The write repository.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    /// <param name="identity">The identity accessor (must have a customer).</param>
    /// <param name="tenant">The current tenant.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The customer's merged basket.</returns>
    public static async Task<BasketDto> Handle(
        MergeBasketCommand command,
        IGenericWriteRepository<Basket, Guid> repository,
        IUnitOfWork unitOfWork,
        Baskets.Application.Baskets.IBasketIdentityAccessor identity,
        ITenantInfo tenant,
        CancellationToken ct)
    {
        if (identity.CustomerId is not Guid customerId)
        {
            throw new InvalidOperationException("Merge requires an authenticated customer.");
        }

        var target = await repository.FirstOrDefaultAsync(new ActiveBasketByCustomerSpec(customerId), enableTracking: true, ct).ConfigureAwait(false);
        if (target is null)
        {
            target = Basket.CreateForCustomer(customerId, tenant.Id ?? string.Empty);
            await repository.AddAsync(target, ct).ConfigureAwait(false);
        }

        var source = await repository.FirstOrDefaultAsync(new ActiveBasketByTokenSpec(command.AnonymousToken), enableTracking: true, ct).ConfigureAwait(false);
        if (source is not null)
        {
            target.MergeFrom(source);
        }

        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return BasketMapper.ToDto(target);
    }
}
```

- [ ] **Step 8: Run the test to verify it passes**

Run: `dotnet test tests/unit/Basket.UnitTests/Basket.UnitTests.csproj --filter CheckoutHandlerTests`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/services/commerce/basket/Basket.Application tests/unit/Basket.UnitTests
git commit -m "feat(basket): add merge and checkout handlers with event publisher"
```

---

### Task 12: Host wiring — identity accessor, endpoints, Program, config

**Files:**
- Create: `src/services/commerce/basket/Basket.Host/Infrastructure/BasketIdentityAccessor.cs`
- Create endpoints under `Basket.Host/Endpoints/Baskets/` (endpoint + request + validator per use case).
- Replace: `src/services/commerce/basket/Basket.Host/Program.cs`
- Create: `src/services/commerce/basket/Basket.Host/Program.Public.cs`
- Create: `src/services/commerce/basket/Basket.Host/appsettings.json`
- Create: `src/services/commerce/basket/Basket.Host/appsettings.Development.json`

**Interfaces:**
- Consumes: all Application commands + `IBasketIdentityAccessor`.
- Produces: a bootable host exposing the basket REST API.

- [ ] **Step 1: Create `Infrastructure/BasketIdentityAccessor.cs`** — reads the `customer_id` claim, else the `X-Basket-Token` header.

```csharp
using System.Security.Claims;
using Baskets.Application.Baskets;
using MassTransit;

namespace Baskets.Host.Infrastructure;

/// <summary>
/// Resolves basket identity from the HTTP context: the authenticated <c>customer_id</c> claim, or
/// the <c>X-Basket-Token</c> header for guests.
/// </summary>
/// <param name="httpContextAccessor">The HTTP context accessor.</param>
public sealed class BasketIdentityAccessor(IHttpContextAccessor httpContextAccessor) : IBasketIdentityAccessor
{
    /// <summary>The request header carrying a guest basket token.</summary>
    public const string TokenHeader = "X-Basket-Token";

    private Guid? _minted;

    /// <inheritdoc/>
    public Guid? CustomerId
    {
        get
        {
            var value = httpContextAccessor.HttpContext?.User?.FindFirstValue("customer_id");
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    /// <inheritdoc/>
    public Guid? AnonymousToken
    {
        get
        {
            var header = httpContextAccessor.HttpContext?.Request.Headers[TokenHeader].ToString();
            return Guid.TryParse(header, out var token) ? token : _minted;
        }
    }

    /// <inheritdoc/>
    public Guid EnsureAnonymousToken() => AnonymousToken ?? (_minted ??= NewId.Next().ToGuid());
}
```

> Confirm the claim name used for the customer identifier against `src/services/commerce/AGENTS.md` ("Tenant Resolution"/JWT claims) and the Keycloak realm (`src/aspire/Teck.AppHost/realms/teck-realm.json`). If the platform uses `sub` or a different claim for the customer, adjust `FindFirstValue("customer_id")` accordingly.

- [ ] **Step 2: Create the `GetOrCreateBasket` endpoint** `Endpoints/Baskets/GetCurrentBasketEndpoint.cs` — anonymous-capable; echoes the token header.

```csharp
using Baskets.Application.Baskets;
using Baskets.Application.Baskets.Features.GetOrCreateBasket.V1;
using Baskets.Application.Baskets.Responses;
using Baskets.Host.Infrastructure;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Baskets.Host.Endpoints.Baskets;

/// <summary>Returns the caller's active basket, creating one if none exists.</summary>
/// <param name="bus">The message bus.</param>
/// <param name="identity">The identity accessor, used to echo a minted guest token.</param>
public sealed class GetCurrentBasketEndpoint(IMessageBus bus, IBasketIdentityAccessor identity)
    : AuthenticatedEndpoint<EmptyRequest, BasketDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => EndpointPermission.Anonymous("public");

    /// <inheritdoc/>
    public override async Task HandleAsync(EmptyRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<BasketDto>(new GetOrCreateBasketCommand(), ct);
        if (result.AnonymousToken is Guid token)
        {
            HttpContext.Response.Headers[BasketIdentityAccessor.TokenHeader] = token.ToString();
        }

        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Get("/baskets/current");
        Version(0);
    }
}
```

> `EmptyRequest` is FastEndpoints' built-in no-body request type (`FastEndpoints.EmptyRequest`). Add `using FastEndpoints;` if the analyzer cannot resolve it.

- [ ] **Step 3: Create the `AddItem` endpoint + request + validator** `Endpoints/Baskets/AddItemEndpoint.cs`, `AddItemRequest.cs`, `AddItemRequestValidator.cs`

```csharp
// AddItemRequest.cs
namespace Baskets.Host.Endpoints.Baskets;

/// <summary>Request to add an item to a basket.</summary>
/// <param name="BasketId">The target basket identifier.</param>
/// <param name="ProductId">The product identifier.</param>
/// <param name="ProductName">The product name.</param>
/// <param name="UnitPrice">The unit price.</param>
/// <param name="Quantity">The quantity to add.</param>
public sealed record AddItemRequest(Guid BasketId, Guid ProductId, string ProductName, decimal UnitPrice, int Quantity);
```

```csharp
// AddItemRequestValidator.cs
using FastEndpoints;
using FluentValidation;

namespace Baskets.Host.Endpoints.Baskets;

/// <summary>Validates <see cref="AddItemRequest"/> instances.</summary>
public sealed class AddItemRequestValidator : Validator<AddItemRequest>
{
    /// <summary>Initializes a new instance of the <see cref="AddItemRequestValidator"/> class.</summary>
    public AddItemRequestValidator()
    {
        RuleFor(request => request.BasketId).NotEmpty();
        RuleFor(request => request.ProductId).NotEmpty();
        RuleFor(request => request.ProductName).NotEmpty();
        RuleFor(request => request.UnitPrice).GreaterThanOrEqualTo(0);
        RuleFor(request => request.Quantity).GreaterThan(0);
    }
}
```

```csharp
// AddItemEndpoint.cs
using Baskets.Application.Baskets.Features.AddItem.V1;
using Baskets.Application.Baskets.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Baskets.Host.Endpoints.Baskets;

/// <summary>Adds an item to a basket.</summary>
/// <param name="bus">The message bus.</param>
public sealed class AddItemEndpoint(IMessageBus bus) : AuthenticatedEndpoint<AddItemRequest, BasketDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => EndpointPermission.Anonymous("public");

    /// <inheritdoc/>
    public override async Task HandleAsync(AddItemRequest request, CancellationToken ct)
    {
        var command = new AddItemCommand(request.BasketId, request.ProductId, request.ProductName, request.UnitPrice, request.Quantity);
        var result = await bus.InvokeAsync<BasketDto>(command, ct);
        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Post("/baskets/items");
        Version(0);
    }
}
```

- [ ] **Step 4: Create the `UpdateItemQuantity`, `RemoveItem`, `ClearBasket` endpoints** following the same shape (all `EndpointPermission.Anonymous("public")`):

  - `UpdateItemEndpoint` — `Put("/baskets/items/{productId}")`, request `UpdateItemRequest(Guid BasketId, Guid ProductId, int Quantity)` (bind `ProductId` from route), invokes `UpdateItemQuantityCommand`.
  - `RemoveItemEndpoint` — `Delete("/baskets/items/{productId}")`, request `RemoveItemRequest(Guid BasketId, Guid ProductId)`, invokes `RemoveItemCommand`.
  - `ClearBasketEndpoint` — `Post("/baskets/clear")`, request `ClearBasketRequest(Guid BasketId)`, invokes `ClearBasketCommand`.

  Concrete `UpdateItemEndpoint.cs` (use as the template for the other two):

```csharp
using Baskets.Application.Baskets.Features.UpdateItemQuantity.V1;
using Baskets.Application.Baskets.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Baskets.Host.Endpoints.Baskets;

/// <summary>Updates the quantity of a basket line.</summary>
/// <param name="bus">The message bus.</param>
public sealed class UpdateItemEndpoint(IMessageBus bus) : AuthenticatedEndpoint<UpdateItemRequest, BasketDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => EndpointPermission.Anonymous("public");

    /// <inheritdoc/>
    public override async Task HandleAsync(UpdateItemRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<BasketDto>(
            new UpdateItemQuantityCommand(request.BasketId, request.ProductId, request.Quantity), ct);
        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Put("/baskets/items/{productId}");
        Version(0);
    }
}
```

```csharp
// UpdateItemRequest.cs
namespace Baskets.Host.Endpoints.Baskets;

/// <summary>Request to update a basket line quantity.</summary>
/// <param name="BasketId">The target basket identifier.</param>
/// <param name="ProductId">The product identifier (bound from route).</param>
/// <param name="Quantity">The new quantity.</param>
public sealed record UpdateItemRequest(Guid BasketId, Guid ProductId, int Quantity);
```

  Create `RemoveItemRequest`, `RemoveItemEndpoint`, `ClearBasketRequest`, `ClearBasketEndpoint` analogously (each request needs a validator with `RuleFor(r => r.BasketId).NotEmpty();`). `RemoveItemEndpoint` returns the updated basket via `RemoveItemCommand`; `ClearBasketEndpoint` via `ClearBasketCommand`.

- [ ] **Step 5: Create the `merge` endpoint** `MergeBasketEndpoint.cs` (authenticated — NOT anonymous)

```csharp
using Baskets.Application.Baskets.Features.MergeBasket.V1;
using Baskets.Application.Baskets.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Baskets.Host.Endpoints.Baskets;

/// <summary>Merges a guest basket into the authenticated customer's basket.</summary>
/// <param name="bus">The message bus.</param>
public sealed class MergeBasketEndpoint(IMessageBus bus) : AuthenticatedEndpoint<MergeBasketRequest, BasketDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("basket", "merge", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(MergeBasketRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<BasketDto>(new MergeBasketCommand(request.AnonymousToken), ct);
        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Post("/baskets/merge");
        Version(0);
    }
}
```

```csharp
// MergeBasketRequest.cs
namespace Baskets.Host.Endpoints.Baskets;

/// <summary>Request to merge a guest basket into the customer basket.</summary>
/// <param name="AnonymousToken">The guest basket token.</param>
public sealed record MergeBasketRequest(Guid AnonymousToken);
```

- [ ] **Step 6: Create the `checkout` endpoint** `CheckoutBasketEndpoint.cs` (authenticated; 201)

```csharp
using Baskets.Application.Baskets.Features.Checkout.V1;
using Baskets.Application.Baskets.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Baskets.Host.Endpoints.Baskets;

/// <summary>Checks out a basket.</summary>
/// <param name="bus">The message bus.</param>
public sealed class CheckoutBasketEndpoint(IMessageBus bus) : AuthenticatedEndpoint<CheckoutBasketRequest, BasketDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("basket", "checkout", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(CheckoutBasketRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<BasketDto>(new CheckoutCommand(request.BasketId), ct);
        HttpContext.Response.Headers.Location = $"/baskets/{result.Id}";
        await Send.ResponseAsync(result, StatusCodes.Status201Created, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Post("/baskets/checkout");
        Version(0);
    }
}
```

```csharp
// CheckoutBasketRequest.cs
namespace Baskets.Host.Endpoints.Baskets;

/// <summary>Request to check out a basket.</summary>
/// <param name="BasketId">The basket to check out.</param>
public sealed record CheckoutBasketRequest(Guid BasketId);
```

- [ ] **Step 7: Replace `Program.cs`** with the order-mirroring host bootstrap (adds persistence, Keycloak, Wolverine, options, identity accessor DI)

```csharp
using Baskets.Application.Baskets;
using Baskets.Application.Database;
using Baskets.Host.Database;
using Baskets.Host.Infrastructure;
using Keycloak.AuthServices.Authentication;
using SharedKernel.Infrastructure.Auth;
using SharedKernel.Infrastructure.Behaviors;
using SharedKernel.Infrastructure.Hosting;
using SharedKernel.Infrastructure.Messaging.DeadLetter;
using Teck.ServiceDefaults;
using Wolverine;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddTeckService(typeof(Program).Assembly, builder.Configuration);
builder.AddBasketPersistence();
builder.Services.Configure<BasketOptions>(builder.Configuration.GetSection("Basket"));
builder.Services.AddScoped<IBasketIdentityAccessor, BasketIdentityAccessor>();
builder.Services.AddKeycloak(builder.Configuration, builder.Environment,
    builder.Configuration.GetSection("Keycloak").Get<KeycloakAuthenticationOptions>()!);
builder.Host.UseWolverine(opts =>
{
    opts.Discovery.IncludeAssembly(typeof(BasketDbContext).Assembly);
    opts.AddTeckBehaviors();
    opts.AddTeckDeadLetterPolicy(new DeadLetterOptions());
});
var app = builder.Build();
app.UseTeckService();
app.MapDefaultEndpoints();
return await app.RunTeckServiceAsync(args);
```

> `IBasketIdentityAccessor` is registered explicitly here because ServiceScan registers `*Service`/`*Repository`-suffixed types by convention; `BasketIdentityAccessor` does not match that suffix. Confirm against `src/services/commerce/AGENTS.md` "DI Registration" — if ServiceScan is wired to also scan the Host assembly and picks it up, the explicit line is harmless but keep it for clarity.

- [ ] **Step 8: Create `Program.Public.cs`**

```csharp
/// <summary>
/// Entry point class for the Basket host application, exposed for integration testing.
/// </summary>
public partial class Program
{
}
```

- [ ] **Step 9: Create `appsettings.json`** (mirror order's; rename Cors/log paths) and copy `appsettings.Development.json` from `order` changing `resource`/DB names to `basket` (read `src/services/commerce/order/Order.Host/appsettings.Development.json` and adapt: Keycloak `resource: "basket-api"`, connection strings `BasketWrite`/`BasketRead`).

```json
{
  "TeckService": {
    "CorsPolicyName": "BasketServiceCors",
    "CorsOrigins": [],
    "HealthPath": "/health",
    "ReadyPath": "/ready"
  },
  "SerilogOptions": {
    "EnableEnrichers": true,
    "EnableConsole": true,
    "EnableFile": false,
    "FilePath": "logs/basket-.log",
    "EnableLoki": false,
    "LokiUrl": ""
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

- [ ] **Step 10: Build the host**

Run: `dotnet build src/services/commerce/basket/Basket.Host/Basket.Host.csproj`
Expected: Build succeeded, 0 warnings/errors.

- [ ] **Step 11: Commit**

```bash
git add src/services/commerce/basket/Basket.Host
git commit -m "feat(basket): add host endpoints, identity accessor, program bootstrap"
```

---

### Task 13: EF Core migration `InitialBasket`

**Files:**
- Create: `src/services/commerce/basket/Basket.Host/Database/Migrations/*` (generated).

- [ ] **Step 1: Add the migration** (run from repo root; the design-time factory supplies the context)

Run:
```bash
dotnet ef migrations add InitialBasket \
  --project src/services/commerce/basket/Basket.Application/Basket.Application.csproj \
  --startup-project src/services/commerce/basket/Basket.Host/Basket.Host.csproj \
  --context BasketDbContext \
  --output-dir Database/Migrations
```
Expected: `Migrations/<timestamp>_InitialBasket.cs`, `.Designer.cs`, and `BasketDbContextModelSnapshot.cs` created under `Basket.Host/Database/Migrations/`.

> Mirror `order`: the migration output lives in the **Host** project even though the migration target context is in Application. Confirm the `--output-dir` resolves under `Basket.Host/Database/Migrations` (it is relative to the `--project`, so you may need `--output-dir ../Basket.Host/Database/Migrations` — verify against where `order`'s migrations physically sit: `src/services/commerce/order/Order.Host/Database/Migrations/`). Adjust the path so the generated files land in the Host project, matching order exactly.

- [ ] **Step 2: Build to confirm the migration compiles**

Run: `dotnet build src/services/commerce/basket/Basket.Host/Basket.Host.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/services/commerce/basket/Basket.Host/Database/Migrations
git commit -m "feat(basket): add InitialBasket EF Core migration"
```

---

### Task 14: Order-side consumer — create an order from `BasketCheckedOut`

**Files:**
- Create: `src/services/commerce/order/Order.Application/Orders/EventHandlers/IntegrationEvents/BasketCheckedOutConsumer.cs`
- Create: `tests/unit/Order.UnitTests/BasketCheckedOutConsumerTests.cs`

**Interfaces:**
- Consumes: `BasketCheckedOutIntegrationEvent` (from `SharedKernel.Events`), the existing `CreateOrderCommand` / `CreateOrderLine`.
- Produces: a WolverineFx consumer that maps the event to `CreateOrderCommand` and invokes it.

> `Order.Application` already references `SharedKernel.Events` — no csproj change needed. WolverineFx auto-discovers the handler (Order's `Program.cs` includes the application assembly). RabbitMQ subscription/routing for the event is handled by the platform's WolverineFx conventions; if explicit routing is required, mirror how `order` already listens for integration events (grep `Order.Host` for `PublishAsync`/listener config — if none exists yet, the convention-based transport applies).

- [ ] **Step 1: Write the failing test** `tests/unit/Order.UnitTests/BasketCheckedOutConsumerTests.cs`

```csharp
using NSubstitute;
using Orders.Application.Orders.EventHandlers.IntegrationEvents;
using Orders.Application.Orders.Features.CreateOrder.V1;
using Orders.Application.Orders.Responses;
using SharedKernel.Events;
using Wolverine;
using Xunit;

namespace Orders.UnitTests;

public sealed class BasketCheckedOutConsumerTests
{
    [Fact]
    public async Task Handle_InvokesCreateOrderWithEventLines()
    {
        var evt = new BasketCheckedOutIntegrationEvent
        {
            BasketId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            TenantId = "tenant-1",
            Subtotal = 20m,
            CheckedOutAt = DateTimeOffset.UtcNow,
            Items = [new BasketCheckedOutLine(Guid.NewGuid(), "Widget", 10m, 2, 20m)],
        };
        var bus = Substitute.For<IMessageBus>();
        bus.InvokeAsync<OrderDto>(Arg.Any<CreateOrderCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new OrderDto(Guid.NewGuid(), evt.CustomerId!.Value, "Pending", [], 20m, DateTimeOffset.UtcNow)));

        await BasketCheckedOutConsumer.Handle(evt, bus, CancellationToken.None);

        await bus.Received(1).InvokeAsync<OrderDto>(
            Arg.Is<CreateOrderCommand>(command => command.CustomerId == evt.CustomerId && command.Lines.Count == 1),
            Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/unit/Order.UnitTests/Order.UnitTests.csproj --filter BasketCheckedOutConsumerTests`
Expected: FAIL — consumer missing.

- [ ] **Step 3: Create `BasketCheckedOutConsumer.cs`**

```csharp
using Orders.Application.Orders.Features.CreateOrder.V1;
using Orders.Application.Orders.Responses;
using SharedKernel.Events;
using Wolverine;

namespace Orders.Application.Orders.EventHandlers.IntegrationEvents;

/// <summary>Creates an order in response to a basket being checked out.</summary>
public static class BasketCheckedOutConsumer
{
    /// <summary>Maps the checkout event to a create-order command and dispatches it.</summary>
    /// <param name="integrationEvent">The basket checkout event.</param>
    /// <param name="bus">The message bus.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    public static async Task Handle(BasketCheckedOutIntegrationEvent integrationEvent, IMessageBus bus, CancellationToken ct)
    {
        if (integrationEvent.CustomerId is not Guid customerId)
        {
            // Guest checkout without a customer cannot yet create an order; ignored until guest checkout exists.
            return;
        }

        var lines = integrationEvent.Items
            .Select(item => new CreateOrderLine(item.ProductId, item.ProductName, item.Quantity, item.UnitPrice))
            .ToList();

        await bus.InvokeAsync<OrderDto>(new CreateOrderCommand(customerId, lines), ct).ConfigureAwait(false);
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/unit/Order.UnitTests/Order.UnitTests.csproj --filter BasketCheckedOutConsumerTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/services/commerce/order/Order.Application tests/unit/Order.UnitTests
git commit -m "feat(order): consume BasketCheckedOut to create an order"
```

---

### Task 15: Architecture test project

**Files:**
- Create: `tests/architecture/Basket.Architecture.UnitTests/Basket.Architecture.UnitTests.csproj`
- Create: `tests/architecture/Basket.Architecture.UnitTests/BasketArchitectureTests.cs`
- Modify: `Teck.Platform.slnx`

**Interfaces:**
- Consumes: `Baskets.Domain`, `Baskets.Application`, `Baskets.Host`, `Teck.Platform.Arch.Tests`.

- [ ] **Step 1: Create the csproj** (mirror `Order.Architecture.UnitTests.csproj`, swapping `Order`→`Basket`)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>Baskets.Architecture.UnitTests</RootNamespace>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
    <PackageReference Include="TngTech.ArchUnitNET" />
    <PackageReference Include="TngTech.ArchUnitNET.xUnitV3" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\src\services\commerce\basket\Basket.Application\Basket.Application.csproj" />
    <ProjectReference Include="..\..\..\src\services\commerce\basket\Basket.Domain\Basket.Domain.csproj" />
    <ProjectReference Include="..\..\..\src\services\commerce\basket\Basket.Host\Basket.Host.csproj" />
    <ProjectReference Include="..\Teck.Platform.Arch.Tests\Teck.Platform.Arch.Tests.csproj" />
  </ItemGroup>
</Project>
```

Register in `Teck.Platform.slnx` near the other architecture tests.

- [ ] **Step 2: Create `BasketArchitectureTests.cs`** (mirror `OrderArchitectureTests`, swapping types)

```csharp
using System.Reflection;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using SharedKernel.Core.Domain;
using Teck.Platform.Arch.Tests.Rules;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Baskets.Architecture.UnitTests;

public sealed class BasketArchitectureTests : Teck.Platform.Arch.Tests.SharedTestBase
{
    private static readonly Assembly DomainAssembly = typeof(Baskets.Domain.Entities.Basket).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(Baskets.Application.Baskets.Features.Checkout.V1.CheckoutHandler).Assembly;
    private static readonly Assembly HostAssembly = typeof(Program).Assembly;

    private static readonly ArchUnitNET.Domain.Architecture BasketArchitecture = new ArchLoader()
        .LoadAssemblies(DomainAssembly, ApplicationAssembly, HostAssembly)
        .Build();

    [Fact]
    public void BasketHost_ShouldNotReferenceBasketDomainDirectly() =>
        Types().That().ResideInAssembly(HostAssembly)
            .Should().NotDependOnAny(Types().That().ResideInAssembly(DomainAssembly))
            .Because("the host must depend on the application layer, not the domain layer directly")
            .Check(BasketArchitecture);

    [Fact]
    public void BasketApplication_ShouldNotReferenceBasketHost() =>
        Types().That().ResideInAssembly(ApplicationAssembly)
            .Should().NotDependOnAny(Types().That().ResideInAssembly(HostAssembly))
            .Because("the application layer must not depend on the host layer")
            .Check(BasketArchitecture);

    [Fact]
    public void BasketAggregateRoots_ShouldImplementTenantScoped() =>
        Classes().That().ImplementInterface(typeof(IAggregateRoot))
            .Should().ImplementInterface(typeof(ITenantScoped))
            .Because("tenant-owned basket aggregates must be scoped to a tenant")
            .Check(BasketArchitecture);

    [Fact]
    public void BasketApplication_ShouldNotDependOnDbContextOrAardalisRepository() =>
        Types().That().ResideInAssembly(ApplicationAssembly)
            .And().DoNotHaveFullNameContaining("DbContext")
            .Should().NotDependOnAny(Types().That().HaveFullNameContaining("DbContext"))
            .AndShould().NotDependOnAny(Types().That().HaveFullNameContaining("Ardalis.Specification.IRepositoryBase"))
            .Because("application handlers must use SharedKernel repository + unit-of-work abstractions")
            .Check(BasketArchitecture);

    [Fact]
    public void BasketEndpoints_ShouldDeriveFromAuthenticatedEndpoint() =>
        EndpointRules.EndpointsShouldDeriveFromAuthenticatedEndpoint(HostAssembly);

    [Fact]
    public void BasketService_ShouldFollowSharedArchitectureRules() =>
        SharedArchitectureRules.AssertAll(BasketArchitecture, ApplicationAssembly);
}
```

- [ ] **Step 3: Run the architecture tests**

Run: `dotnet test tests/architecture/Basket.Architecture.UnitTests/Basket.Architecture.UnitTests.csproj`
Expected: PASS (all rules green — this proves handlers avoid `DbContext`/`IRepositoryBase` and endpoints are correctly based).

- [ ] **Step 4: Commit**

```bash
git add tests/architecture/Basket.Architecture.UnitTests Teck.Platform.slnx
git commit -m "test(basket): add architecture boundary tests"
```

---

### Task 16: Integration test — checkout publishes event & creates order

**Files:**
- Create: `tests/integration/Basket.IntegrationTests/Basket.IntegrationTests.csproj`
- Create: `tests/integration/Basket.IntegrationTests/BasketCheckoutTests.cs`
- Modify: `Teck.Platform.slnx`

**Interfaces:**
- Consumes: `Basket.Host` (`Program`), the shared integration-test harness `Teck.Platform.IntegrationTests.Shared`.

> **Read `tests/integration/Order.IntegrationTests/` first** — it is the authoritative harness (Testcontainers Postgres + WolverineFx + WebApplicationFactory over `Program`). Mirror its csproj references, base fixture, and tenant-header setup. The test below is the target behavior; adapt its plumbing to match the Order harness exactly (fixture base class, DI overrides, `X-TenantId` header helper).

- [ ] **Step 1: Create the csproj** mirroring `tests/integration/Order.IntegrationTests/Order.IntegrationTests.csproj` (swap `Order`→`Basket`, reference `Basket.Host` and `Teck.Platform.IntegrationTests.Shared`). Register in `Teck.Platform.slnx`.

- [ ] **Step 2: Write the integration test** `BasketCheckoutTests.cs` — anonymous add → checkout publishes `BasketCheckedOutIntegrationEvent`. Adapt the fixture to the Order harness.

```csharp
using System.Net;
using System.Net.Http.Json;
using Baskets.Application.Baskets.Responses;
using Xunit;

namespace Baskets.IntegrationTests;

public sealed class BasketCheckoutTests // : IClassFixture<BasketApiFixture>  ← mirror Order harness fixture
{
    // NOTE: constructor + fixture injection mirror Order.IntegrationTests. Replace `client` acquisition
    // with the harness-provided authenticated HttpClient (with X-TenantId + customer_id claim).

    [Fact(Skip = "Enable once the fixture is wired to the Order integration harness")]
    public async Task Checkout_AfterAddingItem_ReturnsCreatedAndClearsActiveBasket()
    {
        HttpClient client = null!; // provided by fixture

        var current = await client.GetFromJsonAsync<BasketDto>("/baskets/current");
        Assert.NotNull(current);

        var afterAdd = await client.PostAsJsonAsync("/baskets/items",
            new { BasketId = current!.Id, ProductId = Guid.NewGuid(), ProductName = "Widget", UnitPrice = 10m, Quantity = 2 });
        afterAdd.EnsureSuccessStatusCode();

        var checkout = await client.PostAsJsonAsync("/baskets/checkout", new { BasketId = current.Id });

        Assert.Equal(HttpStatusCode.Created, checkout.StatusCode);
    }
}
```

> The test is intentionally `Skip`-gated until the fixture is wired, so the suite stays green. The executing agent's job in this task is to (a) copy the Order harness fixture, (b) remove the `Skip`, (c) make it pass against real Postgres, and (d) assert the basket transitions to `CheckedOut`. If wiring the full RabbitMQ round-trip to Order is too heavy for one task, assert the basket state + that the endpoint returns 201; a separate cross-service integration test can assert order creation.

- [ ] **Step 3: Run**

Run: `dotnet test tests/integration/Basket.IntegrationTests/Basket.IntegrationTests.csproj`
Expected: PASS (skipped test reported as skipped, project builds and green) — then, after un-skipping, PASS against Testcontainers.

- [ ] **Step 4: Commit**

```bash
git add tests/integration/Basket.IntegrationTests Teck.Platform.slnx
git commit -m "test(basket): add checkout integration test scaffold"
```

---

### Task 17: Full affected build/test gate + register in Aspire (optional wiring)

**Files:**
- Modify (optional): `src/aspire/Teck.AppHost/*` to add the `basket` service to local orchestration (mirror how `order` is registered).

- [ ] **Step 1: Run the same checks CI runs**

Run: `nx affected -t build test lint typecheck`
Expected: all green across affected projects (basket + order + SharedKernel.Events).

- [ ] **Step 2: (Optional) Register basket in Aspire AppHost** — mirror the `order` registration in `src/aspire/Teck.AppHost/AppHost.cs` (or equivalent) so `aspire run` boots basket with its Postgres database. Read how `order` is added and replicate for `basket`. Build the AppHost to confirm.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "chore(basket): wire basket into Aspire local orchestration"
```

---

## Self-Review

**Spec coverage:**
- Domain (`Basket`, `BasketItem`, `BasketStatus`, `BasketPricingService`, `BasketCheckedOut`) → Tasks 2–4. ✅
- Anonymous + customer identity + merge-on-login → `IBasketIdentityAccessor` (Task 9), `BasketIdentityAccessor` (Task 12), `MergeBasket` (Task 11), `AssignToCustomer` (Task 4). ✅
- Three-context CQRS + repositories + UoW → Task 5. ✅
- DTOs + Mapperly → Task 6; Specs → Task 7. ✅
- Shared `BasketCheckedOutIntegrationEvent` in `SharedKernel.Events` → Task 8. ✅
- All use cases (get-or-create, add, update, remove, clear, merge, checkout) → Tasks 9–11. ✅
- Host endpoints (anonymous cart ops; authenticated merge/checkout) + Program + config → Task 12. ✅
- Migration → Task 13. ✅
- Order-side consumer (close the loop) → Task 14. ✅
- Tests: unit (Tasks 2–11), architecture (Task 15), integration (Task 16); CI gate (Task 17). ✅
- Deferred items (ProductPriceChanged/OrderPlaced consumers, guest checkout) correctly excluded — the Order consumer explicitly no-ops on null customer, matching the "checkout requires auth" decision. ✅

**Placeholder scan:** The only intentionally-deferred code is the integration-test fixture (Task 16), which is `Skip`-gated with explicit wiring instructions and a directive to mirror the existing Order harness — not a silent TODO. Endpoint Task 4/12 references three sibling endpoints described with a full concrete template + exact routes/records; acceptable since they are mechanical repeats of the shown template.

**Type consistency:** `BasketDto` shape (with `AnonymousToken`) is consistent across mapper (Task 6), handlers (Tasks 9–11), and endpoints (Task 12). `BasketCheckedOutIntegrationEvent`/`BasketCheckedOutLine` property names match between producer (Task 11) and consumer (Task 14). Handler names all end in `Handler`/`Consumer` (satisfies the arch test in Task 15). Command names match between Application and Host endpoints.

**Known verification points flagged inline for the executor** (not placeholders — explicit "confirm against reference" notes): exact tenant interface (`ITenantInfo.Id`), the customer claim name, the EF `--output-dir` path for migrations, the InMemory context-construction pattern, and ServiceScan's handling of the identity accessor. Each names the exact reference file to check.
