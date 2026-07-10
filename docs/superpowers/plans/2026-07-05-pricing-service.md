# Pricing Service Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the `pricing` commerce microservice — the authority for product **list prices** with commercetools-style scoped price lists, min-quantity tiers, and multi-currency **FX conversion** — resolving *"what is the list price for product X in context C?"* and emitting `PriceChanged` for future consumers.

**Architecture:** Clean architecture (`Pricing.Domain → Pricing.Application → Pricing.Host`), mirroring the complete `order`/`basket` reference services exactly. Three-context CQRS (abstract base + write leaf in Application, NoTracking read leaf in Host). Handlers are static WolverineFx methods depending only on `IGenericReadRepository`/`IGenericWriteRepository`/`IUnitOfWork`. `PriceList` is the write aggregate; `Price` is a first-class, indexed entity for the read hot path. Cross-service event contract lives in `SharedKernel.Events`.

**Tech Stack:** .NET 10, EF Core (Npgsql), FastEndpoints, WolverineFx, Ardalis.Specification + SmartEnum, Riok.Mapperly, Finbuckle multi-tenancy, ErrorOr, FluentValidation, xunit.v3, NSubstitute, ArchUnitNET, Testcontainers.

**Spec:** `docs/superpowers/specs/2026-07-05-pricing-service-design.md`
**Reference services (mirror them):** `src/services/commerce/basket/` and `src/services/commerce/order/`
**Work-package brief:** `docs/superpowers/plans/services/pricing.md` · **Coordination:** `docs/superpowers/plans/services/COORDINATION.md`

## Prerequisites (before Task 1)

- **Work in an isolated worktree** (mandatory per COORDINATION — one worktree per service):
  ```bash
  git worktree add .claude/worktrees/pricing-service -b worktree-pricing-service main
  ```
  Fork from current `main` (already has the basket `ITenantInfo` fix + the shared-file baseline). All work happens in that worktree. Use the `superpowers:using-git-worktrees` skill if available.

## Global Constraints

- **Namespaces use the PLURAL convention**, matching `basket`/`order`: project files are `Pricing.Domain.csproj` / `Pricing.Application.csproj` / `Pricing.Host.csproj`, but their `RootNamespace` is `Pricing.Domain` / `Pricing.Application` / `Pricing.Host`. **Note:** the service name `pricing` is already a plural-safe noun, so folder = `pricing`, csproj = `Pricing.*`, namespace root = `Pricing.*`, and the Application capability folder is `Pricing/` (unlike basket where the folder was `Baskets/`). Keep it consistent: capability namespace is `Pricing.Application.Pricing.*`.
- **`TreatWarningsAsErrors=true` + analyzers-as-errors.** The root `.editorconfig` enforces StyleCop: usings ordered (System first), file-scoped namespaces, one type per file, file name = type name, member ordering, and **XML docs on every public type/member** (`<summary>` + `<param>`/`<returns>`/`<typeparam>`). Test projects are exempt from `SA*` but not formatting/IDE rules.
- **Repository/UoW rule (build-failing ArchUnit test):** Application types must NOT depend on any `DbContext` or `Ardalis.Specification.IRepositoryBase`. Handlers inject `IGenericReadRepository<T,Guid>` / `IGenericWriteRepository<T,Guid>` / `IUnitOfWork` only. `IUnitOfWork.SaveChangesAsync(ct)` is the single commit point, called exactly once per command.
- **Load-to-mutate requires `enableTracking: true`:** `repository.FirstOrDefaultAsync(spec, enableTracking: true, ct)` — default spec overloads are `AsNoTracking`, so without it mutations never persist.
- **`IMessageBus.PublishAsync(evt)` takes NO CancellationToken.** `InvokeAsync<T>(msg, ct)` does.
- **Every new project must be registered in `Teck.Platform.slnx`** (Nx `@nx/dotnet` infers projects from `.csproj`; no `project.json`).
- **`TId` is `System.Guid`** for all entities. Money amounts are `decimal`. `TenantId` is `string` (max length 64). Currency/country are ISO strings.
- **Query handlers return `ErrorOr<T>`** and use `IGenericReadRepository` only; command handlers return the DTO (or `ErrorOr<T>`) and may use the write repository. Queries are `IQuery<T>`; commands are `ICommand<T>`. Because pricing HAS a real `IQuery<>` (`ResolvePrice`), the architecture test calls `SharedArchitectureRules.AssertAll` directly (like `order`) — do NOT skip `QueriesShouldNotModifyState`.
- **Commit after every task** using conventional commits (`feat(pricing): ...`). Commits are GPG-signed automatically — never pass `--no-gpg-sign`; if signing fails, stop and surface it.
- **Domain-event → integration-event publishing is inline** (no EF→Wolverine bridge is wired platform-wide): command handlers capture the aggregate's `PriceChanged` domain events and, after `SaveChangesAsync`, publish a `PriceChangedIntegrationEvent` per event via `IMessageBus.PublishAsync`. Mirrors `basket`'s `CheckoutHandler`.

## File Structure

```
src/services/commerce/pricing/
  Directory.Build.props                         # chains to parent
  Pricing.Domain/
    Pricing.Domain.csproj
    ValueObjects/Money.cs                        # amount + ISO currency, non-negative
    ValueObjects/PriceScope.cs                   # currency(req) + country?/customerGroup?/channel? + matching
    ValueObjects/PriceTier.cs                    # (MinQuantity, Money Amount)
    ValueObjects/PriceListStatus.cs              # SmartEnum Draft/Active/Archived
    ValueObjects/PriceChangeType.cs              # enum Upserted/Removed
    Entities/Price.cs                            # first-class entity, FK to PriceList, (Tenant,Product) index
    Entities/PriceList.cs                        # aggregate root; owns scope+validity; mutates prices
    Entities/ExchangeRate.cs                     # aggregate root; per-tenant from→to rate
    Services/CurrencyConverter.cs                # Money × rate → rounded Money
    Services/PriceResolutionService.cs           # most-specific + native-preferred + tier selection
    Services/PriceResolutionContext.cs           # resolution input
    Services/ResolvedSelection.cs                # resolution output (Price + unit Money)
    DomainEvents/PriceChanged.cs
  Pricing.Application/
    Pricing.Application.csproj
    Database/PricingDbContextBase.cs
    Database/PricingDbContext.cs
    Database/Configurations/PriceListConfiguration.cs
    Database/Configurations/PriceConfiguration.cs
    Database/Configurations/ExchangeRateConfiguration.cs
    Pricing/PricingOptions.cs
    Pricing/IExchangeRateProvider.cs
    Pricing/PricingEventPublisher.cs             # inline PriceChanged→integration publish helper
    Pricing/Features/CreatePriceList/V1/...
    Pricing/Features/UpdatePriceList/V1/...
    Pricing/Features/ActivatePriceList/V1/...
    Pricing/Features/ArchivePriceList/V1/...
    Pricing/Features/AddOrUpdatePrice/V1/...
    Pricing/Features/RemovePrice/V1/...
    Pricing/Features/SetExchangeRate/V1/...
    Pricing/Features/RemoveExchangeRate/V1/...
    Pricing/Features/ResolvePrice/V1/...
    Pricing/Features/GetPriceList/V1/...
    Pricing/Features/ListPriceLists/V1/...
    Pricing/ReadModels/PricesByProductSpec.cs
    Pricing/ReadModels/ActivePriceListsSpec.cs
    Pricing/ReadModels/PriceListByIdSpec.cs
    Pricing/ReadModels/ExchangeRateByPairSpec.cs
    Pricing/Mapping/PriceListMapper.cs
    Pricing/Mapping/ExchangeRateMapper.cs
    Pricing/Responses/PriceListDto.cs
    Pricing/Responses/PriceDto.cs
    Pricing/Responses/PriceTierDto.cs
    Pricing/Responses/ResolvedPriceDto.cs
    Pricing/Responses/ExchangeRateDto.cs
  Pricing.Host/
    Pricing.Host.csproj
    Program.cs
    Program.Public.cs
    Database/PricingReadDbContext.cs
    Database/PricingReadRepository.cs
    Database/PricingWriteRepository.cs
    Database/PricingPersistenceExtensions.cs
    Database/PricingDbContextDesignTimeFactory.cs
    Database/Migrations/*InitialPricing*.cs
    Infrastructure/ExchangeRateProviderStub.cs
    Endpoints/Pricing/*.cs                        # endpoint + Request + Validator per use case
tests/
  unit/Pricing.UnitTests/
  integration/Pricing.IntegrationTests/
  architecture/Pricing.Architecture.UnitTests/
```

Shared-file touchpoints (additive): `Teck.Platform.slnx`, `src/aspire/Teck.AppHost/AppHost.cs` + `Teck.AppHost.csproj`, `src/shared/SharedKernel.Events/PriceChangedIntegrationEvent.cs` (new file). No `nx.json` change (commerce group exists).

---

### Task 1: Scaffold the three projects and register them

**Files:**
- Create: `src/services/commerce/pricing/Pricing.Domain/Pricing.Domain.csproj`
- Create: `src/services/commerce/pricing/Pricing.Application/Pricing.Application.csproj`
- Create: `src/services/commerce/pricing/Pricing.Host/Pricing.Host.csproj`
- Create: `src/services/commerce/pricing/Directory.Build.props`
- Create: `src/services/commerce/pricing/Pricing.Host/Program.cs` (temporary minimal, replaced in Task 17)
- Modify: `Teck.Platform.slnx`

**Interfaces:**
- Produces: three buildable projects with namespaces `Pricing.Domain`, `Pricing.Application`, `Pricing.Host`.

- [ ] **Step 1: Create `Pricing.Domain/Pricing.Domain.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>Pricing.Domain</RootNamespace>
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

- [ ] **Step 2: Create `Pricing.Application/Pricing.Application.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>Pricing.Application</RootNamespace>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Pricing.Domain\Pricing.Domain.csproj" />
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

- [ ] **Step 3: Create `Pricing.Host/Pricing.Host.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>Pricing.Host</RootNamespace>
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
    <ProjectReference Include="..\Pricing.Application\Pricing.Application.csproj" />
    <ProjectReference Include="..\Pricing.Domain\Pricing.Domain.csproj" />
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

- [ ] **Step 4: Create `Pricing.Host/Program.cs`** (temporary — replaced in Task 17)

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
return await Task.FromResult(0);
```

- [ ] **Step 5: Create `src/services/commerce/pricing/Directory.Build.props`**

```xml
<Project>
  <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />
</Project>
```

- [ ] **Step 6: Register the three projects in `Teck.Platform.slnx`**

Add a folder block alongside the existing `basket` block (mirror lines 28–31):

```xml
  <Folder Name="/src/services/commerce/pricing/">
    <Project Path="src/services/commerce/pricing/Pricing.Application/Pricing.Application.csproj" />
    <Project Path="src/services/commerce/pricing/Pricing.Domain/Pricing.Domain.csproj" />
    <Project Path="src/services/commerce/pricing/Pricing.Host/Pricing.Host.csproj" />
  </Folder>
```

- [ ] **Step 7: Verify the solution restores and builds**

Run: `dotnet build src/services/commerce/pricing/Pricing.Host/Pricing.Host.csproj`
Expected: build succeeds (empty host).

- [ ] **Step 8: Commit**

```bash
git add src/services/commerce/pricing Teck.Platform.slnx
git commit -m "feat(pricing): scaffold Domain/Application/Host projects"
```

---

### Task 2: Domain value objects — `Money`, `PriceScope`, `PriceTier`, `PriceListStatus`, `PriceChangeType`

**Files:**
- Create: `src/services/commerce/pricing/Pricing.Domain/ValueObjects/Money.cs`
- Create: `src/services/commerce/pricing/Pricing.Domain/ValueObjects/PriceScope.cs`
- Create: `src/services/commerce/pricing/Pricing.Domain/ValueObjects/PriceTier.cs`
- Create: `src/services/commerce/pricing/Pricing.Domain/ValueObjects/PriceListStatus.cs`
- Create: `src/services/commerce/pricing/Pricing.Domain/ValueObjects/PriceChangeType.cs`
- Test: `tests/unit/Pricing.UnitTests/ValueObjectTests.cs`

**Interfaces:**
- Produces:
  - `Money(decimal Amount, string Currency)` — `sealed class : ValueObject`, non-negative amount, non-blank currency.
  - `PriceScope(string Currency, string? Country, Guid? CustomerGroupId, Guid? ChannelId)` — `sealed class : ValueObject`; `bool IsCompatibleWith(string? country, Guid? customerGroupId, Guid? channelId)`; `int Specificity`.
  - `PriceTier(int MinQuantity, Money Amount)` — `sealed record`.
  - `PriceListStatus` — SmartEnum `Draft(1)`, `Active(2)`, `Archived(3)`.
  - `PriceChangeType` — enum `Upserted`, `Removed`.

- [ ] **Step 1: Write the failing test** `tests/unit/Pricing.UnitTests/ValueObjectTests.cs`

```csharp
using Pricing.Domain.ValueObjects;
using Xunit;

namespace Pricing.UnitTests;

public sealed class ValueObjectTests
{
    [Fact]
    public void Money_NegativeAmount_Throws() =>
        Assert.Throws<ArgumentException>(() => new Money(-1m, "USD"));

    [Fact]
    public void Money_BlankCurrency_Throws() =>
        Assert.Throws<ArgumentException>(() => new Money(1m, " "));

    [Fact]
    public void PriceScope_NullDimensions_AreWildcardsAndCompatibleWithAnything()
    {
        var scope = new PriceScope("USD", country: null, customerGroupId: null, channelId: null);

        Assert.True(scope.IsCompatibleWith("US", Guid.NewGuid(), Guid.NewGuid()));
        Assert.Equal(0, scope.Specificity);
    }

    [Fact]
    public void PriceScope_SetDimension_RequiresExactMatch()
    {
        var group = Guid.NewGuid();
        var scope = new PriceScope("USD", country: "US", customerGroupId: group, channelId: null);

        Assert.True(scope.IsCompatibleWith("US", group, Guid.NewGuid()));
        Assert.False(scope.IsCompatibleWith("DE", group, null));    // country mismatch
        Assert.False(scope.IsCompatibleWith("US", Guid.NewGuid(), null)); // group mismatch
        Assert.False(scope.IsCompatibleWith(null, group, null));    // request lacks a set dimension
        Assert.Equal(2, scope.Specificity);
    }

    [Fact]
    public void PriceListStatus_FromValue_RoundTrips() =>
        Assert.Equal(PriceListStatus.Active, PriceListStatus.FromValue(2));
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/unit/Pricing.UnitTests` (after adding the test project in Step 8)
Expected: FAIL — types do not exist yet. (If the test project is not yet created, create it first per Step 8, then return here.)

- [ ] **Step 3: Create `Money.cs`**

```csharp
using SharedKernel.Core.Domain;

namespace Pricing.Domain.ValueObjects;

/// <summary>An immutable monetary amount in a given ISO currency.</summary>
public sealed class Money : ValueObject
{
    /// <summary>Initializes a new instance of the <see cref="Money"/> class.</summary>
    /// <param name="amount">The non-negative amount.</param>
    /// <param name="currency">The ISO 4217 currency code.</param>
    public Money(decimal amount, string currency)
    {
        if (amount < 0)
        {
            throw new ArgumentException("Amount cannot be negative.", nameof(amount));
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException("Currency is required.", nameof(currency));
        }

        Amount = amount;
        Currency = currency;
    }

    /// <summary>Gets the amount.</summary>
    public decimal Amount { get; }

    /// <summary>Gets the ISO 4217 currency code.</summary>
    public string Currency { get; }

    /// <inheritdoc/>
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }
}
```

- [ ] **Step 4: Create `PriceScope.cs`**

```csharp
using SharedKernel.Core.Domain;

namespace Pricing.Domain.ValueObjects;

/// <summary>
/// The scope a price list applies to. A null dimension is a wildcard that matches any request value.
/// </summary>
public sealed class PriceScope : ValueObject
{
    /// <summary>Initializes a new instance of the <see cref="PriceScope"/> class.</summary>
    /// <param name="currency">The ISO 4217 currency (required — a list is single-currency).</param>
    /// <param name="country">The ISO 3166-1 alpha-2 country, or null for any.</param>
    /// <param name="customerGroupId">The customer group, or null for any.</param>
    /// <param name="channelId">The sales channel, or null for any.</param>
    public PriceScope(string currency, string? country, Guid? customerGroupId, Guid? channelId)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException("Currency is required.", nameof(currency));
        }

        Currency = currency;
        Country = country;
        CustomerGroupId = customerGroupId;
        ChannelId = channelId;
    }

    /// <summary>Gets the ISO 4217 currency.</summary>
    public string Currency { get; }

    /// <summary>Gets the ISO 3166-1 alpha-2 country, or null for any.</summary>
    public string? Country { get; }

    /// <summary>Gets the customer group, or null for any.</summary>
    public Guid? CustomerGroupId { get; }

    /// <summary>Gets the sales channel, or null for any.</summary>
    public Guid? ChannelId { get; }

    /// <summary>Gets the number of set (non-wildcard) non-currency dimensions.</summary>
    public int Specificity =>
        (Country is null ? 0 : 1) + (CustomerGroupId is null ? 0 : 1) + (ChannelId is null ? 0 : 1);

    /// <summary>Determines whether this scope is compatible with a request context.</summary>
    /// <param name="country">The request country.</param>
    /// <param name="customerGroupId">The request customer group.</param>
    /// <param name="channelId">The request channel.</param>
    /// <returns><c>true</c> if every set dimension equals the corresponding request value.</returns>
    public bool IsCompatibleWith(string? country, Guid? customerGroupId, Guid? channelId)
    {
        if (Country is not null && !string.Equals(Country, country, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (CustomerGroupId is not null && CustomerGroupId != customerGroupId)
        {
            return false;
        }

        return ChannelId is null || ChannelId == channelId;
    }

    /// <inheritdoc/>
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Currency;
        yield return Country;
        yield return CustomerGroupId;
        yield return ChannelId;
    }
}
```

- [ ] **Step 5: Create `PriceTier.cs`**

```csharp
namespace Pricing.Domain.ValueObjects;

/// <summary>A quantity tier within a price: the unit amount that applies from a minimum quantity.</summary>
/// <param name="MinQuantity">The minimum quantity (>= 1) at which this tier's amount applies.</param>
/// <param name="Amount">The unit amount for this tier.</param>
public sealed record PriceTier(int MinQuantity, Money Amount);
```

- [ ] **Step 6: Create `PriceListStatus.cs`**

```csharp
using Ardalis.SmartEnum;

namespace Pricing.Domain.ValueObjects;

/// <summary>Represents the lifecycle status of a price list.</summary>
public sealed class PriceListStatus : SmartEnum<PriceListStatus>
{
    /// <summary>The list is being edited and is not yet resolvable.</summary>
    public static readonly PriceListStatus Draft = new(nameof(Draft), 1);

    /// <summary>The list is active and participates in price resolution.</summary>
    public static readonly PriceListStatus Active = new(nameof(Active), 2);

    /// <summary>The list is archived and no longer resolvable.</summary>
    public static readonly PriceListStatus Archived = new(nameof(Archived), 3);

    private PriceListStatus(string name, int value)
        : base(name, value)
    {
    }
}
```

- [ ] **Step 7: Create `PriceChangeType.cs`**

```csharp
namespace Pricing.Domain.ValueObjects;

/// <summary>The kind of change described by a price-changed event.</summary>
public enum PriceChangeType
{
    /// <summary>A price was created or updated and is (or becomes) effective.</summary>
    Upserted,

    /// <summary>A price was removed or retracted and is no longer effective.</summary>
    Removed,
}
```

- [ ] **Step 8: Create the unit-test project** `tests/unit/Pricing.UnitTests/Pricing.UnitTests.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>Pricing.UnitTests</RootNamespace>
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
    <ProjectReference Include="..\..\..\src\services\commerce\pricing\Pricing.Domain\Pricing.Domain.csproj" />
    <ProjectReference Include="..\..\..\src\services\commerce\pricing\Pricing.Application\Pricing.Application.csproj" />
  </ItemGroup>
</Project>
```

Register it in `Teck.Platform.slnx` (mirror the basket unit-test entry, in the tests folder block):

```xml
    <Project Path="tests/unit/Pricing.UnitTests/Pricing.UnitTests.csproj" />
```

- [ ] **Step 9: Run tests to verify they pass**

Run: `dotnet test tests/unit/Pricing.UnitTests`
Expected: PASS (5 tests).

- [ ] **Step 10: Commit**

```bash
git add src/services/commerce/pricing/Pricing.Domain tests/unit/Pricing.UnitTests Teck.Platform.slnx
git commit -m "feat(pricing): domain value objects and status enums"
```

---

### Task 3: `CurrencyConverter` domain service

**Files:**
- Create: `src/services/commerce/pricing/Pricing.Domain/Services/CurrencyConverter.cs`
- Test: `tests/unit/Pricing.UnitTests/CurrencyConverterTests.cs`

**Interfaces:**
- Consumes: `Money` (Task 2).
- Produces: `static Money CurrencyConverter.Convert(Money source, string targetCurrency, decimal rate, int decimals, MidpointRounding mode)` — multiplies by `rate`, rounds to `decimals` using `mode`, returns `Money` in `targetCurrency`. Throws `ArgumentOutOfRangeException` if `rate <= 0`.

- [ ] **Step 1: Write the failing test** `tests/unit/Pricing.UnitTests/CurrencyConverterTests.cs`

```csharp
using Pricing.Domain.Services;
using Pricing.Domain.ValueObjects;
using Xunit;

namespace Pricing.UnitTests;

public sealed class CurrencyConverterTests
{
    [Fact]
    public void Convert_MultipliesByRate_AndSetsTargetCurrency()
    {
        var result = CurrencyConverter.Convert(new Money(10m, "USD"), "EUR", 0.9m, 2, MidpointRounding.ToEven);

        Assert.Equal(9.00m, result.Amount);
        Assert.Equal("EUR", result.Currency);
    }

    [Fact]
    public void Convert_RoundsToBankersRounding()
    {
        // 10.125 * 1 = 10.125 -> ToEven at 2dp -> 10.12
        var result = CurrencyConverter.Convert(new Money(10.125m, "USD"), "USD", 1m, 2, MidpointRounding.ToEven);

        Assert.Equal(10.12m, result.Amount);
    }

    [Fact]
    public void Convert_NonPositiveRate_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CurrencyConverter.Convert(new Money(10m, "USD"), "EUR", 0m, 2, MidpointRounding.ToEven));
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/unit/Pricing.UnitTests --filter CurrencyConverterTests`
Expected: FAIL — `CurrencyConverter` does not exist.

- [ ] **Step 3: Create `CurrencyConverter.cs`**

```csharp
using Pricing.Domain.ValueObjects;

namespace Pricing.Domain.Services;

/// <summary>Converts monetary amounts between currencies using an exchange rate.</summary>
public static class CurrencyConverter
{
    /// <summary>Converts a source amount into a target currency at the given rate.</summary>
    /// <param name="source">The source money.</param>
    /// <param name="targetCurrency">The ISO 4217 target currency.</param>
    /// <param name="rate">The multiplicative rate (source → target); must be positive.</param>
    /// <param name="decimals">The number of decimal places to round to.</param>
    /// <param name="mode">The midpoint rounding mode.</param>
    /// <returns>The converted amount as <see cref="Money"/> in <paramref name="targetCurrency"/>.</returns>
    public static Money Convert(Money source, string targetCurrency, decimal rate, int decimals, MidpointRounding mode)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (rate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rate), "Exchange rate must be positive.");
        }

        decimal converted = Math.Round(source.Amount * rate, decimals, mode);
        return new Money(converted, targetCurrency);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/unit/Pricing.UnitTests --filter CurrencyConverterTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/services/commerce/pricing/Pricing.Domain/Services/CurrencyConverter.cs tests/unit/Pricing.UnitTests/CurrencyConverterTests.cs
git commit -m "feat(pricing): currency converter domain service"
```

---

### Task 4: `PriceChanged` event + `Price` entity + `PriceList` aggregate

`Price` and `PriceList` reference each other and only compile together, so they are one task (files that change together live together).

**Files:**
- Create: `src/services/commerce/pricing/Pricing.Domain/DomainEvents/PriceChanged.cs`
- Create: `src/services/commerce/pricing/Pricing.Domain/Entities/Price.cs`
- Create: `src/services/commerce/pricing/Pricing.Domain/Entities/PriceList.cs`
- Test: `tests/unit/Pricing.UnitTests/PriceTests.cs`
- Test: `tests/unit/Pricing.UnitTests/PriceListTests.cs`

**Interfaces:**
- Consumes: `Money`, `PriceTier`, `PriceChangeType` (Task 2).
- Produces:
  - `PriceChanged(Guid productId, Guid priceListId, string tenantId, decimal amount, string currency, DateTimeOffset effectiveFrom, PriceChangeType changeType)` — `sealed class : DomainEvent` with matching read-only properties.
  - `Price` — `sealed class : BaseEntity, ITenantScoped`. Members: `Guid ProductId`, `Money Amount`, `IReadOnlyList<PriceTier> Tiers`, `Guid PriceListId`, `PriceList PriceList` (navigation), `string TenantId`. `internal static Price Create(Guid productId, Money amount, IReadOnlyList<PriceTier> tiers, string tenantId)`; `internal void Update(Money amount, IReadOnlyList<PriceTier> tiers)`; `Money UnitAmountFor(int quantity)` (highest tier with `MinQuantity <= quantity`, else base `Amount`). Validates tiers (ascending unique `MinQuantity >= 1`, each amount currency == `Amount.Currency`).

- [ ] **Step 1: Write the failing test** `tests/unit/Pricing.UnitTests/PriceTests.cs`

```csharp
using Pricing.Domain.Entities;
using Pricing.Domain.ValueObjects;
using Xunit;

namespace Pricing.UnitTests;

public sealed class PriceTests
{
    private static Price NewPrice(params PriceTier[] tiers) =>
        Price.Create(Guid.NewGuid(), new Money(10m, "USD"), tiers, "tenant-1");

    [Fact]
    public void UnitAmountFor_NoTiers_ReturnsBaseAmount()
    {
        var price = NewPrice();

        Assert.Equal(10m, price.UnitAmountFor(5).Amount);
    }

    [Fact]
    public void UnitAmountFor_PicksHighestApplicableTier()
    {
        var price = NewPrice(new PriceTier(1, new Money(10m, "USD")), new PriceTier(10, new Money(8m, "USD")));

        Assert.Equal(10m, price.UnitAmountFor(9).Amount);
        Assert.Equal(8m, price.UnitAmountFor(10).Amount);
        Assert.Equal(8m, price.UnitAmountFor(100).Amount);
    }

    [Fact]
    public void Create_TierWithForeignCurrency_Throws() =>
        Assert.Throws<ArgumentException>(() => NewPrice(new PriceTier(1, new Money(10m, "EUR"))));

    [Fact]
    public void Create_NonAscendingTiers_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            NewPrice(new PriceTier(10, new Money(8m, "USD")), new PriceTier(5, new Money(9m, "USD"))));

    [Fact]
    public void Create_TierBelowOne_Throws() =>
        Assert.Throws<ArgumentException>(() => NewPrice(new PriceTier(0, new Money(10m, "USD"))));
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/unit/Pricing.UnitTests --filter PriceTests`
Expected: FAIL — `Price` does not exist.

- [ ] **Step 3: Create `PriceChanged.cs`**

```csharp
using Pricing.Domain.ValueObjects;
using SharedKernel.Core.Events;

namespace Pricing.Domain.DomainEvents;

/// <summary>Domain event raised when an effective price is created, updated, or removed.</summary>
public sealed class PriceChanged : DomainEvent
{
    /// <summary>Initializes a new instance of the <see cref="PriceChanged"/> class.</summary>
    /// <param name="productId">The product whose price changed.</param>
    /// <param name="priceListId">The owning price list.</param>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="amount">The amount involved in the change.</param>
    /// <param name="currency">The ISO currency of the amount.</param>
    /// <param name="effectiveFrom">When the change takes effect.</param>
    /// <param name="changeType">Whether the price was upserted or removed.</param>
    public PriceChanged(Guid productId, Guid priceListId, string tenantId, decimal amount, string currency, DateTimeOffset effectiveFrom, PriceChangeType changeType)
    {
        ProductId = productId;
        PriceListId = priceListId;
        TenantId = tenantId;
        Amount = amount;
        Currency = currency;
        EffectiveFrom = effectiveFrom;
        ChangeType = changeType;
    }

    /// <summary>Gets the product whose price changed.</summary>
    public Guid ProductId { get; }

    /// <summary>Gets the owning price list.</summary>
    public Guid PriceListId { get; }

    /// <summary>Gets the owning tenant.</summary>
    public string TenantId { get; }

    /// <summary>Gets the amount involved in the change.</summary>
    public decimal Amount { get; }

    /// <summary>Gets the ISO currency of the amount.</summary>
    public string Currency { get; }

    /// <summary>Gets when the change takes effect.</summary>
    public DateTimeOffset EffectiveFrom { get; }

    /// <summary>Gets whether the price was upserted or removed.</summary>
    public PriceChangeType ChangeType { get; }
}
```

- [ ] **Step 4: Create `Price.cs`**

```csharp
using Pricing.Domain.ValueObjects;
using SharedKernel.Core.Domain;

namespace Pricing.Domain.Entities;

/// <summary>
/// A product's price within a <see cref="PriceList"/>. A first-class, tenant-scoped entity indexed
/// by (TenantId, ProductId) for the resolution hot path; mutated only through the owning list.
/// </summary>
public sealed class Price : BaseEntity, ITenantScoped
{
    private readonly List<PriceTier> _tiers = [];

    private Price()
    {
    }

    /// <summary>Gets the product identifier.</summary>
    public Guid ProductId { get; private set; }

    /// <summary>Gets the base unit amount (used when no tier applies).</summary>
    public Money Amount { get; private set; } = null!;

    /// <summary>Gets the quantity tiers, ascending by minimum quantity.</summary>
    public IReadOnlyList<PriceTier> Tiers => _tiers;

    /// <summary>Gets the owning price list identifier.</summary>
    public Guid PriceListId { get; private set; }

    /// <summary>Gets the owning price list navigation.</summary>
    public PriceList PriceList { get; private set; } = null!;

    /// <inheritdoc/>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Creates a price for a product.</summary>
    /// <param name="productId">The product identifier.</param>
    /// <param name="amount">The base unit amount.</param>
    /// <param name="tiers">The quantity tiers (may be empty).</param>
    /// <param name="tenantId">The owning tenant.</param>
    /// <returns>The new price.</returns>
    internal static Price Create(Guid productId, Money amount, IReadOnlyList<PriceTier> tiers, string tenantId)
    {
        var price = new Price { ProductId = productId, TenantId = tenantId };
        price.Update(amount, tiers);
        return price;
    }

    /// <summary>Replaces the amount and tiers, validating tier ordering and currency.</summary>
    /// <param name="amount">The new base unit amount.</param>
    /// <param name="tiers">The new quantity tiers.</param>
    internal void Update(Money amount, IReadOnlyList<PriceTier> tiers)
    {
        ArgumentNullException.ThrowIfNull(amount);
        ArgumentNullException.ThrowIfNull(tiers);

        int previousMin = 0;
        foreach (PriceTier tier in tiers)
        {
            if (tier.MinQuantity < 1)
            {
                throw new ArgumentException("Tier minimum quantity must be at least 1.", nameof(tiers));
            }

            if (tier.MinQuantity <= previousMin)
            {
                throw new ArgumentException("Tiers must have strictly ascending, unique minimum quantities.", nameof(tiers));
            }

            if (!string.Equals(tier.Amount.Currency, amount.Currency, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Tier currency must match the price currency.", nameof(tiers));
            }

            previousMin = tier.MinQuantity;
        }

        Amount = amount;
        _tiers.Clear();
        _tiers.AddRange(tiers);
    }

    /// <summary>Returns the unit amount for a quantity (highest applicable tier, else base amount).</summary>
    /// <param name="quantity">The requested quantity.</param>
    /// <returns>The applicable unit amount.</returns>
    public Money UnitAmountFor(int quantity)
    {
        Money best = Amount;
        int bestMin = 0;
        foreach (PriceTier tier in _tiers)
        {
            if (tier.MinQuantity <= quantity && tier.MinQuantity >= bestMin)
            {
                best = tier.Amount;
                bestMin = tier.MinQuantity;
            }
        }

        return best;
    }
}
```

- [ ] **Step 5: Write the failing test** `tests/unit/Pricing.UnitTests/PriceListTests.cs`

```csharp
using Pricing.Domain.Entities;
using Pricing.Domain.DomainEvents;
using Pricing.Domain.ValueObjects;
using Xunit;

namespace Pricing.UnitTests;

public sealed class PriceListTests
{
    private static readonly Guid Product = Guid.NewGuid();

    private static PriceList Draft() =>
        PriceList.Create("Default", new PriceScope("USD", null, null, null), validFrom: null, validUntil: null, "tenant-1");

    [Fact]
    public void Create_StartsDraft_WithNoPrices()
    {
        var list = Draft();

        Assert.Equal(PriceListStatus.Draft, list.Status);
        Assert.Empty(list.Prices);
    }

    [Fact]
    public void AddOrUpdatePrice_OnDraft_DoesNotRaisePriceChanged()
    {
        var list = Draft();

        list.AddOrUpdatePrice(Product, new Money(10m, "USD"), []);

        Assert.Single(list.Prices);
        Assert.Empty(list.DomainEvents.OfType<PriceChanged>());
    }

    [Fact]
    public void AddOrUpdatePrice_ForeignCurrency_Throws()
    {
        var list = Draft();

        Assert.Throws<ArgumentException>(() => list.AddOrUpdatePrice(Product, new Money(10m, "EUR"), []));
    }

    [Fact]
    public void AddOrUpdatePrice_Twice_UpdatesInPlace()
    {
        var list = Draft();
        list.AddOrUpdatePrice(Product, new Money(10m, "USD"), []);
        list.AddOrUpdatePrice(Product, new Money(12m, "USD"), []);

        Price price = Assert.Single(list.Prices);
        Assert.Equal(12m, price.Amount.Amount);
    }

    [Fact]
    public void Activate_RaisesUpsertedPerPrice()
    {
        var list = Draft();
        list.AddOrUpdatePrice(Product, new Money(10m, "USD"), []);

        list.Activate();

        Assert.Equal(PriceListStatus.Active, list.Status);
        PriceChanged evt = Assert.Single(list.DomainEvents.OfType<PriceChanged>());
        Assert.Equal(PriceChangeType.Upserted, evt.ChangeType);
        Assert.Equal(Product, evt.ProductId);
    }

    [Fact]
    public void AddOrUpdatePrice_OnActive_RaisesUpserted()
    {
        var list = Draft();
        list.Activate();

        list.AddOrUpdatePrice(Product, new Money(10m, "USD"), []);

        Assert.Contains(list.DomainEvents.OfType<PriceChanged>(), e => e.ChangeType == PriceChangeType.Upserted);
    }

    [Fact]
    public void Archive_RaisesRemovedPerPrice()
    {
        var list = Draft();
        list.AddOrUpdatePrice(Product, new Money(10m, "USD"), []);
        list.Activate();

        list.Archive();

        Assert.Equal(PriceListStatus.Archived, list.Status);
        Assert.Contains(list.DomainEvents.OfType<PriceChanged>(), e => e.ChangeType == PriceChangeType.Removed);
    }

    [Fact]
    public void RemovePrice_Missing_Throws()
    {
        var list = Draft();

        Assert.Throws<InvalidOperationException>(() => list.RemovePrice(Product));
    }

    [Fact]
    public void Create_InvalidValidityWindow_Throws()
    {
        var from = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentException>(() =>
            PriceList.Create("x", new PriceScope("USD", null, null, null), validFrom: from, validUntil: from.AddDays(-1), "tenant-1"));
    }
}
```

- [ ] **Step 6: Create `PriceList.cs`**

```csharp
using Pricing.Domain.DomainEvents;
using Pricing.Domain.ValueObjects;
using SharedKernel.Core.Domain;

namespace Pricing.Domain.Entities;

/// <summary>
/// A named, tenant-scoped set of product prices sharing one scope (currency + optional
/// country/customer-group/channel) and validity window. The write aggregate: prices are added,
/// updated, and removed only through this root, which raises <see cref="PriceChanged"/> for
/// effective (Active-list) changes.
/// </summary>
public sealed class PriceList : BaseEntity, IAggregateRoot, ITenantScoped
{
    private readonly List<Price> _prices = [];

    private PriceList()
    {
    }

    /// <summary>Gets the display name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Gets the optional description.</summary>
    public string? Description { get; private set; }

    /// <inheritdoc/>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets the lifecycle status.</summary>
    public PriceListStatus Status { get; private set; } = PriceListStatus.Draft;

    /// <summary>Gets the scope this list applies to.</summary>
    public PriceScope Scope { get; private set; } = null!;

    /// <summary>Gets the inclusive start of the validity window, or null for open-started.</summary>
    public DateTimeOffset? ValidFrom { get; private set; }

    /// <summary>Gets the exclusive end of the validity window, or null for open-ended.</summary>
    public DateTimeOffset? ValidUntil { get; private set; }

    /// <summary>Gets the prices contained in this list.</summary>
    public IReadOnlyCollection<Price> Prices => _prices;

    /// <summary>Determines whether the list's validity window contains a moment.</summary>
    /// <param name="at">The moment to test.</param>
    /// <returns><c>true</c> if within the window.</returns>
    public bool IsValidAt(DateTimeOffset at) =>
        (ValidFrom is null || at >= ValidFrom) && (ValidUntil is null || at < ValidUntil);

    /// <summary>Creates a new draft price list.</summary>
    /// <param name="name">The display name.</param>
    /// <param name="scope">The scope.</param>
    /// <param name="validFrom">The inclusive validity start, or null.</param>
    /// <param name="validUntil">The exclusive validity end, or null.</param>
    /// <param name="tenantId">The owning tenant.</param>
    /// <returns>The new draft list.</returns>
    public static PriceList Create(string name, PriceScope scope, DateTimeOffset? validFrom, DateTimeOffset? validUntil, string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(scope);
        ValidateWindow(validFrom, validUntil);

        return new PriceList
        {
            Name = name,
            Scope = scope,
            ValidFrom = validFrom,
            ValidUntil = validUntil,
            TenantId = tenantId,
            Status = PriceListStatus.Draft,
        };
    }

    /// <summary>Updates the name and description.</summary>
    /// <param name="name">The new name.</param>
    /// <param name="description">The new description.</param>
    public void UpdateDetails(string name, string? description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Description = description;
    }

    /// <summary>Replaces the scope (currency change re-validates contained prices) and re-emits when active.</summary>
    /// <param name="scope">The new scope.</param>
    public void UpdateScope(PriceScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        foreach (Price price in _prices)
        {
            if (!string.Equals(price.Amount.Currency, scope.Currency, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Cannot change scope currency while prices in the old currency exist.");
            }
        }

        Scope = scope;
        RaiseForAllPrices(PriceChangeType.Upserted);
    }

    /// <summary>Updates the validity window and re-emits when active.</summary>
    /// <param name="validFrom">The new inclusive start, or null.</param>
    /// <param name="validUntil">The new exclusive end, or null.</param>
    public void UpdateValidity(DateTimeOffset? validFrom, DateTimeOffset? validUntil)
    {
        ValidateWindow(validFrom, validUntil);
        ValidFrom = validFrom;
        ValidUntil = validUntil;
        RaiseForAllPrices(PriceChangeType.Upserted);
    }

    /// <summary>Activates the list and emits <see cref="PriceChanged"/> (Upserted) for every price.</summary>
    public void Activate()
    {
        Status = PriceListStatus.Active;
        RaiseForAllPrices(PriceChangeType.Upserted);
    }

    /// <summary>Archives the list and emits <see cref="PriceChanged"/> (Removed) for every price.</summary>
    public void Archive()
    {
        Status = PriceListStatus.Archived;
        RaiseForAllPrices(PriceChangeType.Removed);
    }

    /// <summary>Adds or updates the price for a product; emits Upserted only when the list is active.</summary>
    /// <param name="productId">The product identifier.</param>
    /// <param name="amount">The base unit amount (must match the list currency).</param>
    /// <param name="tiers">The quantity tiers.</param>
    public void AddOrUpdatePrice(Guid productId, Money amount, IReadOnlyList<PriceTier> tiers)
    {
        ArgumentNullException.ThrowIfNull(amount);
        if (!string.Equals(amount.Currency, Scope.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Price currency must match the list scope currency.", nameof(amount));
        }

        Price? existing = _prices.Find(price => price.ProductId == productId);
        if (existing is null)
        {
            _prices.Add(Price.Create(productId, amount, tiers, TenantId));
        }
        else
        {
            existing.Update(amount, tiers);
        }

        if (Status == PriceListStatus.Active)
        {
            Raise(productId, amount, PriceChangeType.Upserted);
        }
    }

    /// <summary>Removes a product's price; emits Removed only when the list is active.</summary>
    /// <param name="productId">The product identifier.</param>
    public void RemovePrice(Guid productId)
    {
        Price existing = _prices.Find(price => price.ProductId == productId)
            ?? throw new InvalidOperationException($"Product '{productId}' has no price in list '{Id}'.");

        _prices.Remove(existing);

        if (Status == PriceListStatus.Active)
        {
            Raise(productId, existing.Amount, PriceChangeType.Removed);
        }
    }

    private static void ValidateWindow(DateTimeOffset? validFrom, DateTimeOffset? validUntil)
    {
        if (validFrom is not null && validUntil is not null && validUntil <= validFrom)
        {
            throw new ArgumentException("ValidUntil must be after ValidFrom.", nameof(validUntil));
        }
    }

    private void RaiseForAllPrices(PriceChangeType changeType)
    {
        if (Status != PriceListStatus.Active && changeType == PriceChangeType.Upserted)
        {
            return;
        }

        foreach (Price price in _prices)
        {
            Raise(price.ProductId, price.Amount, changeType);
        }
    }

    private void Raise(Guid productId, Money amount, PriceChangeType changeType) =>
        AddDomainEvent(new PriceChanged(
            productId,
            Id,
            TenantId,
            amount.Amount,
            amount.Currency,
            changeType == PriceChangeType.Upserted ? ValidFrom ?? DateTimeOffset.UtcNow : DateTimeOffset.UtcNow,
            changeType));
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/unit/Pricing.UnitTests --filter "PriceTests|PriceListTests"`
Expected: PASS (Price: 5, PriceList: 10).

- [ ] **Step 8: Commit**

```bash
git add src/services/commerce/pricing/Pricing.Domain tests/unit/Pricing.UnitTests/PriceTests.cs tests/unit/Pricing.UnitTests/PriceListTests.cs
git commit -m "feat(pricing): Price entity and PriceList aggregate with PriceChanged"
```

---

### Task 5: `ExchangeRate` aggregate

**Files:**
- Create: `src/services/commerce/pricing/Pricing.Domain/Entities/ExchangeRate.cs`
- Test: `tests/unit/Pricing.UnitTests/ExchangeRateTests.cs`

**Interfaces:**
- Produces: `ExchangeRate` — `sealed class : BaseEntity, IAggregateRoot, ITenantScoped`. Members: `string FromCurrency`, `string ToCurrency`, `decimal Rate`, `DateTimeOffset? ValidFrom`, `DateTimeOffset? ValidUntil`, `string TenantId`. `static ExchangeRate Create(string from, string to, decimal rate, DateTimeOffset? validFrom, DateTimeOffset? validUntil, string tenantId)`; `void UpdateRate(decimal rate)`; `void UpdateValidity(DateTimeOffset? from, DateTimeOffset? until)`; `bool IsValidAt(DateTimeOffset at)`. Guards: `rate > 0`, from ≠ to (case-insensitive), window ordering.

- [ ] **Step 1: Write the failing test** `tests/unit/Pricing.UnitTests/ExchangeRateTests.cs`

```csharp
using Pricing.Domain.Entities;
using Xunit;

namespace Pricing.UnitTests;

public sealed class ExchangeRateTests
{
    [Fact]
    public void Create_NonPositiveRate_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ExchangeRate.Create("USD", "EUR", 0m, null, null, "tenant-1"));

    [Fact]
    public void Create_SameCurrency_Throws() =>
        Assert.Throws<ArgumentException>(
            () => ExchangeRate.Create("USD", "usd", 1m, null, null, "tenant-1"));

    [Fact]
    public void IsValidAt_OpenWindow_AlwaysValid()
    {
        var rate = ExchangeRate.Create("USD", "EUR", 0.9m, null, null, "tenant-1");

        Assert.True(rate.IsValidAt(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void IsValidAt_OutsideWindow_False()
    {
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var until = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        var rate = ExchangeRate.Create("USD", "EUR", 0.9m, from, until, "tenant-1");

        Assert.False(rate.IsValidAt(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.True(rate.IsValidAt(new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero)));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/unit/Pricing.UnitTests --filter ExchangeRateTests`
Expected: FAIL — `ExchangeRate` does not exist.

- [ ] **Step 3: Create `ExchangeRate.cs`**

```csharp
using SharedKernel.Core.Domain;

namespace Pricing.Domain.Entities;

/// <summary>
/// A tenant-managed exchange rate from one currency to another. v1 keeps at most one rate per
/// (FromCurrency, ToCurrency) pair; the validity window is optional (null = always valid).
/// </summary>
public sealed class ExchangeRate : BaseEntity, IAggregateRoot, ITenantScoped
{
    private ExchangeRate()
    {
    }

    /// <summary>Gets the source ISO currency.</summary>
    public string FromCurrency { get; private set; } = string.Empty;

    /// <summary>Gets the target ISO currency.</summary>
    public string ToCurrency { get; private set; } = string.Empty;

    /// <summary>Gets the multiplicative rate (from → to).</summary>
    public decimal Rate { get; private set; }

    /// <summary>Gets the inclusive validity start, or null for open-started.</summary>
    public DateTimeOffset? ValidFrom { get; private set; }

    /// <summary>Gets the exclusive validity end, or null for open-ended.</summary>
    public DateTimeOffset? ValidUntil { get; private set; }

    /// <inheritdoc/>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Creates a new exchange rate.</summary>
    /// <param name="from">The source ISO currency.</param>
    /// <param name="to">The target ISO currency.</param>
    /// <param name="rate">The positive multiplicative rate.</param>
    /// <param name="validFrom">The inclusive validity start, or null.</param>
    /// <param name="validUntil">The exclusive validity end, or null.</param>
    /// <param name="tenantId">The owning tenant.</param>
    /// <returns>The new exchange rate.</returns>
    public static ExchangeRate Create(string from, string to, decimal rate, DateTimeOffset? validFrom, DateTimeOffset? validUntil, string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(from);
        ArgumentException.ThrowIfNullOrWhiteSpace(to);
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("From and to currencies must differ.", nameof(to));
        }

        ValidateWindow(validFrom, validUntil);
        if (rate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rate), "Rate must be positive.");
        }

        return new ExchangeRate
        {
            FromCurrency = from,
            ToCurrency = to,
            Rate = rate,
            ValidFrom = validFrom,
            ValidUntil = validUntil,
            TenantId = tenantId,
        };
    }

    /// <summary>Updates the rate.</summary>
    /// <param name="rate">The new positive rate.</param>
    public void UpdateRate(decimal rate)
    {
        if (rate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rate), "Rate must be positive.");
        }

        Rate = rate;
    }

    /// <summary>Updates the validity window.</summary>
    /// <param name="from">The new inclusive start, or null.</param>
    /// <param name="until">The new exclusive end, or null.</param>
    public void UpdateValidity(DateTimeOffset? from, DateTimeOffset? until)
    {
        ValidateWindow(from, until);
        ValidFrom = from;
        ValidUntil = until;
    }

    /// <summary>Determines whether this rate is usable at a moment.</summary>
    /// <param name="at">The moment to test.</param>
    /// <returns><c>true</c> if within the (possibly open) window.</returns>
    public bool IsValidAt(DateTimeOffset at) =>
        (ValidFrom is null || at >= ValidFrom) && (ValidUntil is null || at < ValidUntil);

    private static void ValidateWindow(DateTimeOffset? from, DateTimeOffset? until)
    {
        if (from is not null && until is not null && until <= from)
        {
            throw new ArgumentException("ValidUntil must be after ValidFrom.", nameof(until));
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/unit/Pricing.UnitTests --filter ExchangeRateTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/services/commerce/pricing/Pricing.Domain/Entities/ExchangeRate.cs tests/unit/Pricing.UnitTests/ExchangeRateTests.cs
git commit -m "feat(pricing): ExchangeRate aggregate"
```

---

### Task 6: `PriceResolutionService` — most-specific, native-preferred selection (the core)

**Files:**
- Create: `src/services/commerce/pricing/Pricing.Domain/Services/PriceResolutionContext.cs`
- Create: `src/services/commerce/pricing/Pricing.Domain/Services/ResolvedSelection.cs`
- Create: `src/services/commerce/pricing/Pricing.Domain/Services/PriceResolutionService.cs`
- Test: `tests/unit/Pricing.UnitTests/PriceResolutionServiceTests.cs`

**Interfaces:**
- Consumes: `Price`, `PriceList`, `PriceScope`, `Money` (Tasks 2, 4).
- Produces:
  - `PriceResolutionContext(string Currency, int Quantity, string? Country, Guid? CustomerGroupId, Guid? ChannelId, DateTimeOffset At)` — `sealed record`.
  - `ResolvedSelection(Price Price, Money UnitAmount)` — `sealed record`. `UnitAmount.Currency` is the winning price's native currency (FX applied later by the handler).
  - `static ResolvedSelection? PriceResolutionService.SelectBest(IEnumerable<Price> candidates, PriceResolutionContext context)` — filters to prices whose `PriceList` is `Active`, valid at `context.At`, and scope-compatible; prefers native-currency candidates; among the chosen pool picks most-specific (tie-break: channel-set, then group-set, then country-set, then lowest unit amount, then earliest list `CreatedAt`); returns the winner with its tiered unit amount, or `null` if none.

- [ ] **Step 1: Write the failing test** `tests/unit/Pricing.UnitTests/PriceResolutionServiceTests.cs`

```csharp
using Pricing.Domain.Entities;
using Pricing.Domain.Services;
using Pricing.Domain.ValueObjects;
using Xunit;

namespace Pricing.UnitTests;

public sealed class PriceResolutionServiceTests
{
    private static readonly Guid Product = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private static Price PriceIn(PriceList list)
    {
        // AddOrUpdatePrice creates the Price inside the list; return it with its navigation set.
        Price price = Assert.Single(list.Prices);
        typeof(Price).GetProperty(nameof(Price.PriceList))!.SetValue(price, list);
        return price;
    }

    private static (PriceList List, Price Price) ActiveList(PriceScope scope, decimal amount, params PriceTier[] tiers)
    {
        var list = PriceList.Create("l", scope, null, null, "tenant-1");
        list.AddOrUpdatePrice(Product, new Money(amount, scope.Currency), tiers);
        list.Activate();
        return (list, PriceIn(list));
    }

    private static PriceResolutionContext Ctx(string currency = "USD", int qty = 1, string? country = null, Guid? group = null, Guid? channel = null) =>
        new(currency, qty, country, group, channel, Now);

    [Fact]
    public void SelectBest_NoCandidates_ReturnsNull() =>
        Assert.Null(PriceResolutionService.SelectBest([], Ctx()));

    [Fact]
    public void SelectBest_DraftList_Ignored()
    {
        var list = PriceList.Create("l", new PriceScope("USD", null, null, null), null, null, "tenant-1");
        list.AddOrUpdatePrice(Product, new Money(10m, "USD"), []);
        // not activated -> Draft
        Price price = PriceIn(list);

        Assert.Null(PriceResolutionService.SelectBest([price], Ctx()));
    }

    [Fact]
    public void SelectBest_MostSpecificScopeWins()
    {
        var group = Guid.NewGuid();
        var (_, general) = ActiveList(new PriceScope("USD", null, null, null), 10m);
        var (_, specific) = ActiveList(new PriceScope("USD", null, group, null), 8m);

        ResolvedSelection? result = PriceResolutionService.SelectBest([general, specific], Ctx(group: group));

        Assert.NotNull(result);
        Assert.Equal(8m, result!.UnitAmount.Amount);
    }

    [Fact]
    public void SelectBest_PrefersNativeCurrencyOverMoreSpecificForeign()
    {
        var group = Guid.NewGuid();
        var (_, nativeGeneral) = ActiveList(new PriceScope("USD", null, null, null), 10m);
        var (_, foreignSpecific) = ActiveList(new PriceScope("EUR", null, group, null), 5m);

        ResolvedSelection? result = PriceResolutionService.SelectBest([nativeGeneral, foreignSpecific], Ctx(currency: "USD", group: group));

        Assert.NotNull(result);
        Assert.Equal("USD", result!.UnitAmount.Currency);
        Assert.Equal(10m, result.UnitAmount.Amount);
    }

    [Fact]
    public void SelectBest_AppliesQuantityTier()
    {
        var (_, tiered) = ActiveList(new PriceScope("USD", null, null, null), 10m, new PriceTier(1, new Money(10m, "USD")), new PriceTier(10, new Money(8m, "USD")));

        ResolvedSelection? result = PriceResolutionService.SelectBest([tiered], Ctx(qty: 10));

        Assert.Equal(8m, result!.UnitAmount.Amount);
    }

    [Fact]
    public void SelectBest_IncompatibleScope_Excluded()
    {
        var (_, deOnly) = ActiveList(new PriceScope("USD", "DE", null, null), 10m);

        Assert.Null(PriceResolutionService.SelectBest([deOnly], Ctx(country: "US")));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/unit/Pricing.UnitTests --filter PriceResolutionServiceTests`
Expected: FAIL — resolution types do not exist.

- [ ] **Step 3: Create `PriceResolutionContext.cs`**

```csharp
namespace Pricing.Domain.Services;

/// <summary>The input context for resolving a product's price.</summary>
/// <param name="Currency">The requested ISO currency (required).</param>
/// <param name="Quantity">The requested quantity (>= 1).</param>
/// <param name="Country">The optional request country.</param>
/// <param name="CustomerGroupId">The optional request customer group.</param>
/// <param name="ChannelId">The optional request channel.</param>
/// <param name="At">The moment at which to resolve.</param>
public sealed record PriceResolutionContext(
    string Currency,
    int Quantity,
    string? Country,
    Guid? CustomerGroupId,
    Guid? ChannelId,
    DateTimeOffset At);
```

- [ ] **Step 4: Create `ResolvedSelection.cs`**

```csharp
using Pricing.Domain.Entities;
using Pricing.Domain.ValueObjects;

namespace Pricing.Domain.Services;

/// <summary>The result of price selection: the winning price and its tiered unit amount (native currency).</summary>
/// <param name="Price">The winning price.</param>
/// <param name="UnitAmount">The tiered unit amount in the price's native currency.</param>
public sealed record ResolvedSelection(Price Price, Money UnitAmount);
```

- [ ] **Step 5: Create `PriceResolutionService.cs`**

```csharp
using Pricing.Domain.Entities;
using Pricing.Domain.ValueObjects;

namespace Pricing.Domain.Services;

/// <summary>
/// Selects the winning price for a resolution context: most-specific scope, native currency
/// preferred, with quantity-tier application. Pure and side-effect free; FX conversion of a
/// foreign winner is applied by the caller.
/// </summary>
public static class PriceResolutionService
{
    /// <summary>Selects the best price for the context, or null when none applies.</summary>
    /// <param name="candidates">Candidate prices for the product (each with its <see cref="Price.PriceList"/> loaded).</param>
    /// <param name="context">The resolution context.</param>
    /// <returns>The winning selection, or null.</returns>
    public static ResolvedSelection? SelectBest(IEnumerable<Price> candidates, PriceResolutionContext context)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(context);

        List<Price> compatible = candidates
            .Where(price => price.PriceList is not null
                && price.PriceList.Status == PriceListStatus.Active
                && price.PriceList.IsValidAt(context.At)
                && price.PriceList.Scope.IsCompatibleWith(context.Country, context.CustomerGroupId, context.ChannelId))
            .ToList();

        if (compatible.Count == 0)
        {
            return null;
        }

        List<Price> native = compatible
            .Where(price => string.Equals(price.PriceList.Scope.Currency, context.Currency, StringComparison.OrdinalIgnoreCase))
            .ToList();

        List<Price> pool = native.Count > 0 ? native : compatible;

        Price winner = pool
            .OrderByDescending(price => price.PriceList.Scope.Specificity)
            .ThenByDescending(price => price.PriceList.Scope.ChannelId is not null)
            .ThenByDescending(price => price.PriceList.Scope.CustomerGroupId is not null)
            .ThenByDescending(price => price.PriceList.Scope.Country is not null)
            .ThenBy(price => price.UnitAmountFor(context.Quantity).Amount)
            .ThenBy(price => price.PriceList.CreatedAt)
            .First();

        return new ResolvedSelection(winner, winner.UnitAmountFor(context.Quantity));
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/unit/Pricing.UnitTests --filter PriceResolutionServiceTests`
Expected: PASS (6 tests).

- [ ] **Step 7: Commit**

```bash
git add src/services/commerce/pricing/Pricing.Domain/Services tests/unit/Pricing.UnitTests/PriceResolutionServiceTests.cs
git commit -m "feat(pricing): price resolution domain service"
```

---

### Task 7: Persistence — DbContext trio, EF configurations, repositories, DI

**Files:**
- Create: `src/services/commerce/pricing/Pricing.Application/Database/PricingDbContextBase.cs`
- Create: `src/services/commerce/pricing/Pricing.Application/Database/PricingDbContext.cs`
- Create: `src/services/commerce/pricing/Pricing.Application/Database/Configurations/PriceListConfiguration.cs`
- Create: `src/services/commerce/pricing/Pricing.Application/Database/Configurations/PriceConfiguration.cs`
- Create: `src/services/commerce/pricing/Pricing.Application/Database/Configurations/ExchangeRateConfiguration.cs`
- Create: `src/services/commerce/pricing/Pricing.Host/Database/PricingReadDbContext.cs`
- Create: `src/services/commerce/pricing/Pricing.Host/Database/PricingReadRepository.cs`
- Create: `src/services/commerce/pricing/Pricing.Host/Database/PricingWriteRepository.cs`
- Create: `src/services/commerce/pricing/Pricing.Host/Database/PricingPersistenceExtensions.cs`
- Create: `src/services/commerce/pricing/Pricing.Host/Database/PricingDbContextDesignTimeFactory.cs`
- Test: `tests/unit/Pricing.UnitTests/PricingDbContextTests.cs`

**Interfaces:**
- Consumes: `PriceList`, `Price`, `ExchangeRate` (Tasks 4, 5).
- Produces: `PricingDbContextBase` (`DbSet<PriceList> PriceLists`, `DbSet<Price> Prices`, `DbSet<ExchangeRate> ExchangeRates`); `PricingDbContext` (write leaf); `PricingReadDbContext` (NoTracking, Host); `AddPricingPersistence(this WebApplicationBuilder)`; open-generic `PricingReadRepository<,>` / `PricingWriteRepository<,>`; `PricingDbContextDesignTimeFactory`.

- [ ] **Step 1: Create `PricingDbContextBase.cs`**

```csharp
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Pricing.Domain.Entities;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.MultiTenant;

namespace Pricing.Application.Database;

/// <summary>
/// Abstract pricing context defining the entity model exactly once. The write and read contexts
/// derive from it.
/// </summary>
/// <param name="options">The options used to configure the context.</param>
/// <param name="tenantContextAccessor">The accessor used to resolve the current tenant.</param>
public abstract class PricingDbContextBase(DbContextOptions options, IMultiTenantContextAccessor<TenantDetails> tenantContextAccessor)
    : BaseDbContext(options, tenantAccessor: tenantContextAccessor)
{
    /// <summary>Gets the set of tracked price lists.</summary>
    public DbSet<PriceList> PriceLists => Set<PriceList>();

    /// <summary>Gets the set of tracked prices.</summary>
    public DbSet<Price> Prices => Set<Price>();

    /// <summary>Gets the set of tracked exchange rates.</summary>
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Owned-type configuration must run before base.OnModelCreating so Finbuckle does not
        // discover owned collections (Prices.Tiers, PriceList.Scope) as plain entities.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PricingDbContextBase).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
```

- [ ] **Step 2: Create `PricingDbContext.cs`**

```csharp
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Infrastructure.MultiTenant;

namespace Pricing.Application.Database;

/// <summary>The pricing write context (change tracking enabled). Owns EF Core migrations.</summary>
/// <param name="options">The options used to configure the context.</param>
/// <param name="tenantContextAccessor">The accessor used to resolve the current tenant.</param>
public class PricingDbContext(DbContextOptions options, IMultiTenantContextAccessor<TenantDetails> tenantContextAccessor)
    : PricingDbContextBase(options, tenantContextAccessor);
```

- [ ] **Step 3: Create `PriceListConfiguration.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pricing.Domain.Entities;
using Pricing.Domain.ValueObjects;

namespace Pricing.Application.Database.Configurations;

/// <summary>Configures the EF Core mapping for the <see cref="PriceList"/> aggregate.</summary>
public sealed class PriceListConfiguration : IEntityTypeConfiguration<PriceList>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<PriceList> builder)
    {
        builder.ToTable("PriceLists");
        builder.HasKey(list => list.Id);
        builder.Ignore(list => list.DomainEvents);
        builder.Property(list => list.TenantId).HasMaxLength(64);
        builder.Property(list => list.Name).HasMaxLength(256);

        builder.Property(list => list.Status)
            .HasConversion(status => status.Value, value => PriceListStatus.FromValue(value));

        builder.OwnsOne(list => list.Scope, scope =>
        {
            scope.Property(s => s.Currency).HasColumnName("Currency").HasMaxLength(3).IsRequired();
            scope.Property(s => s.Country).HasColumnName("Country").HasMaxLength(2);
            scope.Property(s => s.CustomerGroupId).HasColumnName("CustomerGroupId");
            scope.Property(s => s.ChannelId).HasColumnName("ChannelId");
        });

        builder.Navigation(list => list.Prices).HasField("_prices");
        builder.HasMany(list => list.Prices)
            .WithOne(price => price.PriceList)
            .HasForeignKey(price => price.PriceListId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(list => new { list.TenantId, list.Status });
    }
}
```

- [ ] **Step 4: Create `PriceConfiguration.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pricing.Domain.Entities;
using Pricing.Domain.ValueObjects;

namespace Pricing.Application.Database.Configurations;

/// <summary>Configures the EF Core mapping for the <see cref="Price"/> entity and its owned tiers.</summary>
public sealed class PriceConfiguration : IEntityTypeConfiguration<Price>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Price> builder)
    {
        builder.ToTable("Prices");
        builder.HasKey(price => price.Id);
        builder.Ignore(price => price.DomainEvents);
        builder.Property(price => price.TenantId).HasMaxLength(64);

        // Base amount as an owned Money (Amount + Currency columns).
        builder.OwnsOne(price => price.Amount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("Amount");
            money.Property(m => m.Currency).HasColumnName("Currency").HasMaxLength(3).IsRequired();
        });
        builder.Navigation(price => price.Amount).IsRequired();

        // Tiers as an owned collection; each tier owns its Money amount.
        builder.Navigation(price => price.Tiers).HasField("_tiers");
        builder.OwnsMany(price => price.Tiers, tier =>
        {
            tier.ToTable("PriceTiers");
            tier.WithOwner().HasForeignKey("PriceId");
            tier.Property(t => t.MinQuantity);
            tier.HasKey("PriceId", nameof(PriceTier.MinQuantity));
            tier.OwnsOne(t => t.Amount, money =>
            {
                money.Property(m => m.Amount).HasColumnName("Amount");
                money.Property(m => m.Currency).HasColumnName("Currency").HasMaxLength(3).IsRequired();
            });
            tier.Navigation(t => t.Amount).IsRequired();
        });

        // The resolution hot-path key.
        builder.HasIndex(price => new { price.TenantId, price.ProductId });
    }
}
```

- [ ] **Step 5: Create `ExchangeRateConfiguration.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pricing.Domain.Entities;

namespace Pricing.Application.Database.Configurations;

/// <summary>Configures the EF Core mapping for the <see cref="ExchangeRate"/> aggregate.</summary>
public sealed class ExchangeRateConfiguration : IEntityTypeConfiguration<ExchangeRate>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<ExchangeRate> builder)
    {
        builder.ToTable("ExchangeRates");
        builder.HasKey(rate => rate.Id);
        builder.Ignore(rate => rate.DomainEvents);
        builder.Property(rate => rate.TenantId).HasMaxLength(64);
        builder.Property(rate => rate.FromCurrency).HasMaxLength(3).IsRequired();
        builder.Property(rate => rate.ToCurrency).HasMaxLength(3).IsRequired();

        builder.HasIndex(rate => new { rate.TenantId, rate.FromCurrency, rate.ToCurrency }).IsUnique();
    }
}
```

- [ ] **Step 6: Create `Pricing.Host/Database/PricingReadDbContext.cs`**

```csharp
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Pricing.Application.Database;
using SharedKernel.Infrastructure.MultiTenant;

namespace Pricing.Host.Database;

/// <summary>The pricing read context (change tracking disabled).</summary>
/// <param name="options">The options used to configure the context.</param>
/// <param name="tenantContextAccessor">The accessor providing the current tenant context.</param>
public class PricingReadDbContext(DbContextOptions options, IMultiTenantContextAccessor<TenantDetails> tenantContextAccessor)
    : PricingDbContextBase(options, tenantContextAccessor)
{
    /// <inheritdoc/>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    }
}
```

- [ ] **Step 7: Create `Pricing.Host/Database/PricingReadRepository.cs`**

```csharp
using SharedKernel.Core.Domain;
using SharedKernel.Infrastructure.Database.EFCore;

namespace Pricing.Host.Database;

/// <summary>Pricing read repository bound to <see cref="PricingReadDbContext"/> (NoTracking).</summary>
/// <typeparam name="TReadModel">The read model type.</typeparam>
/// <typeparam name="TId">The identifier type.</typeparam>
/// <param name="dbContext">The pricing read context.</param>
public sealed class PricingReadRepository<TReadModel, TId>(PricingReadDbContext dbContext)
    : GenericReadRepository<TReadModel, TId, PricingReadDbContext>(dbContext)
    where TReadModel : class, IReadModel<TId>;
```

- [ ] **Step 8: Create `Pricing.Host/Database/PricingWriteRepository.cs`**

```csharp
using Microsoft.AspNetCore.Http;
using Pricing.Application.Database;
using SharedKernel.Core.Domain;
using SharedKernel.Infrastructure.Database.EFCore;

namespace Pricing.Host.Database;

/// <summary>Pricing write repository bound to <see cref="PricingDbContext"/>.</summary>
/// <typeparam name="TEntity">The aggregate entity type.</typeparam>
/// <typeparam name="TId">The entity identifier type.</typeparam>
/// <param name="dbContext">The pricing write context.</param>
/// <param name="httpContextAccessor">The HTTP context accessor used for audit stamping on bulk deletes.</param>
public sealed class PricingWriteRepository<TEntity, TId>(PricingDbContext dbContext, IHttpContextAccessor httpContextAccessor)
    : GenericWriteRepository<TEntity, TId, PricingDbContext>(dbContext, httpContextAccessor)
    where TEntity : BaseEntity;
```

- [ ] **Step 9: Create `Pricing.Host/Database/PricingPersistenceExtensions.cs`**

```csharp
using Pricing.Application.Database;
using SharedKernel.Core.Database;
using SharedKernel.Infrastructure.Database;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.Database.MultiTenant;

namespace Pricing.Host.Database;

/// <summary>Registers the pricing persistence stack: tenant-aware contexts, repositories, unit of work.</summary>
public static class PricingPersistenceExtensions
{
    /// <summary>Adds the pricing read/write contexts, repositories and unit of work to the host.</summary>
    /// <param name="builder">The web application builder.</param>
    /// <returns>The same builder for chaining.</returns>
    public static WebApplicationBuilder AddPricingPersistence(this WebApplicationBuilder builder)
    {
        var write = CodegenConnectionString.ResolveRequired(builder.Configuration, "PricingWrite", "Default");
        var read = builder.Configuration.GetConnectionString("PricingRead") ?? write;

        builder.AddHybridMultiTenantDbContexts<PricingDbContext, PricingReadDbContext>(
            migrationsAssembly: typeof(Program).Assembly,
            defaultWriteConnectionString: write,
            defaultReadConnectionString: read,
            serviceName: "pricing");

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped(typeof(IGenericReadRepository<,>), typeof(PricingReadRepository<,>));
        builder.Services.AddScoped(typeof(IGenericWriteRepository<,>), typeof(PricingWriteRepository<,>));
        builder.Services.AddScoped<IUnitOfWork>(sp => new UnitOfWork<PricingDbContext>(sp.GetRequiredService<PricingDbContext>()));

        return builder;
    }
}
```

- [ ] **Step 10: Create `Pricing.Host/Database/PricingDbContextDesignTimeFactory.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pricing.Application.Database;

namespace Pricing.Host.Database;

/// <summary>Design-time factory for <see cref="PricingDbContext"/> used by EF Core migrations tooling.</summary>
public sealed class PricingDbContextDesignTimeFactory : IDesignTimeDbContextFactory<PricingDbContext>
{
    /// <inheritdoc/>
    public PricingDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("PRICING_DESIGN_TIME_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=pricing_design;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<PricingDbContext>();
        optionsBuilder.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsAssembly(typeof(PricingDbContextDesignTimeFactory).Assembly.FullName));

        return new PricingDbContext(optionsBuilder.Options, tenantContextAccessor: null!);
    }
}
```

- [ ] **Step 11: Write the model-builds test** `tests/unit/Pricing.UnitTests/PricingDbContextTests.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Pricing.Application.Database;
using Xunit;

namespace Pricing.UnitTests;

public sealed class PricingDbContextTests
{
    [Fact]
    public void Model_BuildsWithoutError()
    {
        var options = new DbContextOptionsBuilder<PricingDbContext>()
            .UseInMemoryDatabase("pricing-model-test")
            .Options;

        using var context = new PricingDbContext(options, tenantContextAccessor: null!);

        // Forcing model creation validates every IEntityTypeConfiguration (owned Money, tiers, scope).
        Assert.NotNull(context.Model.FindEntityType(typeof(Pricing.Domain.Entities.PriceList)));
        Assert.NotNull(context.Model.FindEntityType(typeof(Pricing.Domain.Entities.Price)));
        Assert.NotNull(context.Model.FindEntityType(typeof(Pricing.Domain.Entities.ExchangeRate)));
    }
}
```

> Note: the in-memory provider builds the model without a tenant accessor. `BaseDbContext.OnConfiguring`/interceptors may require a non-null accessor at query time, but model construction (this test) does not. If `null!` throws during `OnModelCreating`, mirror `Basket.UnitTests/BasketDbContextTests.cs` which performs the same model-build assertion — copy its accessor setup.

- [ ] **Step 12: Run tests to verify they pass**

Run: `dotnet test tests/unit/Pricing.UnitTests --filter PricingDbContextTests`
Expected: PASS. If the model fails to build, the EF configuration (nested owned Money) is wrong — fix before proceeding; this test is the guard for Task 18's migration.

- [ ] **Step 13: Commit**

```bash
git add src/services/commerce/pricing/Pricing.Application/Database src/services/commerce/pricing/Pricing.Host/Database tests/unit/Pricing.UnitTests/PricingDbContextTests.cs
git commit -m "feat(pricing): persistence — dbcontext trio, EF config, repositories, DI"
```

---

### Task 8: Response DTOs + Mapperly mappers

**Files:**
- Create: `Pricing.Application/Pricing/Responses/PriceListDto.cs`
- Create: `Pricing.Application/Pricing/Responses/PriceDto.cs`
- Create: `Pricing.Application/Pricing/Responses/PriceTierDto.cs`
- Create: `Pricing.Application/Pricing/Responses/ResolvedPriceDto.cs`
- Create: `Pricing.Application/Pricing/Responses/ExchangeRateDto.cs`
- Create: `Pricing.Application/Pricing/Mapping/PriceListMapper.cs`
- Create: `Pricing.Application/Pricing/Mapping/ExchangeRateMapper.cs`
- Test: `tests/unit/Pricing.UnitTests/PriceListMapperTests.cs`

**Interfaces:**
- Produces: DTOs below; `PriceListMapper.ToDto(this PriceList)`, `ExchangeRateMapper.ToDto(this ExchangeRate)`. `ResolvedPriceDto` is constructed by hand in the resolve handler (Task 16), not mapped.

- [ ] **Step 1: Create the DTOs**

`Pricing/Responses/PriceTierDto.cs`:
```csharp
namespace Pricing.Application.Pricing.Responses;

/// <summary>A quantity tier in API responses.</summary>
/// <param name="MinQuantity">The minimum quantity at which the amount applies.</param>
/// <param name="Amount">The unit amount.</param>
public sealed record PriceTierDto(int MinQuantity, decimal Amount);
```

`Pricing/Responses/PriceDto.cs`:
```csharp
namespace Pricing.Application.Pricing.Responses;

/// <summary>A product price in API responses.</summary>
/// <param name="ProductId">The product identifier.</param>
/// <param name="Amount">The base unit amount.</param>
/// <param name="Currency">The ISO currency.</param>
/// <param name="Tiers">The quantity tiers.</param>
public sealed record PriceDto(Guid ProductId, decimal Amount, string Currency, IReadOnlyList<PriceTierDto> Tiers);
```

`Pricing/Responses/PriceListDto.cs`:
```csharp
namespace Pricing.Application.Pricing.Responses;

/// <summary>A price list in API responses.</summary>
/// <param name="Id">The list identifier.</param>
/// <param name="Name">The display name.</param>
/// <param name="Description">The optional description.</param>
/// <param name="Status">The lifecycle status name.</param>
/// <param name="Currency">The scope currency.</param>
/// <param name="Country">The scope country, or null.</param>
/// <param name="CustomerGroupId">The scope customer group, or null.</param>
/// <param name="ChannelId">The scope channel, or null.</param>
/// <param name="ValidFrom">The inclusive validity start, or null.</param>
/// <param name="ValidUntil">The exclusive validity end, or null.</param>
/// <param name="Prices">The contained prices.</param>
public sealed record PriceListDto(
    Guid Id,
    string Name,
    string? Description,
    string Status,
    string Currency,
    string? Country,
    Guid? CustomerGroupId,
    Guid? ChannelId,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidUntil,
    IReadOnlyList<PriceDto> Prices);
```

`Pricing/Responses/ResolvedPriceDto.cs`:
```csharp
namespace Pricing.Application.Pricing.Responses;

/// <summary>The resolved price for a product in a request context.</summary>
/// <param name="ProductId">The product identifier.</param>
/// <param name="UnitAmount">The resolved unit amount (converted if cross-currency).</param>
/// <param name="Currency">The requested ISO currency of the amount.</param>
/// <param name="PriceListId">The winning price list.</param>
/// <param name="Converted">Whether an FX conversion was applied.</param>
/// <param name="RateApplied">The FX rate applied, or null when native.</param>
public sealed record ResolvedPriceDto(
    Guid ProductId,
    decimal UnitAmount,
    string Currency,
    Guid PriceListId,
    bool Converted,
    decimal? RateApplied);
```

`Pricing/Responses/ExchangeRateDto.cs`:
```csharp
namespace Pricing.Application.Pricing.Responses;

/// <summary>An exchange rate in API responses.</summary>
/// <param name="Id">The rate identifier.</param>
/// <param name="FromCurrency">The source ISO currency.</param>
/// <param name="ToCurrency">The target ISO currency.</param>
/// <param name="Rate">The multiplicative rate.</param>
/// <param name="ValidFrom">The inclusive validity start, or null.</param>
/// <param name="ValidUntil">The exclusive validity end, or null.</param>
public sealed record ExchangeRateDto(
    Guid Id,
    string FromCurrency,
    string ToCurrency,
    decimal Rate,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidUntil);
```

- [ ] **Step 2: Create `PriceListMapper.cs`**

```csharp
using Pricing.Application.Pricing.Responses;
using Pricing.Domain.Entities;
using Pricing.Domain.ValueObjects;
using Riok.Mapperly.Abstractions;

namespace Pricing.Application.Pricing.Mapping;

/// <summary>Mapperly mappings from pricing entities to their DTOs.</summary>
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class PriceListMapper
{
    /// <summary>Maps a <see cref="PriceList"/> to a <see cref="PriceListDto"/>.</summary>
    /// <param name="list">The price list.</param>
    /// <returns>The mapped DTO.</returns>
    [MapProperty("Status.Name", nameof(PriceListDto.Status))]
    [MapProperty("Scope.Currency", nameof(PriceListDto.Currency))]
    [MapProperty("Scope.Country", nameof(PriceListDto.Country))]
    [MapProperty("Scope.CustomerGroupId", nameof(PriceListDto.CustomerGroupId))]
    [MapProperty("Scope.ChannelId", nameof(PriceListDto.ChannelId))]
    public static partial PriceListDto ToDto(this PriceList list);

    /// <summary>Maps a <see cref="Price"/> to a <see cref="PriceDto"/>.</summary>
    /// <param name="price">The price.</param>
    /// <returns>The mapped DTO.</returns>
    [MapProperty("Amount.Amount", nameof(PriceDto.Amount))]
    [MapProperty("Amount.Currency", nameof(PriceDto.Currency))]
    public static partial PriceDto ToDto(this Price price);

    /// <summary>Maps a <see cref="PriceTier"/> to a <see cref="PriceTierDto"/>.</summary>
    /// <param name="tier">The tier.</param>
    /// <returns>The mapped DTO.</returns>
    [MapProperty("Amount.Amount", nameof(PriceTierDto.Amount))]
    public static partial PriceTierDto ToDto(this PriceTier tier);
}
```

- [ ] **Step 3: Create `ExchangeRateMapper.cs`**

```csharp
using Pricing.Application.Pricing.Responses;
using Pricing.Domain.Entities;
using Riok.Mapperly.Abstractions;

namespace Pricing.Application.Pricing.Mapping;

/// <summary>Mapperly mappings for exchange rates.</summary>
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class ExchangeRateMapper
{
    /// <summary>Maps an <see cref="ExchangeRate"/> to an <see cref="ExchangeRateDto"/>.</summary>
    /// <param name="rate">The exchange rate.</param>
    /// <returns>The mapped DTO.</returns>
    public static partial ExchangeRateDto ToDto(this ExchangeRate rate);
}
```

- [ ] **Step 4: Write the mapper test** `tests/unit/Pricing.UnitTests/PriceListMapperTests.cs`

```csharp
using Pricing.Application.Pricing.Mapping;
using Pricing.Application.Pricing.Responses;
using Pricing.Domain.Entities;
using Pricing.Domain.ValueObjects;
using Xunit;

namespace Pricing.UnitTests;

public sealed class PriceListMapperTests
{
    [Fact]
    public void ToDto_FlattensScopeStatusAndPrices()
    {
        var list = PriceList.Create("Retail", new PriceScope("USD", "US", null, null), null, null, "tenant-1");
        list.AddOrUpdatePrice(Guid.NewGuid(), new Money(10m, "USD"), [new PriceTier(1, new Money(10m, "USD"))]);
        list.Activate();

        PriceListDto dto = list.ToDto();

        Assert.Equal("Active", dto.Status);
        Assert.Equal("USD", dto.Currency);
        Assert.Equal("US", dto.Country);
        PriceDto price = Assert.Single(dto.Prices);
        Assert.Equal(10m, price.Amount);
        Assert.Equal("USD", price.Currency);
        Assert.Single(price.Tiers);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/unit/Pricing.UnitTests --filter PriceListMapperTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/services/commerce/pricing/Pricing.Application/Pricing/Responses src/services/commerce/pricing/Pricing.Application/Pricing/Mapping tests/unit/Pricing.UnitTests/PriceListMapperTests.cs
git commit -m "feat(pricing): response DTOs and Mapperly mappers"
```

---

### Task 9: Specifications

**Files:**
- Create: `Pricing.Application/Pricing/ReadModels/PricesByProductSpec.cs`
- Create: `Pricing.Application/Pricing/ReadModels/PriceListByIdSpec.cs`
- Create: `Pricing.Application/Pricing/ReadModels/ExchangeRateByPairSpec.cs`

**Interfaces:**
- Produces: `PricesByProductSpec(Guid productId)` : `Specification<Price>` (includes `PriceList`); `PriceListByIdSpec(Guid id)` : `Specification<PriceList>` (includes `Prices`); `ExchangeRateByPairSpec(string from, string to)` : `Specification<ExchangeRate>`.

- [ ] **Step 1: Create `PricesByProductSpec.cs`**

```csharp
using Ardalis.Specification;
using Pricing.Domain.Entities;

namespace Pricing.Application.Pricing.ReadModels;

/// <summary>Selects all prices for a product, including their owning price list (scope/status/validity).</summary>
public sealed class PricesByProductSpec : Specification<Price>
{
    /// <summary>Initializes a new instance of the <see cref="PricesByProductSpec"/> class.</summary>
    /// <param name="productId">The product identifier.</param>
    public PricesByProductSpec(Guid productId) =>
        Query.Where(price => price.ProductId == productId).Include(price => price.PriceList);
}
```

- [ ] **Step 2: Create `PriceListByIdSpec.cs`**

```csharp
using Ardalis.Specification;
using Pricing.Domain.Entities;

namespace Pricing.Application.Pricing.ReadModels;

/// <summary>Selects a single price list by identifier, including its prices.</summary>
public sealed class PriceListByIdSpec : Specification<PriceList>
{
    /// <summary>Initializes a new instance of the <see cref="PriceListByIdSpec"/> class.</summary>
    /// <param name="id">The price list identifier.</param>
    public PriceListByIdSpec(Guid id) =>
        Query.Where(list => list.Id == id).Include(list => list.Prices);
}
```

- [ ] **Step 3: Create `ExchangeRateByPairSpec.cs`**

```csharp
using Ardalis.Specification;
using Pricing.Domain.Entities;

namespace Pricing.Application.Pricing.ReadModels;

/// <summary>Selects the exchange rate for a currency pair.</summary>
public sealed class ExchangeRateByPairSpec : Specification<ExchangeRate>
{
    /// <summary>Initializes a new instance of the <see cref="ExchangeRateByPairSpec"/> class.</summary>
    /// <param name="from">The source ISO currency.</param>
    /// <param name="to">The target ISO currency.</param>
    public ExchangeRateByPairSpec(string from, string to) =>
        Query.Where(rate => rate.FromCurrency == from && rate.ToCurrency == to);
}
```

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build src/services/commerce/pricing/Pricing.Application/Pricing.Application.csproj`
Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git add src/services/commerce/pricing/Pricing.Application/Pricing/ReadModels
git commit -m "feat(pricing): read specifications"
```

---

### Task 10: Shared cross-service contract `PriceChangedIntegrationEvent`

**Files:**
- Create: `src/shared/SharedKernel.Events/PriceChangedIntegrationEvent.cs`

**Interfaces:**
- Produces: `PriceChangedIntegrationEvent` — `[MemoryPackable] partial class : IntegrationEvent` with settable `ProductId (Guid)`, `PriceListId (Guid)`, `TenantId (string)`, `Amount (decimal)`, `Currency (string)`, `EffectiveFrom (DateTimeOffset)`, `ChangeType (string)`, and a parameterless `[MemoryPackConstructor]`. Pricing-owned per COORDINATION. Mirrors `BasketCheckedOutIntegrationEvent`. Does NOT reference the domain event (SharedKernel must not reference a service) — the publisher maps by hand.

- [ ] **Step 1: Create `PriceChangedIntegrationEvent.cs`**

```csharp
using MemoryPack;
using SharedKernel.Core.Events;

namespace SharedKernel.Events;

/// <summary>
/// Integration event published when an effective product price changes. Owned by the pricing
/// service. Consumers (basket reprice, search, catalog display) subscribe without referencing pricing.
/// </summary>
[MemoryPackable]
public partial class PriceChangedIntegrationEvent : IntegrationEvent
{
    /// <summary>Initializes a new instance of the <see cref="PriceChangedIntegrationEvent"/> class.</summary>
    [MemoryPackConstructor]
    public PriceChangedIntegrationEvent()
    {
    }

    /// <summary>Gets or sets the product whose price changed.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Gets or sets the owning price list.</summary>
    public Guid PriceListId { get; set; }

    /// <summary>Gets or sets the owning tenant.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets or sets the amount involved in the change.</summary>
    public decimal Amount { get; set; }

    /// <summary>Gets or sets the ISO currency of the amount.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Gets or sets when the change takes effect.</summary>
    public DateTimeOffset EffectiveFrom { get; set; }

    /// <summary>Gets or sets the change type ("Upserted" or "Removed").</summary>
    public string ChangeType { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Build the shared project**

Run: `dotnet build src/shared/SharedKernel.Events/SharedKernel.Events.csproj`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/shared/SharedKernel.Events/PriceChangedIntegrationEvent.cs
git commit -m "feat(pricing): PriceChanged integration event contract"
```

---

### Task 11: `PricingOptions`, `IExchangeRateProvider`, `PricingEventPublisher`

**Files:**
- Create: `Pricing.Application/Pricing/PricingOptions.cs`
- Create: `Pricing.Application/Pricing/IExchangeRateProvider.cs`
- Create: `Pricing.Application/Pricing/RateSnapshot.cs`
- Create: `Pricing.Application/Pricing/PricingEventPublisher.cs`
- Test: `tests/unit/Pricing.UnitTests/PricingEventPublisherTests.cs`

**Interfaces:**
- Produces:
  - `PricingOptions` — `RoundingDecimals` (default 2), `RoundingMode` (default `MidpointRounding.ToEven`), `MaxTiersPerPrice` (default 20).
  - `IExchangeRateProvider` — `Task<IReadOnlyList<RateSnapshot>> GetLatestAsync(CancellationToken ct)`.
  - `RateSnapshot(string FromCurrency, string ToCurrency, decimal Rate)`.
  - `static Task PricingEventPublisher.PublishAsync(IEnumerable<PriceChanged> events, IMessageBus bus)` — maps each domain event to `PriceChangedIntegrationEvent` and publishes it.

- [ ] **Step 1: Create `PricingOptions.cs`**

```csharp
namespace Pricing.Application.Pricing;

/// <summary>Configuration options for the pricing service.</summary>
public sealed class PricingOptions
{
    /// <summary>Gets the number of decimals FX conversion rounds to.</summary>
    public int RoundingDecimals { get; init; } = 2;

    /// <summary>Gets the midpoint rounding mode used for FX conversion.</summary>
    public MidpointRounding RoundingMode { get; init; } = MidpointRounding.ToEven;

    /// <summary>Gets the maximum number of tiers allowed on a single price.</summary>
    public int MaxTiersPerPrice { get; init; } = 20;
}
```

- [ ] **Step 2: Create `RateSnapshot.cs`**

```csharp
namespace Pricing.Application.Pricing;

/// <summary>A rate observation from an external provider.</summary>
/// <param name="FromCurrency">The source ISO currency.</param>
/// <param name="ToCurrency">The target ISO currency.</param>
/// <param name="Rate">The multiplicative rate.</param>
public sealed record RateSnapshot(string FromCurrency, string ToCurrency, decimal Rate);
```

- [ ] **Step 3: Create `IExchangeRateProvider.cs`**

```csharp
namespace Pricing.Application.Pricing;

/// <summary>
/// Seam for fetching exchange rates from an external source. v1 uses a no-op stub; a real
/// ECB/OXR adapter and a scheduled refresh can adopt this later without a domain change.
/// </summary>
public interface IExchangeRateProvider
{
    /// <summary>Gets the latest available rates.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The latest rate snapshots (empty when no external source is configured).</returns>
    Task<IReadOnlyList<RateSnapshot>> GetLatestAsync(CancellationToken ct);
}
```

- [ ] **Step 4: Create `PricingEventPublisher.cs`**

```csharp
using Pricing.Domain.DomainEvents;
using SharedKernel.Events;
using Wolverine;

namespace Pricing.Application.Pricing;

/// <summary>Publishes <see cref="PriceChanged"/> domain events as integration events after commit.</summary>
public static class PricingEventPublisher
{
    /// <summary>Maps and publishes each price-changed event.</summary>
    /// <param name="events">The captured domain events.</param>
    /// <param name="bus">The message bus.</param>
    /// <returns>A task representing the publish operations.</returns>
    public static async Task PublishAsync(IEnumerable<PriceChanged> events, IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(bus);

        foreach (PriceChanged evt in events)
        {
            await bus.PublishAsync(new PriceChangedIntegrationEvent
            {
                ProductId = evt.ProductId,
                PriceListId = evt.PriceListId,
                TenantId = evt.TenantId,
                Amount = evt.Amount,
                Currency = evt.Currency,
                EffectiveFrom = evt.EffectiveFrom,
                ChangeType = evt.ChangeType.ToString(),
            }).ConfigureAwait(false);
        }
    }
}
```

- [ ] **Step 5: Write the publisher test** `tests/unit/Pricing.UnitTests/PricingEventPublisherTests.cs`

```csharp
using NSubstitute;
using Pricing.Application.Pricing;
using Pricing.Domain.DomainEvents;
using Pricing.Domain.ValueObjects;
using SharedKernel.Events;
using Wolverine;
using Xunit;

namespace Pricing.UnitTests;

public sealed class PricingEventPublisherTests
{
    [Fact]
    public async Task PublishAsync_PublishesOneIntegrationEventPerDomainEvent()
    {
        var bus = Substitute.For<IMessageBus>();
        var evt = new PriceChanged(Guid.NewGuid(), Guid.NewGuid(), "tenant-1", 10m, "USD", DateTimeOffset.UtcNow, PriceChangeType.Upserted);

        await PricingEventPublisher.PublishAsync([evt], bus);

        await bus.Received(1).PublishAsync(Arg.Is<PriceChangedIntegrationEvent>(e =>
            e.ProductId == evt.ProductId && e.ChangeType == "Upserted" && e.Currency == "USD"));
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/unit/Pricing.UnitTests --filter PricingEventPublisherTests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/services/commerce/pricing/Pricing.Application/Pricing/PricingOptions.cs src/services/commerce/pricing/Pricing.Application/Pricing/IExchangeRateProvider.cs src/services/commerce/pricing/Pricing.Application/Pricing/RateSnapshot.cs src/services/commerce/pricing/Pricing.Application/Pricing/PricingEventPublisher.cs tests/unit/Pricing.UnitTests/PricingEventPublisherTests.cs
git commit -m "feat(pricing): options, FX provider seam, event publisher"
```

---

### Task 12: Price-list command handlers (Create / Update / Activate / Archive)

Each feature has `Features/{UseCase}/V1/{UseCase}Command.cs` + `{UseCase}Handler.cs`.

**Files:**
- Create: `Pricing.Application/Pricing/Features/CreatePriceList/V1/CreatePriceListCommand.cs` + `CreatePriceListHandler.cs`
- Create: `Pricing.Application/Pricing/Features/UpdatePriceList/V1/UpdatePriceListCommand.cs` + `UpdatePriceListHandler.cs`
- Create: `Pricing.Application/Pricing/Features/ActivatePriceList/V1/ActivatePriceListCommand.cs` + `ActivatePriceListHandler.cs`
- Create: `Pricing.Application/Pricing/Features/ArchivePriceList/V1/ArchivePriceListCommand.cs` + `ArchivePriceListHandler.cs`
- Test: `tests/unit/Pricing.UnitTests/PriceListHandlerTests.cs`

**Interfaces:**
- Consumes: `IGenericWriteRepository<PriceList, Guid>`, `IUnitOfWork`, `IMessageBus`, `ITenantInfo`, `PriceListByIdSpec`, `PriceListMapper`, `PricingEventPublisher`.
- Produces:
  - `CreatePriceListCommand(string Name, string? Description, string Currency, string? Country, Guid? CustomerGroupId, Guid? ChannelId, DateTimeOffset? ValidFrom, DateTimeOffset? ValidUntil) : ICommand<PriceListDto>` → handler returns `Task<PriceListDto>`.
  - `UpdatePriceListCommand(Guid Id, string Name, string? Description, string Currency, string? Country, Guid? CustomerGroupId, Guid? ChannelId, DateTimeOffset? ValidFrom, DateTimeOffset? ValidUntil) : ICommand<ErrorOr<PriceListDto>>` → `Task<ErrorOr<PriceListDto>>`.
  - `ActivatePriceListCommand(Guid Id) : ICommand<ErrorOr<PriceListDto>>` → `Task<ErrorOr<PriceListDto>>`.
  - `ArchivePriceListCommand(Guid Id) : ICommand<ErrorOr<PriceListDto>>` → `Task<ErrorOr<PriceListDto>>`.

- [ ] **Step 1: Write the failing handler test** `tests/unit/Pricing.UnitTests/PriceListHandlerTests.cs`

```csharp
using ErrorOr;
using Finbuckle.MultiTenant.Abstractions;
using NSubstitute;
using Pricing.Application.Pricing.Features.ActivatePriceList.V1;
using Pricing.Application.Pricing.Features.CreatePriceList.V1;
using Pricing.Domain.Entities;
using Pricing.Domain.ValueObjects;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;
using Xunit;

namespace Pricing.UnitTests;

public sealed class PriceListHandlerTests
{
    [Fact]
    public async Task Create_AddsDraftList_AndCommitsOnce()
    {
        var repo = Substitute.For<IGenericWriteRepository<PriceList, Guid>>();
        var uow = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IMessageBus>();
        var tenant = Substitute.For<ITenantInfo>();
        tenant.Id.Returns("tenant-1");

        var command = new CreatePriceListCommand("Retail", null, "USD", null, null, null, null, null);

        var dto = await CreatePriceListHandler.Handle(command, repo, uow, tenant, bus, CancellationToken.None);

        Assert.Equal("Draft", dto.Status);
        await repo.Received(1).AddAsync(Arg.Any<PriceList>(), Arg.Any<CancellationToken>());
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Activate_MissingList_ReturnsNotFound()
    {
        var repo = Substitute.For<IGenericWriteRepository<PriceList, Guid>>();
        var uow = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IMessageBus>();
        repo.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<PriceList>>(), true, Arg.Any<CancellationToken>())
            .Returns((PriceList?)null);

        var result = await ActivatePriceListHandler.Handle(new ActivatePriceListCommand(Guid.NewGuid()), repo, uow, bus, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task Activate_PublishesPriceChangedPerPrice()
    {
        var list = PriceList.Create("l", new PriceScope("USD", null, null, null), null, null, "tenant-1");
        list.AddOrUpdatePrice(Guid.NewGuid(), new Money(10m, "USD"), []);
        var repo = Substitute.For<IGenericWriteRepository<PriceList, Guid>>();
        var uow = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IMessageBus>();
        repo.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<PriceList>>(), true, Arg.Any<CancellationToken>())
            .Returns(list);

        var result = await ActivatePriceListHandler.Handle(new ActivatePriceListCommand(list.Id), repo, uow, bus, CancellationToken.None);

        Assert.False(result.IsError);
        await bus.Received(1).PublishAsync(Arg.Any<PriceChangedIntegrationEvent>());
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/unit/Pricing.UnitTests --filter PriceListHandlerTests`
Expected: FAIL — handlers do not exist.

- [ ] **Step 3: Create `CreatePriceListCommand.cs`**

```csharp
using Pricing.Application.Pricing.Responses;
using SharedKernel.Core.CQRS;

namespace Pricing.Application.Pricing.Features.CreatePriceList.V1;

/// <summary>Command that creates a new draft price list.</summary>
/// <param name="Name">The display name.</param>
/// <param name="Description">The optional description.</param>
/// <param name="Currency">The scope ISO currency.</param>
/// <param name="Country">The scope country, or null.</param>
/// <param name="CustomerGroupId">The scope customer group, or null.</param>
/// <param name="ChannelId">The scope channel, or null.</param>
/// <param name="ValidFrom">The inclusive validity start, or null.</param>
/// <param name="ValidUntil">The exclusive validity end, or null.</param>
public sealed record CreatePriceListCommand(
    string Name,
    string? Description,
    string Currency,
    string? Country,
    Guid? CustomerGroupId,
    Guid? ChannelId,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidUntil) : ICommand<PriceListDto>;
```

- [ ] **Step 4: Create `CreatePriceListHandler.cs`**

```csharp
using Finbuckle.MultiTenant.Abstractions;
using Pricing.Application.Pricing.Mapping;
using Pricing.Application.Pricing.Responses;
using Pricing.Domain.Entities;
using Pricing.Domain.ValueObjects;
using SharedKernel.Core.Database;
using Wolverine;

namespace Pricing.Application.Pricing.Features.CreatePriceList.V1;

/// <summary>Handles <see cref="CreatePriceListCommand"/>.</summary>
public static class CreatePriceListHandler
{
    /// <summary>Creates a draft price list and commits.</summary>
    /// <param name="command">The command.</param>
    /// <param name="repository">The write repository.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    /// <param name="tenant">The current tenant.</param>
    /// <param name="bus">The message bus (unused for draft; kept for signature symmetry).</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The created price list.</returns>
    public static async Task<PriceListDto> Handle(
        CreatePriceListCommand command,
        IGenericWriteRepository<PriceList, Guid> repository,
        IUnitOfWork unitOfWork,
        ITenantInfo tenant,
        IMessageBus bus,
        CancellationToken ct)
    {
        var scope = new PriceScope(command.Currency, command.Country, command.CustomerGroupId, command.ChannelId);
        var list = PriceList.Create(command.Name, scope, command.ValidFrom, command.ValidUntil, tenant.Id ?? string.Empty);
        list.UpdateDetails(command.Name, command.Description);

        await repository.AddAsync(list, ct).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return list.ToDto();
    }
}
```

- [ ] **Step 5: Create the Update/Activate/Archive commands**

`UpdatePriceList/V1/UpdatePriceListCommand.cs`:
```csharp
using ErrorOr;
using Pricing.Application.Pricing.Responses;
using SharedKernel.Core.CQRS;

namespace Pricing.Application.Pricing.Features.UpdatePriceList.V1;

/// <summary>Command that updates a price list's details, scope, and validity.</summary>
/// <param name="Id">The list identifier.</param>
/// <param name="Name">The new name.</param>
/// <param name="Description">The new description.</param>
/// <param name="Currency">The new scope currency.</param>
/// <param name="Country">The new scope country, or null.</param>
/// <param name="CustomerGroupId">The new scope customer group, or null.</param>
/// <param name="ChannelId">The new scope channel, or null.</param>
/// <param name="ValidFrom">The new inclusive validity start, or null.</param>
/// <param name="ValidUntil">The new exclusive validity end, or null.</param>
public sealed record UpdatePriceListCommand(
    Guid Id,
    string Name,
    string? Description,
    string Currency,
    string? Country,
    Guid? CustomerGroupId,
    Guid? ChannelId,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidUntil) : ICommand<ErrorOr<PriceListDto>>;
```

`ActivatePriceList/V1/ActivatePriceListCommand.cs`:
```csharp
using ErrorOr;
using Pricing.Application.Pricing.Responses;
using SharedKernel.Core.CQRS;

namespace Pricing.Application.Pricing.Features.ActivatePriceList.V1;

/// <summary>Command that activates a price list.</summary>
/// <param name="Id">The list identifier.</param>
public sealed record ActivatePriceListCommand(Guid Id) : ICommand<ErrorOr<PriceListDto>>;
```

`ArchivePriceList/V1/ArchivePriceListCommand.cs`:
```csharp
using ErrorOr;
using Pricing.Application.Pricing.Responses;
using SharedKernel.Core.CQRS;

namespace Pricing.Application.Pricing.Features.ArchivePriceList.V1;

/// <summary>Command that archives a price list.</summary>
/// <param name="Id">The list identifier.</param>
public sealed record ArchivePriceListCommand(Guid Id) : ICommand<ErrorOr<PriceListDto>>;
```

- [ ] **Step 6: Create the Update/Activate/Archive handlers** (all follow the same load-mutate-commit-publish shape)

`UpdatePriceList/V1/UpdatePriceListHandler.cs`:
```csharp
using ErrorOr;
using Pricing.Application.Pricing.Mapping;
using Pricing.Application.Pricing.ReadModels;
using Pricing.Application.Pricing.Responses;
using Pricing.Domain.DomainEvents;
using Pricing.Domain.Entities;
using Pricing.Domain.ValueObjects;
using SharedKernel.Core.Database;
using Wolverine;

namespace Pricing.Application.Pricing.Features.UpdatePriceList.V1;

/// <summary>Handles <see cref="UpdatePriceListCommand"/>.</summary>
public static class UpdatePriceListHandler
{
    /// <summary>Loads, updates details/scope/validity, commits, and publishes any effective changes.</summary>
    /// <param name="command">The command.</param>
    /// <param name="repository">The write repository.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    /// <param name="bus">The message bus.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The updated list, or a not-found error.</returns>
    public static async Task<ErrorOr<PriceListDto>> Handle(
        UpdatePriceListCommand command,
        IGenericWriteRepository<PriceList, Guid> repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct)
    {
        PriceList? list = await repository.FirstOrDefaultAsync(new PriceListByIdSpec(command.Id), enableTracking: true, ct).ConfigureAwait(false);
        if (list is null)
        {
            return Error.NotFound(description: $"Price list '{command.Id}' was not found.");
        }

        list.UpdateDetails(command.Name, command.Description);
        list.UpdateScope(new PriceScope(command.Currency, command.Country, command.CustomerGroupId, command.ChannelId));
        list.UpdateValidity(command.ValidFrom, command.ValidUntil);

        var events = list.DomainEvents.OfType<PriceChanged>().ToList();
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        await PricingEventPublisher.PublishAsync(events, bus).ConfigureAwait(false);
        return list.ToDto();
    }
}
```

`ActivatePriceList/V1/ActivatePriceListHandler.cs`:
```csharp
using ErrorOr;
using Pricing.Application.Pricing.Mapping;
using Pricing.Application.Pricing.ReadModels;
using Pricing.Application.Pricing.Responses;
using Pricing.Domain.DomainEvents;
using Pricing.Domain.Entities;
using SharedKernel.Core.Database;
using Wolverine;

namespace Pricing.Application.Pricing.Features.ActivatePriceList.V1;

/// <summary>Handles <see cref="ActivatePriceListCommand"/>.</summary>
public static class ActivatePriceListHandler
{
    /// <summary>Loads, activates, commits, and publishes an Upserted event per price.</summary>
    /// <param name="command">The command.</param>
    /// <param name="repository">The write repository.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    /// <param name="bus">The message bus.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The activated list, or a not-found error.</returns>
    public static async Task<ErrorOr<PriceListDto>> Handle(
        ActivatePriceListCommand command,
        IGenericWriteRepository<PriceList, Guid> repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct)
    {
        PriceList? list = await repository.FirstOrDefaultAsync(new PriceListByIdSpec(command.Id), enableTracking: true, ct).ConfigureAwait(false);
        if (list is null)
        {
            return Error.NotFound(description: $"Price list '{command.Id}' was not found.");
        }

        list.Activate();
        var events = list.DomainEvents.OfType<PriceChanged>().ToList();
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        await PricingEventPublisher.PublishAsync(events, bus).ConfigureAwait(false);
        return list.ToDto();
    }
}
```

`ArchivePriceList/V1/ArchivePriceListHandler.cs` — identical to Activate but calls `list.Archive()` (emits Removed). Copy the Activate handler, rename the type/namespace to `ArchivePriceList`, and replace `list.Activate();` with `list.Archive();`.

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/unit/Pricing.UnitTests --filter PriceListHandlerTests`
Expected: PASS (3 tests).

- [ ] **Step 8: Commit**

```bash
git add src/services/commerce/pricing/Pricing.Application/Pricing/Features tests/unit/Pricing.UnitTests/PriceListHandlerTests.cs
git commit -m "feat(pricing): price-list command handlers"
```

---

### Task 13: Price command handlers (AddOrUpdatePrice / RemovePrice)

**Files:**
- Create: `Pricing.Application/Pricing/Features/AddOrUpdatePrice/V1/AddOrUpdatePriceCommand.cs` + `AddOrUpdatePriceHandler.cs`
- Create: `Pricing.Application/Pricing/Features/AddOrUpdatePrice/V1/PriceTierInput.cs`
- Create: `Pricing.Application/Pricing/Features/RemovePrice/V1/RemovePriceCommand.cs` + `RemovePriceHandler.cs`
- Test: `tests/unit/Pricing.UnitTests/PriceCommandHandlerTests.cs`

**Interfaces:**
- Produces:
  - `PriceTierInput(int MinQuantity, decimal Amount)` — plain input record (currency comes from the list).
  - `AddOrUpdatePriceCommand(Guid PriceListId, Guid ProductId, decimal Amount, IReadOnlyList<PriceTierInput> Tiers) : ICommand<ErrorOr<PriceListDto>>`.
  - `RemovePriceCommand(Guid PriceListId, Guid ProductId) : ICommand<ErrorOr<PriceListDto>>`.
  - Handlers load the list (tracking), map inputs to `Money`/`PriceTier` using the list's scope currency, mutate, commit, publish, return the list DTO or not-found.

- [ ] **Step 1: Write the failing test** `tests/unit/Pricing.UnitTests/PriceCommandHandlerTests.cs`

```csharp
using ErrorOr;
using NSubstitute;
using Pricing.Application.Pricing.Features.AddOrUpdatePrice.V1;
using Pricing.Domain.Entities;
using Pricing.Domain.ValueObjects;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;
using Xunit;

namespace Pricing.UnitTests;

public sealed class PriceCommandHandlerTests
{
    [Fact]
    public async Task AddOrUpdatePrice_OnActiveList_AddsPrice_AndPublishes()
    {
        var list = PriceList.Create("l", new PriceScope("USD", null, null, null), null, null, "tenant-1");
        list.Activate();
        var repo = Substitute.For<IGenericWriteRepository<PriceList, Guid>>();
        var uow = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IMessageBus>();
        repo.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<PriceList>>(), true, Arg.Any<CancellationToken>())
            .Returns(list);

        var command = new AddOrUpdatePriceCommand(list.Id, Guid.NewGuid(), 10m, [new PriceTierInput(10, 8m)]);
        var result = await AddOrUpdatePriceHandler.Handle(command, repo, uow, bus, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Single(list.Prices);
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await bus.Received(1).PublishAsync(Arg.Any<PriceChangedIntegrationEvent>());
    }

    [Fact]
    public async Task AddOrUpdatePrice_MissingList_ReturnsNotFound()
    {
        var repo = Substitute.For<IGenericWriteRepository<PriceList, Guid>>();
        var uow = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IMessageBus>();
        repo.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<PriceList>>(), true, Arg.Any<CancellationToken>())
            .Returns((PriceList?)null);

        var result = await AddOrUpdatePriceHandler.Handle(
            new AddOrUpdatePriceCommand(Guid.NewGuid(), Guid.NewGuid(), 10m, []), repo, uow, bus, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/unit/Pricing.UnitTests --filter PriceCommandHandlerTests`
Expected: FAIL — handlers do not exist.

- [ ] **Step 3: Create `PriceTierInput.cs`**

```csharp
namespace Pricing.Application.Pricing.Features.AddOrUpdatePrice.V1;

/// <summary>A quantity tier supplied on a price command (currency is the list's scope currency).</summary>
/// <param name="MinQuantity">The minimum quantity (>= 1).</param>
/// <param name="Amount">The unit amount.</param>
public sealed record PriceTierInput(int MinQuantity, decimal Amount);
```

- [ ] **Step 4: Create `AddOrUpdatePriceCommand.cs`**

```csharp
using ErrorOr;
using Pricing.Application.Pricing.Responses;
using SharedKernel.Core.CQRS;

namespace Pricing.Application.Pricing.Features.AddOrUpdatePrice.V1;

/// <summary>Command that adds or updates a product's price within a list.</summary>
/// <param name="PriceListId">The owning price list.</param>
/// <param name="ProductId">The product identifier.</param>
/// <param name="Amount">The base unit amount (in the list's currency).</param>
/// <param name="Tiers">The quantity tiers.</param>
public sealed record AddOrUpdatePriceCommand(
    Guid PriceListId,
    Guid ProductId,
    decimal Amount,
    IReadOnlyList<PriceTierInput> Tiers) : ICommand<ErrorOr<PriceListDto>>;
```

- [ ] **Step 5: Create `AddOrUpdatePriceHandler.cs`**

```csharp
using ErrorOr;
using Pricing.Application.Pricing.Mapping;
using Pricing.Application.Pricing.ReadModels;
using Pricing.Application.Pricing.Responses;
using Pricing.Domain.DomainEvents;
using Pricing.Domain.Entities;
using Pricing.Domain.ValueObjects;
using SharedKernel.Core.Database;
using Wolverine;

namespace Pricing.Application.Pricing.Features.AddOrUpdatePrice.V1;

/// <summary>Handles <see cref="AddOrUpdatePriceCommand"/>.</summary>
public static class AddOrUpdatePriceHandler
{
    /// <summary>Loads the list, upserts the product's price, commits, and publishes effective changes.</summary>
    /// <param name="command">The command.</param>
    /// <param name="repository">The write repository.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    /// <param name="bus">The message bus.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The updated list, or a not-found error.</returns>
    public static async Task<ErrorOr<PriceListDto>> Handle(
        AddOrUpdatePriceCommand command,
        IGenericWriteRepository<PriceList, Guid> repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct)
    {
        PriceList? list = await repository.FirstOrDefaultAsync(new PriceListByIdSpec(command.PriceListId), enableTracking: true, ct).ConfigureAwait(false);
        if (list is null)
        {
            return Error.NotFound(description: $"Price list '{command.PriceListId}' was not found.");
        }

        string currency = list.Scope.Currency;
        var amount = new Money(command.Amount, currency);
        var tiers = command.Tiers
            .Select(tier => new PriceTier(tier.MinQuantity, new Money(tier.Amount, currency)))
            .ToList();

        list.AddOrUpdatePrice(command.ProductId, amount, tiers);

        var events = list.DomainEvents.OfType<PriceChanged>().ToList();
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        await PricingEventPublisher.PublishAsync(events, bus).ConfigureAwait(false);
        return list.ToDto();
    }
}
```

- [ ] **Step 6: Create `RemovePriceCommand.cs` + `RemovePriceHandler.cs`**

`RemovePrice/V1/RemovePriceCommand.cs`:
```csharp
using ErrorOr;
using Pricing.Application.Pricing.Responses;
using SharedKernel.Core.CQRS;

namespace Pricing.Application.Pricing.Features.RemovePrice.V1;

/// <summary>Command that removes a product's price from a list.</summary>
/// <param name="PriceListId">The owning price list.</param>
/// <param name="ProductId">The product identifier.</param>
public sealed record RemovePriceCommand(Guid PriceListId, Guid ProductId) : ICommand<ErrorOr<PriceListDto>>;
```

`RemovePrice/V1/RemovePriceHandler.cs`:
```csharp
using ErrorOr;
using Pricing.Application.Pricing.Mapping;
using Pricing.Application.Pricing.ReadModels;
using Pricing.Application.Pricing.Responses;
using Pricing.Domain.DomainEvents;
using Pricing.Domain.Entities;
using SharedKernel.Core.Database;
using Wolverine;

namespace Pricing.Application.Pricing.Features.RemovePrice.V1;

/// <summary>Handles <see cref="RemovePriceCommand"/>.</summary>
public static class RemovePriceHandler
{
    /// <summary>Loads the list, removes the product's price, commits, and publishes effective changes.</summary>
    /// <param name="command">The command.</param>
    /// <param name="repository">The write repository.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    /// <param name="bus">The message bus.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The updated list, or a not-found error.</returns>
    public static async Task<ErrorOr<PriceListDto>> Handle(
        RemovePriceCommand command,
        IGenericWriteRepository<PriceList, Guid> repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct)
    {
        PriceList? list = await repository.FirstOrDefaultAsync(new PriceListByIdSpec(command.PriceListId), enableTracking: true, ct).ConfigureAwait(false);
        if (list is null)
        {
            return Error.NotFound(description: $"Price list '{command.PriceListId}' was not found.");
        }

        list.RemovePrice(command.ProductId);

        var events = list.DomainEvents.OfType<PriceChanged>().ToList();
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        await PricingEventPublisher.PublishAsync(events, bus).ConfigureAwait(false);
        return list.ToDto();
    }
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/unit/Pricing.UnitTests --filter PriceCommandHandlerTests`
Expected: PASS (2 tests).

- [ ] **Step 8: Commit**

```bash
git add src/services/commerce/pricing/Pricing.Application/Pricing/Features tests/unit/Pricing.UnitTests/PriceCommandHandlerTests.cs
git commit -m "feat(pricing): price add/update/remove command handlers"
```

---

### Task 14: Exchange-rate command handlers (Set / Remove)

**Files:**
- Create: `Pricing.Application/Pricing/Features/SetExchangeRate/V1/SetExchangeRateCommand.cs` + `SetExchangeRateHandler.cs`
- Create: `Pricing.Application/Pricing/Features/RemoveExchangeRate/V1/RemoveExchangeRateCommand.cs` + `RemoveExchangeRateHandler.cs`
- Test: `tests/unit/Pricing.UnitTests/ExchangeRateHandlerTests.cs`

**Interfaces:**
- Produces:
  - `SetExchangeRateCommand(string FromCurrency, string ToCurrency, decimal Rate, DateTimeOffset? ValidFrom, DateTimeOffset? ValidUntil) : ICommand<ExchangeRateDto>` — upserts by pair.
  - `RemoveExchangeRateCommand(string FromCurrency, string ToCurrency) : ICommand<ErrorOr<Unit>>` — returns `Error.NotFound` if absent, else `Result.Success`.

- [ ] **Step 1: Write the failing test** `tests/unit/Pricing.UnitTests/ExchangeRateHandlerTests.cs`

```csharp
using ErrorOr;
using Finbuckle.MultiTenant.Abstractions;
using NSubstitute;
using Pricing.Application.Pricing.Features.SetExchangeRate.V1;
using Pricing.Domain.Entities;
using SharedKernel.Core.Database;
using Xunit;

namespace Pricing.UnitTests;

public sealed class ExchangeRateHandlerTests
{
    [Fact]
    public async Task Set_NewPair_CreatesRate()
    {
        var repo = Substitute.For<IGenericWriteRepository<ExchangeRate, Guid>>();
        var uow = Substitute.For<IUnitOfWork>();
        var tenant = Substitute.For<ITenantInfo>();
        tenant.Id.Returns("tenant-1");
        repo.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<ExchangeRate>>(), true, Arg.Any<CancellationToken>())
            .Returns((ExchangeRate?)null);

        var dto = await SetExchangeRateHandler.Handle(
            new SetExchangeRateCommand("USD", "EUR", 0.9m, null, null), repo, uow, tenant, CancellationToken.None);

        Assert.Equal(0.9m, dto.Rate);
        await repo.Received(1).AddAsync(Arg.Any<ExchangeRate>(), Arg.Any<CancellationToken>());
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Set_ExistingPair_UpdatesInPlace()
    {
        var existing = ExchangeRate.Create("USD", "EUR", 0.8m, null, null, "tenant-1");
        var repo = Substitute.For<IGenericWriteRepository<ExchangeRate, Guid>>();
        var uow = Substitute.For<IUnitOfWork>();
        var tenant = Substitute.For<ITenantInfo>();
        tenant.Id.Returns("tenant-1");
        repo.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<ExchangeRate>>(), true, Arg.Any<CancellationToken>())
            .Returns(existing);

        var dto = await SetExchangeRateHandler.Handle(
            new SetExchangeRateCommand("USD", "EUR", 0.95m, null, null), repo, uow, tenant, CancellationToken.None);

        Assert.Equal(0.95m, dto.Rate);
        await repo.DidNotReceive().AddAsync(Arg.Any<ExchangeRate>(), Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/unit/Pricing.UnitTests --filter ExchangeRateHandlerTests`
Expected: FAIL — handlers do not exist.

- [ ] **Step 3: Create `SetExchangeRateCommand.cs`**

```csharp
using Pricing.Application.Pricing.Responses;
using SharedKernel.Core.CQRS;

namespace Pricing.Application.Pricing.Features.SetExchangeRate.V1;

/// <summary>Command that creates or updates the exchange rate for a currency pair.</summary>
/// <param name="FromCurrency">The source ISO currency.</param>
/// <param name="ToCurrency">The target ISO currency.</param>
/// <param name="Rate">The positive multiplicative rate.</param>
/// <param name="ValidFrom">The inclusive validity start, or null.</param>
/// <param name="ValidUntil">The exclusive validity end, or null.</param>
public sealed record SetExchangeRateCommand(
    string FromCurrency,
    string ToCurrency,
    decimal Rate,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidUntil) : ICommand<ExchangeRateDto>;
```

- [ ] **Step 4: Create `SetExchangeRateHandler.cs`**

```csharp
using Finbuckle.MultiTenant.Abstractions;
using Pricing.Application.Pricing.Mapping;
using Pricing.Application.Pricing.ReadModels;
using Pricing.Application.Pricing.Responses;
using Pricing.Domain.Entities;
using SharedKernel.Core.Database;

namespace Pricing.Application.Pricing.Features.SetExchangeRate.V1;

/// <summary>Handles <see cref="SetExchangeRateCommand"/> with upsert-by-pair semantics.</summary>
public static class SetExchangeRateHandler
{
    /// <summary>Creates or updates the rate for the pair and commits.</summary>
    /// <param name="command">The command.</param>
    /// <param name="repository">The write repository.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    /// <param name="tenant">The current tenant.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The saved exchange rate.</returns>
    public static async Task<ExchangeRateDto> Handle(
        SetExchangeRateCommand command,
        IGenericWriteRepository<ExchangeRate, Guid> repository,
        IUnitOfWork unitOfWork,
        ITenantInfo tenant,
        CancellationToken ct)
    {
        ExchangeRate? rate = await repository.FirstOrDefaultAsync(
            new ExchangeRateByPairSpec(command.FromCurrency, command.ToCurrency), enableTracking: true, ct).ConfigureAwait(false);

        if (rate is null)
        {
            rate = ExchangeRate.Create(command.FromCurrency, command.ToCurrency, command.Rate, command.ValidFrom, command.ValidUntil, tenant.Id ?? string.Empty);
            await repository.AddAsync(rate, ct).ConfigureAwait(false);
        }
        else
        {
            rate.UpdateRate(command.Rate);
            rate.UpdateValidity(command.ValidFrom, command.ValidUntil);
        }

        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return rate.ToDto();
    }
}
```

- [ ] **Step 5: Create `RemoveExchangeRateCommand.cs` + `RemoveExchangeRateHandler.cs`**

`RemoveExchangeRate/V1/RemoveExchangeRateCommand.cs`:
```csharp
using ErrorOr;
using SharedKernel.Core.CQRS;

namespace Pricing.Application.Pricing.Features.RemoveExchangeRate.V1;

/// <summary>Command that removes the exchange rate for a currency pair.</summary>
/// <param name="FromCurrency">The source ISO currency.</param>
/// <param name="ToCurrency">The target ISO currency.</param>
public sealed record RemoveExchangeRateCommand(string FromCurrency, string ToCurrency) : ICommand<ErrorOr<Success>>;
```

`RemoveExchangeRate/V1/RemoveExchangeRateHandler.cs`:
```csharp
using ErrorOr;
using Pricing.Application.Pricing.ReadModels;
using Pricing.Domain.Entities;
using SharedKernel.Core.Database;

namespace Pricing.Application.Pricing.Features.RemoveExchangeRate.V1;

/// <summary>Handles <see cref="RemoveExchangeRateCommand"/>.</summary>
public static class RemoveExchangeRateHandler
{
    /// <summary>Removes the rate for the pair, or returns not-found.</summary>
    /// <param name="command">The command.</param>
    /// <param name="repository">The write repository.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>Success, or a not-found error.</returns>
    public static async Task<ErrorOr<Success>> Handle(
        RemoveExchangeRateCommand command,
        IGenericWriteRepository<ExchangeRate, Guid> repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        ExchangeRate? rate = await repository.FirstOrDefaultAsync(
            new ExchangeRateByPairSpec(command.FromCurrency, command.ToCurrency), enableTracking: true, ct).ConfigureAwait(false);
        if (rate is null)
        {
            return Error.NotFound(description: $"Exchange rate '{command.FromCurrency}->{command.ToCurrency}' was not found.");
        }

        repository.Delete(rate);
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success;
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/unit/Pricing.UnitTests --filter ExchangeRateHandlerTests`
Expected: PASS (2 tests).

- [ ] **Step 7: Commit**

```bash
git add src/services/commerce/pricing/Pricing.Application/Pricing/Features tests/unit/Pricing.UnitTests/ExchangeRateHandlerTests.cs
git commit -m "feat(pricing): exchange-rate command handlers"
```

---

### Task 15: `ResolvePrice` query + `GetPriceList` + `ListPriceLists`

**Files:**
- Create: `Pricing.Application/Pricing/ReadModels/AllPriceListsSpec.cs`
- Create: `Pricing.Application/Pricing/Features/ResolvePrice/V1/ResolvePriceQuery.cs` + `ResolvePriceHandler.cs`
- Create: `Pricing.Application/Pricing/Features/GetPriceList/V1/GetPriceListQuery.cs` + `GetPriceListHandler.cs`
- Create: `Pricing.Application/Pricing/Features/ListPriceLists/V1/ListPriceListsQuery.cs` + `ListPriceListsHandler.cs`
- Test: `tests/unit/Pricing.UnitTests/ResolvePriceHandlerTests.cs`

**Interfaces:**
- Consumes: `IGenericReadRepository<Price, Guid>`, `IGenericReadRepository<ExchangeRate, Guid>`, `IGenericReadRepository<PriceList, Guid>`, `IOptions<PricingOptions>`, `PricesByProductSpec`, `ExchangeRateByPairSpec`, `PriceListByIdSpec`, `PriceResolutionService`, `CurrencyConverter`.
- Produces:
  - `ResolvePriceQuery(Guid ProductId, string Currency, int Quantity, string? Country, Guid? CustomerGroupId, Guid? ChannelId, DateTimeOffset? At) : IQuery<ResolvedPriceDto>` → `Task<ErrorOr<ResolvedPriceDto>>`. `Error.NotFound` when no price applies; `Error.Failure` (→ 422) when a foreign winner has no valid rate.
  - `GetPriceListQuery(Guid Id) : IQuery<PriceListDto>` → `Task<ErrorOr<PriceListDto>>`.
  - `ListPriceListsQuery : IQuery<IReadOnlyList<PriceListDto>>` → `Task<IReadOnlyList<PriceListDto>>`.

- [ ] **Step 1: Write the failing test** `tests/unit/Pricing.UnitTests/ResolvePriceHandlerTests.cs`

```csharp
using ErrorOr;
using Microsoft.Extensions.Options;
using NSubstitute;
using Pricing.Application.Pricing;
using Pricing.Application.Pricing.Features.ResolvePrice.V1;
using Pricing.Domain.Entities;
using Pricing.Domain.ValueObjects;
using SharedKernel.Core.Database;
using Xunit;

namespace Pricing.UnitTests;

public sealed class ResolvePriceHandlerTests
{
    private static readonly Guid Product = Guid.NewGuid();

    private static Price ActivePrice(PriceScope scope, decimal amount)
    {
        var list = PriceList.Create("l", scope, null, null, "tenant-1");
        list.AddOrUpdatePrice(Product, new Money(amount, scope.Currency), []);
        list.Activate();
        Price price = System.Linq.Enumerable.Single(list.Prices);
        typeof(Price).GetProperty(nameof(Price.PriceList))!.SetValue(price, list);
        return price;
    }

    private static (IGenericReadRepository<Price, Guid> Prices, IGenericReadRepository<ExchangeRate, Guid> Rates) Repos(
        IReadOnlyList<Price> prices, ExchangeRate? rate = null)
    {
        var priceRepo = Substitute.For<IGenericReadRepository<Price, Guid>>();
        priceRepo.ListAsync(Arg.Any<Ardalis.Specification.ISpecification<Price>>(), Arg.Any<CancellationToken>()).Returns(prices);
        var rateRepo = Substitute.For<IGenericReadRepository<ExchangeRate, Guid>>();
        rateRepo.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<ExchangeRate>>(), Arg.Any<CancellationToken>()).Returns(rate);
        return (priceRepo, rateRepo);
    }

    private static IOptions<PricingOptions> Options() => Microsoft.Extensions.Options.Options.Create(new PricingOptions());

    [Fact]
    public async Task Resolve_NativeCurrency_ReturnsUnconverted()
    {
        var price = ActivePrice(new PriceScope("USD", null, null, null), 10m);
        var (prices, rates) = Repos([price]);

        var result = await ResolvePriceHandler.Handle(
            new ResolvePriceQuery(Product, "USD", 1, null, null, null, null), prices, rates, Options(), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.False(result.Value.Converted);
        Assert.Equal(10m, result.Value.UnitAmount);
    }

    [Fact]
    public async Task Resolve_NoPrice_ReturnsNotFound()
    {
        var (prices, rates) = Repos([]);

        var result = await ResolvePriceHandler.Handle(
            new ResolvePriceQuery(Product, "USD", 1, null, null, null, null), prices, rates, Options(), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task Resolve_ForeignWinner_NoRate_ReturnsFailure()
    {
        var price = ActivePrice(new PriceScope("EUR", null, null, null), 10m);
        var (prices, rates) = Repos([price], rate: null);

        var result = await ResolvePriceHandler.Handle(
            new ResolvePriceQuery(Product, "USD", 1, null, null, null, null), prices, rates, Options(), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Failure, result.FirstError.Type);
    }

    [Fact]
    public async Task Resolve_ForeignWinner_WithRate_Converts()
    {
        var price = ActivePrice(new PriceScope("EUR", null, null, null), 10m);
        var rate = ExchangeRate.Create("EUR", "USD", 1.1m, null, null, "tenant-1");
        var (prices, rates) = Repos([price], rate);

        var result = await ResolvePriceHandler.Handle(
            new ResolvePriceQuery(Product, "USD", 1, null, null, null, null), prices, rates, Options(), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.True(result.Value.Converted);
        Assert.Equal(11.00m, result.Value.UnitAmount);
        Assert.Equal(1.1m, result.Value.RateApplied);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/unit/Pricing.UnitTests --filter ResolvePriceHandlerTests`
Expected: FAIL — query/handler do not exist.

- [ ] **Step 3: Create `AllPriceListsSpec.cs`**

```csharp
using Ardalis.Specification;
using Pricing.Domain.Entities;

namespace Pricing.Application.Pricing.ReadModels;

/// <summary>Selects all price lists (ordered by name), including their prices.</summary>
public sealed class AllPriceListsSpec : Specification<PriceList>
{
    /// <summary>Initializes a new instance of the <see cref="AllPriceListsSpec"/> class.</summary>
    public AllPriceListsSpec() =>
        Query.Include(list => list.Prices).OrderBy(list => list.Name);
}
```

- [ ] **Step 4: Create `ResolvePriceQuery.cs`**

```csharp
using Pricing.Application.Pricing.Responses;
using SharedKernel.Core.CQRS;

namespace Pricing.Application.Pricing.Features.ResolvePrice.V1;

/// <summary>Query that resolves the effective price for a product in a request context.</summary>
/// <param name="ProductId">The product identifier.</param>
/// <param name="Currency">The requested ISO currency.</param>
/// <param name="Quantity">The requested quantity (>= 1).</param>
/// <param name="Country">The optional request country.</param>
/// <param name="CustomerGroupId">The optional request customer group.</param>
/// <param name="ChannelId">The optional request channel.</param>
/// <param name="At">The optional resolution moment (defaults to now).</param>
public sealed record ResolvePriceQuery(
    Guid ProductId,
    string Currency,
    int Quantity,
    string? Country,
    Guid? CustomerGroupId,
    Guid? ChannelId,
    DateTimeOffset? At) : IQuery<ResolvedPriceDto>;
```

- [ ] **Step 5: Create `ResolvePriceHandler.cs`**

```csharp
using ErrorOr;
using Microsoft.Extensions.Options;
using Pricing.Application.Pricing.ReadModels;
using Pricing.Application.Pricing.Responses;
using Pricing.Domain.Entities;
using Pricing.Domain.Services;
using Pricing.Domain.ValueObjects;
using SharedKernel.Core.Database;

namespace Pricing.Application.Pricing.Features.ResolvePrice.V1;

/// <summary>Handles <see cref="ResolvePriceQuery"/>: selects the best price and applies FX when needed.</summary>
public static class ResolvePriceHandler
{
    /// <summary>Resolves the effective price, converting cross-currency winners via a stored rate.</summary>
    /// <param name="query">The query.</param>
    /// <param name="prices">The price read repository.</param>
    /// <param name="rates">The exchange-rate read repository.</param>
    /// <param name="options">The pricing options (rounding).</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The resolved price, a not-found error, or a failure when no conversion rate exists.</returns>
    public static async Task<ErrorOr<ResolvedPriceDto>> Handle(
        ResolvePriceQuery query,
        IGenericReadRepository<Price, Guid> prices,
        IGenericReadRepository<ExchangeRate, Guid> rates,
        IOptions<PricingOptions> options,
        CancellationToken ct)
    {
        DateTimeOffset at = query.At ?? DateTimeOffset.UtcNow;
        var context = new PriceResolutionContext(query.Currency, query.Quantity, query.Country, query.CustomerGroupId, query.ChannelId, at);

        IReadOnlyList<Price> candidates = await prices.ListAsync(new PricesByProductSpec(query.ProductId), ct).ConfigureAwait(false);

        ResolvedSelection? selection = PriceResolutionService.SelectBest(candidates, context);
        if (selection is null)
        {
            return Error.NotFound(description: $"No applicable price for product '{query.ProductId}' in '{query.Currency}'.");
        }

        Money unit = selection.UnitAmount;
        if (string.Equals(unit.Currency, query.Currency, StringComparison.OrdinalIgnoreCase))
        {
            return new ResolvedPriceDto(query.ProductId, unit.Amount, query.Currency, selection.Price.PriceListId, Converted: false, RateApplied: null);
        }

        ExchangeRate? rate = await rates.FirstOrDefaultAsync(new ExchangeRateByPairSpec(unit.Currency, query.Currency), ct).ConfigureAwait(false);
        if (rate is null || !rate.IsValidAt(at))
        {
            return Error.Failure(description: $"No conversion rate from '{unit.Currency}' to '{query.Currency}'.");
        }

        PricingOptions opts = options.Value;
        Money converted = CurrencyConverter.Convert(unit, query.Currency, rate.Rate, opts.RoundingDecimals, opts.RoundingMode);
        return new ResolvedPriceDto(query.ProductId, converted.Amount, query.Currency, selection.Price.PriceListId, Converted: true, RateApplied: rate.Rate);
    }
}
```

- [ ] **Step 6: Create `GetPriceListQuery.cs` + `GetPriceListHandler.cs`**

`GetPriceList/V1/GetPriceListQuery.cs`:
```csharp
using Pricing.Application.Pricing.Responses;
using SharedKernel.Core.CQRS;

namespace Pricing.Application.Pricing.Features.GetPriceList.V1;

/// <summary>Query that retrieves a single price list by identifier.</summary>
/// <param name="Id">The price list identifier.</param>
public sealed record GetPriceListQuery(Guid Id) : IQuery<PriceListDto>;
```

`GetPriceList/V1/GetPriceListHandler.cs`:
```csharp
using ErrorOr;
using Pricing.Application.Pricing.Mapping;
using Pricing.Application.Pricing.ReadModels;
using Pricing.Application.Pricing.Responses;
using Pricing.Domain.Entities;
using SharedKernel.Core.Database;

namespace Pricing.Application.Pricing.Features.GetPriceList.V1;

/// <summary>Handles <see cref="GetPriceListQuery"/>.</summary>
public static class GetPriceListHandler
{
    /// <summary>Loads a price list or returns not-found.</summary>
    /// <param name="query">The query.</param>
    /// <param name="repository">The read repository.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The list DTO or a not-found error.</returns>
    public static async Task<ErrorOr<PriceListDto>> Handle(
        GetPriceListQuery query,
        IGenericReadRepository<PriceList, Guid> repository,
        CancellationToken ct)
    {
        PriceList? list = await repository.FirstOrDefaultAsync(new PriceListByIdSpec(query.Id), ct).ConfigureAwait(false);
        return list is null
            ? Error.NotFound(description: $"Price list '{query.Id}' was not found.")
            : list.ToDto();
    }
}
```

- [ ] **Step 7: Create `ListPriceListsQuery.cs` + `ListPriceListsHandler.cs`**

`ListPriceLists/V1/ListPriceListsQuery.cs`:
```csharp
using Pricing.Application.Pricing.Responses;
using SharedKernel.Core.CQRS;

namespace Pricing.Application.Pricing.Features.ListPriceLists.V1;

/// <summary>Query that lists all price lists for the tenant.</summary>
public sealed record ListPriceListsQuery : IQuery<IReadOnlyList<PriceListDto>>;
```

`ListPriceLists/V1/ListPriceListsHandler.cs`:
```csharp
using Pricing.Application.Pricing.Mapping;
using Pricing.Application.Pricing.ReadModels;
using Pricing.Application.Pricing.Responses;
using Pricing.Domain.Entities;
using SharedKernel.Core.Database;

namespace Pricing.Application.Pricing.Features.ListPriceLists.V1;

/// <summary>Handles <see cref="ListPriceListsQuery"/>.</summary>
public static class ListPriceListsHandler
{
    /// <summary>Lists all price lists mapped to DTOs.</summary>
    /// <param name="query">The query.</param>
    /// <param name="repository">The read repository.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The price lists.</returns>
    public static async Task<IReadOnlyList<PriceListDto>> Handle(
        ListPriceListsQuery query,
        IGenericReadRepository<PriceList, Guid> repository,
        CancellationToken ct)
    {
        IReadOnlyList<PriceList> lists = await repository.ListAsync(new AllPriceListsSpec(), ct).ConfigureAwait(false);
        return lists.Select(list => list.ToDto()).ToList();
    }
}
```

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test tests/unit/Pricing.UnitTests --filter ResolvePriceHandlerTests`
Expected: PASS (4 tests). Then run the whole unit suite: `dotnet test tests/unit/Pricing.UnitTests` — all green.

- [ ] **Step 9: Commit**

```bash
git add src/services/commerce/pricing/Pricing.Application/Pricing tests/unit/Pricing.UnitTests/ResolvePriceHandlerTests.cs
git commit -m "feat(pricing): resolve-price query and price-list read queries"
```

---

### Task 16: Host wiring — `Program.cs`, endpoints, config, FX provider stub

**ErrorOr mapping note:** handlers that return `ErrorOr<T>` are unwrapped centrally by `AddTeckBehaviors()` (Wolverine middleware) into `T` (throwing a mapped exception on error → ProblemDetails), exactly as `Order.Host`'s `GetOrderEndpoint` calls `InvokeAsync<OrderDto>` against an `ErrorOr<OrderDto>` handler. So endpoints call `InvokeAsync<Dto>` and never hand-map errors.

**Files:**
- Create: `Pricing.Application/Pricing/Features/ListExchangeRates/V1/ListExchangeRatesQuery.cs` + `ListExchangeRatesHandler.cs`
- Create: `Pricing.Application/Pricing/ReadModels/AllExchangeRatesSpec.cs`
- Replace: `Pricing.Host/Program.cs`
- Create: `Pricing.Host/Program.Public.cs`
- Create: `Pricing.Host/Infrastructure/ExchangeRateProviderStub.cs`
- Create: the 12 endpoint files under `Pricing.Host/Endpoints/Pricing/` (endpoint + request + validator where a body/params exist)

**Interfaces:**
- Produces: `ListExchangeRatesQuery : IQuery<IReadOnlyList<ExchangeRateDto>>`; `ExchangeRateProviderStub : IExchangeRateProvider`; the HTTP surface in the endpoint table below.

- [ ] **Step 1: Create the list-exchange-rates read query + spec** (Application layer)

`Pricing/ReadModels/AllExchangeRatesSpec.cs`:
```csharp
using Ardalis.Specification;
using Pricing.Domain.Entities;

namespace Pricing.Application.Pricing.ReadModels;

/// <summary>Selects all exchange rates ordered by currency pair.</summary>
public sealed class AllExchangeRatesSpec : Specification<ExchangeRate>
{
    /// <summary>Initializes a new instance of the <see cref="AllExchangeRatesSpec"/> class.</summary>
    public AllExchangeRatesSpec() =>
        Query.OrderBy(rate => rate.FromCurrency).ThenBy(rate => rate.ToCurrency);
}
```

`Pricing/Features/ListExchangeRates/V1/ListExchangeRatesQuery.cs`:
```csharp
using Pricing.Application.Pricing.Responses;
using SharedKernel.Core.CQRS;

namespace Pricing.Application.Pricing.Features.ListExchangeRates.V1;

/// <summary>Query that lists all exchange rates for the tenant.</summary>
public sealed record ListExchangeRatesQuery : IQuery<IReadOnlyList<ExchangeRateDto>>;
```

`Pricing/Features/ListExchangeRates/V1/ListExchangeRatesHandler.cs`:
```csharp
using Pricing.Application.Pricing.Mapping;
using Pricing.Application.Pricing.ReadModels;
using Pricing.Application.Pricing.Responses;
using Pricing.Domain.Entities;
using SharedKernel.Core.Database;

namespace Pricing.Application.Pricing.Features.ListExchangeRates.V1;

/// <summary>Handles <see cref="ListExchangeRatesQuery"/>.</summary>
public static class ListExchangeRatesHandler
{
    /// <summary>Lists all exchange rates mapped to DTOs.</summary>
    /// <param name="query">The query.</param>
    /// <param name="repository">The read repository.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The exchange rates.</returns>
    public static async Task<IReadOnlyList<ExchangeRateDto>> Handle(
        ListExchangeRatesQuery query,
        IGenericReadRepository<ExchangeRate, Guid> repository,
        CancellationToken ct)
    {
        IReadOnlyList<ExchangeRate> rates = await repository.ListAsync(new AllExchangeRatesSpec(), ct).ConfigureAwait(false);
        return rates.Select(rate => rate.ToDto()).ToList();
    }
}
```

- [ ] **Step 2: Replace `Pricing.Host/Program.cs`** (mirror `Basket.Host/Program.cs`)

```csharp
using Pricing.Application.Pricing;
using Pricing.Application.Database;
using Pricing.Host.Database;
using Pricing.Host.Infrastructure;
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
builder.AddPricingPersistence();
builder.Services.Configure<PricingOptions>(builder.Configuration.GetSection("Pricing"));
builder.Services.AddScoped<IExchangeRateProvider, ExchangeRateProviderStub>();
builder.Services.AddKeycloak(builder.Configuration, builder.Environment,
    builder.Configuration.GetSection("Keycloak").Get<KeycloakAuthenticationOptions>()!);
builder.Host.UseWolverine(opts =>
{
    // Handlers live in the Pricing.Application assembly; Wolverine scans the entry assembly by
    // default, so include the application assembly for runtime handler discovery.
    opts.Discovery.IncludeAssembly(typeof(PricingDbContext).Assembly);
    opts.AddTeckBehaviors();
    opts.AddTeckDeadLetterPolicy(new DeadLetterOptions());
});
var app = builder.Build();
app.UseTeckService();
app.MapDefaultEndpoints();
return await app.RunTeckServiceAsync(args);
```

- [ ] **Step 3: Create `Pricing.Host/Program.Public.cs`**

```csharp
/// <summary>Entry point class for the Pricing host application, exposed for integration testing.</summary>
public partial class Program
{
}
```

- [ ] **Step 4: Create `Pricing.Host/Infrastructure/ExchangeRateProviderStub.cs`**

```csharp
using Pricing.Application.Pricing;

namespace Pricing.Host.Infrastructure;

/// <summary>
/// No-op <see cref="IExchangeRateProvider"/>: returns no rates. A real ECB/OXR adapter and a
/// scheduled refresh can replace this without any domain or application change.
/// </summary>
public sealed class ExchangeRateProviderStub : IExchangeRateProvider
{
    /// <inheritdoc/>
    public Task<IReadOnlyList<RateSnapshot>> GetLatestAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<RateSnapshot>>([]);
}
```

- [ ] **Step 5: Create the endpoints.** Every endpoint is `sealed class …Endpoint(IMessageBus bus) : AuthenticatedEndpoint<TRequest, TResponse>`, sets `Permission`, builds the command/query, calls `bus.InvokeAsync<TResponse>(…, ct)`, and sends the result. Two worked examples then the full table.

**Example A — GET query with route+query binding** (`ResolvePriceEndpoint.cs` + `ResolvePriceRequest.cs` + `ResolvePriceRequestValidator.cs`):

```csharp
// ResolvePriceRequest.cs
namespace Pricing.Host.Endpoints.Pricing;

/// <summary>Query parameters for resolving a product price.</summary>
/// <param name="ProductId">The product identifier.</param>
/// <param name="Currency">The requested ISO currency.</param>
/// <param name="Quantity">The requested quantity (defaults to 1).</param>
/// <param name="Country">The optional request country.</param>
/// <param name="CustomerGroupId">The optional request customer group.</param>
/// <param name="ChannelId">The optional request channel.</param>
/// <param name="At">The optional resolution moment.</param>
public sealed record ResolvePriceRequest(
    Guid ProductId,
    string Currency,
    int? Quantity,
    string? Country,
    Guid? CustomerGroupId,
    Guid? ChannelId,
    DateTimeOffset? At);
```

```csharp
// ResolvePriceRequestValidator.cs
using FastEndpoints;
using FluentValidation;

namespace Pricing.Host.Endpoints.Pricing;

/// <summary>Validates <see cref="ResolvePriceRequest"/>.</summary>
public sealed class ResolvePriceRequestValidator : Validator<ResolvePriceRequest>
{
    /// <summary>Initializes a new instance of the <see cref="ResolvePriceRequestValidator"/> class.</summary>
    public ResolvePriceRequestValidator()
    {
        RuleFor(request => request.ProductId).NotEmpty();
        RuleFor(request => request.Currency).NotEmpty().Length(3);
        RuleFor(request => request.Quantity).GreaterThanOrEqualTo(1).When(request => request.Quantity.HasValue);
    }
}
```

```csharp
// ResolvePriceEndpoint.cs
using Pricing.Application.Pricing.Features.ResolvePrice.V1;
using Pricing.Application.Pricing.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Pricing.Host.Endpoints.Pricing;

/// <summary>Resolves the effective price for a product in a request context.</summary>
/// <param name="bus">The message bus.</param>
public sealed class ResolvePriceEndpoint(IMessageBus bus) : AuthenticatedEndpoint<ResolvePriceRequest, ResolvedPriceDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("pricing", "read", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(ResolvePriceRequest request, CancellationToken ct)
    {
        var query = new ResolvePriceQuery(
            request.ProductId, request.Currency, request.Quantity ?? 1,
            request.Country, request.CustomerGroupId, request.ChannelId, request.At);
        var result = await bus.InvokeAsync<ResolvedPriceDto>(query, ct);
        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Get("/prices/resolve");
        Version(0);
    }
}
```

**Example B — POST command returning 201** (`CreatePriceListEndpoint.cs` + request + validator):

```csharp
// CreatePriceListRequest.cs
namespace Pricing.Host.Endpoints.Pricing;

/// <summary>Request to create a price list.</summary>
/// <param name="Name">The display name.</param>
/// <param name="Description">The optional description.</param>
/// <param name="Currency">The scope ISO currency.</param>
/// <param name="Country">The scope country, or null.</param>
/// <param name="CustomerGroupId">The scope customer group, or null.</param>
/// <param name="ChannelId">The scope channel, or null.</param>
/// <param name="ValidFrom">The inclusive validity start, or null.</param>
/// <param name="ValidUntil">The exclusive validity end, or null.</param>
public sealed record CreatePriceListRequest(
    string Name,
    string? Description,
    string Currency,
    string? Country,
    Guid? CustomerGroupId,
    Guid? ChannelId,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidUntil);
```

```csharp
// CreatePriceListRequestValidator.cs
using FastEndpoints;
using FluentValidation;

namespace Pricing.Host.Endpoints.Pricing;

/// <summary>Validates <see cref="CreatePriceListRequest"/>.</summary>
public sealed class CreatePriceListRequestValidator : Validator<CreatePriceListRequest>
{
    /// <summary>Initializes a new instance of the <see cref="CreatePriceListRequestValidator"/> class.</summary>
    public CreatePriceListRequestValidator()
    {
        RuleFor(request => request.Name).NotEmpty().MaximumLength(256);
        RuleFor(request => request.Currency).NotEmpty().Length(3);
        RuleFor(request => request.Country).Length(2).When(request => request.Country is not null);
    }
}
```

```csharp
// CreatePriceListEndpoint.cs
using Pricing.Application.Pricing.Features.CreatePriceList.V1;
using Pricing.Application.Pricing.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Pricing.Host.Endpoints.Pricing;

/// <summary>Creates a new draft price list.</summary>
/// <param name="bus">The message bus.</param>
public sealed class CreatePriceListEndpoint(IMessageBus bus) : AuthenticatedEndpoint<CreatePriceListRequest, PriceListDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("pricing", "manage", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(CreatePriceListRequest request, CancellationToken ct)
    {
        var command = new CreatePriceListCommand(
            request.Name, request.Description, request.Currency, request.Country,
            request.CustomerGroupId, request.ChannelId, request.ValidFrom, request.ValidUntil);
        var result = await bus.InvokeAsync<PriceListDto>(command, ct);
        HttpContext.Response.Headers.Location = $"/price-lists/{result.Id}";
        await Send.ResponseAsync(result, StatusCodes.Status201Created, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Post("/price-lists");
        Version(0);
    }
}
```

**Remaining endpoints** — create each as a full file following the applicable example above (Example A shape for GET/reads → `Send.OkAsync`; Example B shape for writes; route parameters bind to same-named request properties; all `Permission => new("pricing", <scope>, "public")`). For `{id}`/`{productId}` route params, add them to the request record and to `Get/Put/Post/Delete("…/{id}")`.

| Endpoint file | Verb + Route | Permission scope | Request → dispatched message | Invoke type | Send |
|---|---|---|---|---|---|
| `GetPriceListEndpoint` | GET `/price-lists/{id}` | read | `GetPriceListRequest(Guid Id)` → `GetPriceListQuery(Id)` | `PriceListDto` | `Send.OkAsync` |
| `ListPriceListsEndpoint` | GET `/price-lists` | read | `EmptyRequest` → `new ListPriceListsQuery()` | `IReadOnlyList<PriceListDto>` | `Send.OkAsync` |
| `UpdatePriceListEndpoint` | PUT `/price-lists/{id}` | manage | `UpdatePriceListRequest(Guid Id, string Name, string? Description, string Currency, string? Country, Guid? CustomerGroupId, Guid? ChannelId, DateTimeOffset? ValidFrom, DateTimeOffset? ValidUntil)` → `UpdatePriceListCommand(...)` | `PriceListDto` | `Send.OkAsync` |
| `ActivatePriceListEndpoint` | POST `/price-lists/{id}/activate` | manage | `ActivatePriceListRequest(Guid Id)` → `ActivatePriceListCommand(Id)` | `PriceListDto` | `Send.OkAsync` |
| `ArchivePriceListEndpoint` | POST `/price-lists/{id}/archive` | manage | `ArchivePriceListRequest(Guid Id)` → `ArchivePriceListCommand(Id)` | `PriceListDto` | `Send.OkAsync` |
| `AddOrUpdatePriceEndpoint` | PUT `/price-lists/{id}/prices/{productId}` | manage | `AddOrUpdatePriceRequest(Guid Id, Guid ProductId, decimal Amount, IReadOnlyList<PriceTierInput> Tiers)` → `AddOrUpdatePriceCommand(Id, ProductId, Amount, Tiers)` | `PriceListDto` | `Send.OkAsync` |
| `RemovePriceEndpoint` | DELETE `/price-lists/{id}/prices/{productId}` | manage | `RemovePriceRequest(Guid Id, Guid ProductId)` → `RemovePriceCommand(Id, ProductId)` | `PriceListDto` | `Send.OkAsync` |
| `SetExchangeRateEndpoint` | PUT `/exchange-rates` | manage | `SetExchangeRateRequest(string FromCurrency, string ToCurrency, decimal Rate, DateTimeOffset? ValidFrom, DateTimeOffset? ValidUntil)` → `SetExchangeRateCommand(...)` | `ExchangeRateDto` | `Send.OkAsync` |
| `GetExchangeRatesEndpoint` | GET `/exchange-rates` | read | `EmptyRequest` → `new ListExchangeRatesQuery()` | `IReadOnlyList<ExchangeRateDto>` | `Send.OkAsync` |
| `RemoveExchangeRateEndpoint` | DELETE `/exchange-rates/{fromCurrency}/{toCurrency}` | manage | `RemoveExchangeRateRequest(string FromCurrency, string ToCurrency)` → `RemoveExchangeRateCommand(...)`; call `bus.InvokeAsync<Success>` then `Send.NoContentAsync(ct)` (`using ErrorOr;`) | `ErrorOr.Success` | `Send.NoContentAsync` |

Validators: add a `Validator<TRequest>` (as in the examples) for any request with constraints — at minimum `NotEmpty` on ids/currencies, `Length(3)` on currency, `GreaterThan(0)` on `Rate`, `GreaterThanOrEqualTo(0)` on `Amount`. `EmptyRequest` endpoints (`ListPriceLists`, `GetExchangeRates`) need no validator. For `EmptyRequest`, add `using FastEndpoints;` (as in `Basket.Host/Endpoints/Baskets/GetCurrentBasketEndpoint.cs`).

- [ ] **Step 6: Build the host**

Run: `dotnet build src/services/commerce/pricing/Pricing.Host/Pricing.Host.csproj`
Expected: build succeeds (analyzers-as-errors — fix any missing XML docs).

- [ ] **Step 7: Commit**

```bash
git add src/services/commerce/pricing/Pricing.Host src/services/commerce/pricing/Pricing.Application/Pricing/Features/ListExchangeRates src/services/commerce/pricing/Pricing.Application/Pricing/ReadModels/AllExchangeRatesSpec.cs
git commit -m "feat(pricing): host — program, endpoints, FX provider stub"
```

---

### Task 17: EF Core migration `InitialPricing`

**Files:**
- Create: `src/services/commerce/pricing/Pricing.Host/Database/Migrations/*_InitialPricing.cs` (+ `.Designer.cs` + `PricingDbContextModelSnapshot.cs`) — generated.

- [ ] **Step 1: Generate the migration**

Run from repo root:
```bash
dotnet ef migrations add InitialPricing \
  --project src/services/commerce/pricing/Pricing.Host/Pricing.Host.csproj \
  --startup-project src/services/commerce/pricing/Pricing.Host/Pricing.Host.csproj \
  --context PricingDbContext \
  --output-dir Database/Migrations
```
Expected: three files created under `Pricing.Host/Database/Migrations/`. The EF design-time factory (`PricingDbContextDesignTimeFactory`) supplies the connection; no running DB is needed to scaffold.

- [ ] **Step 2: Hand-fix the generated migration `.cs` for analyzers** (COORDINATION gotcha)

The generated `*_InitialPricing.cs` uses a block namespace and omits trailing commas, which trips analyzers-as-errors. Convert to a **file-scoped namespace** and add **trailing commas** on multi-line initializers (the `.Designer.cs` and snapshot are `#nullable disable` generated files — leave them; only the main migration `.cs` needs the file-scoped-namespace fix, mirroring `20260701231454_InitialBasket.cs`). Verify the schema includes: `PriceLists` (with `Currency`/`Country`/`CustomerGroupId`/`ChannelId` owned-scope columns, `Status`, `ValidFrom`, `ValidUntil`), `Prices` (with owned `Amount`/`Currency`, FK `PriceListId`, index on `TenantId,ProductId`), `PriceTiers` (owned, `PriceId`+`MinQuantity` key, `Amount`/`Currency`), `ExchangeRates` (unique index `TenantId,FromCurrency,ToCurrency`).

- [ ] **Step 3: Build to confirm the migration compiles**

Run: `dotnet build src/services/commerce/pricing/Pricing.Host/Pricing.Host.csproj`
Expected: build succeeds with analyzers-as-errors.

- [ ] **Step 4: Commit**

```bash
git add src/services/commerce/pricing/Pricing.Host/Database/Migrations
git commit -m "feat(pricing): InitialPricing EF migration"
```

---

### Task 18: Architecture test project

Because pricing HAS real `IQuery<>` types, this uses `SharedArchitectureRules.AssertAll` directly (like `order`) — no skipped rule.

**Files:**
- Create: `tests/architecture/Pricing.Architecture.UnitTests/Pricing.Architecture.UnitTests.csproj`
- Create: `tests/architecture/Pricing.Architecture.UnitTests/PricingArchitectureTests.cs`
- Modify: `Teck.Platform.slnx` (register the project)

- [ ] **Step 1: Create the csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>Pricing.Architecture.UnitTests</RootNamespace>
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
    <ProjectReference Include="..\..\..\src\services\commerce\pricing\Pricing.Application\Pricing.Application.csproj" />
    <ProjectReference Include="..\..\..\src\services\commerce\pricing\Pricing.Domain\Pricing.Domain.csproj" />
    <ProjectReference Include="..\..\..\src\services\commerce\pricing\Pricing.Host\Pricing.Host.csproj" />
    <ProjectReference Include="..\Teck.Platform.Arch.Tests\Teck.Platform.Arch.Tests.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create `PricingArchitectureTests.cs`**

```csharp
using System.Reflection;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using SharedKernel.Core.Domain;
using Teck.Platform.Arch.Tests.Rules;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Pricing.Architecture.UnitTests;

public sealed class PricingArchitectureTests : Teck.Platform.Arch.Tests.SharedTestBase
{
    private static readonly Assembly DomainAssembly = typeof(Pricing.Domain.Entities.PriceList).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(Pricing.Application.Pricing.Features.ResolvePrice.V1.ResolvePriceHandler).Assembly;
    private static readonly Assembly HostAssembly = typeof(Program).Assembly;

    private static readonly ArchUnitNET.Domain.Architecture PricingArchitecture = new ArchLoader()
        .LoadAssemblies(DomainAssembly, ApplicationAssembly, HostAssembly)
        .Build();

    [Fact]
    public void PricingHost_ShouldNotReferencePricingDomainDirectly() =>
        Types().That().ResideInAssembly(HostAssembly)
            .Should().NotDependOnAny(Types().That().ResideInAssembly(DomainAssembly))
            .Because("the host must depend on the application layer, not the domain layer directly")
            .Check(PricingArchitecture);

    [Fact]
    public void PricingApplication_ShouldNotReferencePricingHost() =>
        Types().That().ResideInAssembly(ApplicationAssembly)
            .Should().NotDependOnAny(Types().That().ResideInAssembly(HostAssembly))
            .Because("the application layer must not depend on the host layer")
            .Check(PricingArchitecture);

    [Fact]
    public void PricingAggregateRoots_ShouldImplementTenantScoped() =>
        Classes().That().ImplementInterface(typeof(IAggregateRoot))
            .Should().ImplementInterface(typeof(ITenantScoped))
            .Because("tenant-owned pricing aggregates must be scoped to a tenant")
            .Check(PricingArchitecture);

    [Fact]
    public void PricingApplication_ShouldNotDependOnDbContextOrArdalisRepository() =>
        Types().That().ResideInAssembly(ApplicationAssembly)
            .And().DoNotHaveFullNameContaining("DbContext")
            .Should().NotDependOnAny(Types().That().HaveFullNameContaining("DbContext"))
            .AndShould().NotDependOnAny(Types().That().HaveFullNameContaining("Ardalis.Specification.IRepositoryBase"))
            .Because("application handlers must use SharedKernel repository + unit-of-work abstractions")
            .Check(PricingArchitecture);

    [Fact]
    public void PricingEndpoints_ShouldDeriveFromAuthenticatedEndpoint() =>
        Teck.Platform.Arch.Tests.Rules.EndpointRules
            .EndpointsShouldDeriveFromAuthenticatedEndpoint(HostAssembly);

    /// <summary>
    /// Runs every shared architecture rule via <see cref="SharedArchitectureRules.AssertAll"/>.
    /// Unlike basket/customer, pricing HAS <c>IQuery&lt;T&gt;</c> implementors (ResolvePrice,
    /// GetPriceList, ListPriceLists, ListExchangeRates), so <c>QueriesShouldNotModifyState</c> is
    /// included — mirroring the order reference service.
    /// </summary>
    [Fact]
    public void PricingService_ShouldFollowSharedArchitectureRules() =>
        SharedArchitectureRules.AssertAll(PricingArchitecture, ApplicationAssembly);
}
```

- [ ] **Step 3: Register the project in `Teck.Platform.slnx`** (tests folder block, mirror the basket architecture entry)

```xml
    <Project Path="tests/architecture/Pricing.Architecture.UnitTests/Pricing.Architecture.UnitTests.csproj" />
```

- [ ] **Step 4: Run the architecture tests**

Run: `dotnet test tests/architecture/Pricing.Architecture.UnitTests`
Expected: PASS. If `QueriesShouldNotModifyState` fails, a query handler is using a write repository — fix the handler (queries use `IGenericReadRepository` only). If `Handlers_ShouldEndWithHandler` fails, a static `Handle` class is misnamed.

- [ ] **Step 5: Commit**

```bash
git add tests/architecture/Pricing.Architecture.UnitTests Teck.Platform.slnx
git commit -m "test(pricing): architecture rules"
```

---

### Task 19: Integration test — resolve end-to-end (native + cross-currency FX)

Mirror `Basket.IntegrationTests`: copy its harness files (`SharedTestcontainersCollection.cs`, `MockBearerAuthenticationHandler.cs`, and the `…WebApplicationFactory`/base pattern from `BasketCheckoutTests.cs`), renaming `Basket`→`Pricing` and swapping connection-string keys to `PricingWrite`/`PricingRead`.

**Files:**
- Create: `tests/integration/Pricing.IntegrationTests/Pricing.IntegrationTests.csproj`
- Create: `tests/integration/Pricing.IntegrationTests/SharedTestcontainersCollection.cs`
- Create: `tests/integration/Pricing.IntegrationTests/MockBearerAuthenticationHandler.cs`
- Create: `tests/integration/Pricing.IntegrationTests/PriceResolutionTests.cs` (contains the `PricingIntegrationTestBase` + factory)
- Modify: `Teck.Platform.slnx`

- [ ] **Step 1: Create the csproj** (mirror `Basket.IntegrationTests.csproj`)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>Pricing.IntegrationTests</RootNamespace>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
    <PackageReference Include="Testcontainers.PostgreSql" />
    <PackageReference Include="Testcontainers.RabbitMq" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\src\services\commerce\pricing\Pricing.Application\Pricing.Application.csproj" />
    <ProjectReference Include="..\..\..\src\services\commerce\pricing\Pricing.Host\Pricing.Host.csproj" />
    <ProjectReference Include="..\Teck.Platform.IntegrationTests.Shared\Teck.Platform.IntegrationTests.Shared.csproj" />
    <ProjectReference Include="..\..\..\src\shared\SharedKernel.Infrastructure\SharedKernel.Infrastructure.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Copy `SharedTestcontainersCollection.cs` and `MockBearerAuthenticationHandler.cs`** verbatim from `Basket.IntegrationTests`, changing only the namespace to `Pricing.IntegrationTests`. (The mock handler's tenant/customer claims are fine as-is; pricing reads `tenant_id` for tenant scoping.)

- [ ] **Step 3: Create `PriceResolutionTests.cs`** (test class + factory base, mirroring `BasketCheckoutTests.cs`'s `BasketIntegrationTestBase`/`BasketWebApplicationFactory`)

```csharp
using System.Net.Http.Json;
using Finbuckle.MultiTenant.Extensions;
using JasperFx.CommandLine;
using Keycloak.AuthServices.Authorization.Requirements;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Pricing.Application.Pricing.Responses;
using SharedKernel.Infrastructure.MultiTenant;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Pricing.IntegrationTests;

[Collection("SharedTestcontainers")]
public sealed class PriceResolutionTests : PricingIntegrationTestBase
{
    public PriceResolutionTests(SharedTestcontainersFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Resolve_NativeCurrency_ReturnsTieredPrice()
    {
        var productId = Guid.NewGuid();

        var created = await Client.PostAsJsonAsync("/price-lists", new
        {
            Name = "Retail USD",
            Currency = "USD",
        });
        created.EnsureSuccessStatusCode();
        var list = await created.Content.ReadFromJsonAsync<PriceListDto>();

        var priced = await Client.PutAsJsonAsync($"/price-lists/{list!.Id}/prices/{productId}", new
        {
            Id = list.Id,
            ProductId = productId,
            Amount = 10m,
            Tiers = new[] { new { MinQuantity = 10, Amount = 8m } },
        });
        priced.EnsureSuccessStatusCode();

        var activated = await Client.PostAsJsonAsync($"/price-lists/{list.Id}/activate", new { Id = list.Id });
        activated.EnsureSuccessStatusCode();

        var resolved = await Client.GetFromJsonAsync<ResolvedPriceDto>(
            $"/prices/resolve?productId={productId}&currency=USD&quantity=10");

        Assert.NotNull(resolved);
        Assert.False(resolved!.Converted);
        Assert.Equal(8m, resolved.UnitAmount);
        Assert.Equal("USD", resolved.Currency);
    }

    [Fact]
    public async Task Resolve_CrossCurrency_UsesSeededRate()
    {
        var productId = Guid.NewGuid();

        var created = await Client.PostAsJsonAsync("/price-lists", new { Name = "EUR list", Currency = "EUR" });
        created.EnsureSuccessStatusCode();
        var list = await created.Content.ReadFromJsonAsync<PriceListDto>();

        await (await Client.PutAsJsonAsync($"/price-lists/{list!.Id}/prices/{productId}", new
        {
            Id = list.Id,
            ProductId = productId,
            Amount = 10m,
            Tiers = Array.Empty<object>(),
        })).EnsureSuccessOrThrowAsync();
        await (await Client.PostAsJsonAsync($"/price-lists/{list.Id}/activate", new { Id = list.Id })).EnsureSuccessOrThrowAsync();

        await (await Client.PutAsJsonAsync("/exchange-rates", new
        {
            FromCurrency = "EUR",
            ToCurrency = "USD",
            Rate = 1.1m,
        })).EnsureSuccessOrThrowAsync();

        var resolved = await Client.GetFromJsonAsync<ResolvedPriceDto>(
            $"/prices/resolve?productId={productId}&currency=USD&quantity=1");

        Assert.NotNull(resolved);
        Assert.True(resolved!.Converted);
        Assert.Equal(11.00m, resolved.UnitAmount);
        Assert.Equal(1.1m, resolved.RateApplied);
    }
}

/// <summary>Boots Pricing.Host in-memory against a Testcontainers Postgres, with mock auth.</summary>
public abstract class PricingIntegrationTestBase : IDisposable
{
    private readonly SharedTestcontainersFixture fixture;
    private readonly string databaseConnectionString;
    private readonly WebApplicationFactory<Program> factory;

    protected PricingIntegrationTestBase(SharedTestcontainersFixture fixture)
    {
        this.fixture = fixture;
        databaseConnectionString = fixture
            .CreateSharedTestDatabaseAsync(typeof(Pricing.Application.Database.PricingDbContext), "Pricing.Host")
            .GetAwaiter().GetResult();

        factory = new PricingWebApplicationFactory(databaseConnectionString);
        Client = factory.CreateClient();
    }

    protected HttpClient Client { get; }

    public void Dispose()
    {
        Client.Dispose();
        factory.Dispose();
        fixture.TruncateAllTablesAsync(databaseConnectionString).GetAwaiter().GetResult();
    }

    private sealed class PricingWebApplicationFactory(string databaseConnectionString) : WebApplicationFactory<Program>
    {
        static PricingWebApplicationFactory() => JasperFxEnvironment.AutoStartHost = true;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:PricingWrite", databaseConnectionString);
            builder.UseSetting("ConnectionStrings:PricingRead", databaseConnectionString);
            builder.UseSetting("ConnectionStrings:Default", databaseConnectionString);
            builder.UseSetting("Keycloak:realm", "test");
            builder.UseSetting("Keycloak:auth-server-url", "http://localhost:8080");
            builder.UseSetting("Keycloak:resource", "pricing-api");

            builder.ConfigureTestServices(services =>
            {
                services.AddMultiTenant<TenantDetails>();
                services.AddTransient<MockBearerAuthenticationHandler>();
                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    var bearer = options.Schemes.FirstOrDefault(s => s.Name == MockBearerAuthenticationHandler.SchemeName);
                    if (bearer is not null)
                    {
                        bearer.HandlerType = typeof(MockBearerAuthenticationHandler);
                    }

                    options.DefaultAuthenticateScheme = MockBearerAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = MockBearerAuthenticationHandler.SchemeName;
                    options.DefaultForbidScheme = MockBearerAuthenticationHandler.SchemeName;
                });

                var keycloakHandler = services.FirstOrDefault(
                    d => d.ImplementationType?.Name == "ParameterizedProtectedResourceRequirementHandler");
                if (keycloakHandler is not null)
                {
                    services.Remove(keycloakHandler);
                }

                services.AddSingleton<IAuthorizationHandler, PermissiveProtectedResourceHandler>();
            });
        }
    }

    private sealed class PermissiveProtectedResourceHandler
        : AuthorizationHandler<ParameterizedProtectedResourceRequirement>
    {
        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            ParameterizedProtectedResourceRequirement requirement)
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}

/// <summary>Small helper to surface non-success responses with their body.</summary>
internal static class HttpResponseAssertions
{
    public static async Task EnsureSuccessOrThrowAsync(this HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"{(int)response.StatusCode}: {body}");
        }
    }
}
```

> If `CreateSharedTestDatabaseAsync`/`TruncateAllTablesAsync` signatures differ, copy them exactly from the current `BasketCheckoutTests.cs` base — they are the source of truth for the shared fixture API.

- [ ] **Step 4: Register in `Teck.Platform.slnx`** (tests folder block)

```xml
    <Project Path="tests/integration/Pricing.IntegrationTests/Pricing.IntegrationTests.csproj" />
```

- [ ] **Step 5: Run the integration tests** (Docker required for Testcontainers)

Run: `dotnet test tests/integration/Pricing.IntegrationTests`
Expected: PASS (2 tests). The migration (`InitialPricing`) applies to the Testcontainers DB; both resolve paths return the expected amounts.

- [ ] **Step 6: Commit**

```bash
git add tests/integration/Pricing.IntegrationTests Teck.Platform.slnx
git commit -m "test(pricing): integration — native and cross-currency resolve"
```

---

### Task 20: Aspire registration + full affected gate

**Files:**
- Modify: `src/aspire/Teck.AppHost/Teck.AppHost.csproj`
- Modify: `src/aspire/Teck.AppHost/AppHost.cs`

- [ ] **Step 1: Add the project reference in `Teck.AppHost.csproj`** (mirror the basket line)

```xml
    <ProjectReference Include="..\..\services\commerce\pricing\Pricing.Host\Pricing.Host.csproj" />
```

- [ ] **Step 2: Add the database + project block in `AppHost.cs`**

After the existing `var catalogDb = postgres.AddDatabase("catalogdb");` line, add:
```csharp
var pricingDb = postgres.AddDatabase("pricingdb");
```

Alongside the other `builder.AddProject<…>` blocks, add (mirror the basket block — pricing is independent, consumes no events):
```csharp
// pricing resolves product list prices with multi-currency FX; it emits PriceChanged for
// future consumers and consumes nothing.
builder.AddProject<Projects.Pricing_Host>("pricing")
    .WithHttpEndpoint(name: "http")
    .WithEnvironment("ConnectionStrings__PricingWrite", pricingDb)
    .WithEnvironment("ConnectionStrings__PricingRead", pricingDb)
    .WithReference(rabbitmq).WithReference(redis).WithReference(keycloak)
    .WaitFor(pricingDb).WaitFor(keycloak);
```

- [ ] **Step 3: Build the AppHost** (regenerates the `Projects.Pricing_Host` metadata)

Run: `dotnet build src/aspire/Teck.AppHost/Teck.AppHost.csproj`
Expected: build succeeds; `Projects.Pricing_Host` resolves.

- [ ] **Step 4: Run the full affected gate**

Run: `nx affected -t build test lint typecheck`
Expected: all green. (If `nx` is unavailable in the environment, run `dotnet build Teck.Platform.slnx` then `dotnet test tests/unit/Pricing.UnitTests tests/architecture/Pricing.Architecture.UnitTests tests/integration/Pricing.IntegrationTests`.)

- [ ] **Step 5: Commit**

```bash
git add src/aspire/Teck.AppHost
git commit -m "feat(pricing): register pricing in Aspire orchestration"
```

- [ ] **Step 6: Open the PR** (per COORDINATION — land at a PR; CI handles the rest; never tag or `nx release` from the branch)

```bash
git push -u origin worktree-pricing-service
gh pr create --title "feat(pricing): pricing service (scoped price lists + FX resolution)" \
  --body "Implements the pricing service per docs/superpowers/specs/2026-07-05-pricing-service-design.md and plan docs/superpowers/plans/2026-07-05-pricing-service.md."
```

---

## Definition of Done

1. `Pricing.{Domain,Application,Host}` build clean under analyzers-as-errors; public types documented.
2. All three test projects pass; `nx affected -t build test lint typecheck` green.
3. `PriceChangedIntegrationEvent` in `SharedKernel.Events`, emitted on every effective (Active-list) price change.
4. `InitialPricing` migration applies via `--migrate` / Testcontainers.
5. End-to-end: create list → add tiered price → activate → `GET /prices/resolve` returns the most-specific, correctly-tiered price; a cross-currency resolve returns an FX-converted price using a seeded rate.
6. Pricing registered in `Teck.Platform.slnx` and Aspire; PR opened from `worktree-pricing-service`.

## Out of plan scope (handled elsewhere)

- **Dockerfile / container build:** produced from the shared `deploy/Containerfile.template` with build args (this repo owns no per-service Dockerfile) — not a task here, mirroring the basket build.
- **Base K8s manifests (`deploy/pricing/base/`) and environment overlays:** overlays live in **Teck.GitOps**, infra/Helm in **Teck.Terraform**; base manifests are added through the deploy tooling, not this service plan.
- **WolverineFx codegen** is pre-generated in CI before `dotnet publish` (per `deploy/AGENTS.md`); local dev uses runtime codegen and needs no manual step.
- **Migrating `OrderPlacedIntegrationEvent` into `SharedKernel.Events`** and any consumer of `PriceChanged` (basket reprice, search) are separate work items — pricing only *emits* the contract.

