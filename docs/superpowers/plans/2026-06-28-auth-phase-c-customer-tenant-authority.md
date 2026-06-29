# Auth Phase C — gRPC Contract + `customer` Tenant-Authority Service Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up a minimal real `customer` service whose sole job (for now) is to answer per-tenant database-strategy lookups over the FastEndpoints messaging-remote (gRPC) bus, backed by a real `Tenant` aggregate + EF Core migration.

**Architecture:** New shared gRPC contract (`GetTenantDatabaseInfoCommand` → `TenantDatabaseInfoRpcResult`) in `SharedKernel.Grpc.Contracts`. A `customer` service mirrors the canonical `order` clean-architecture template (Domain → Application → Host) with the CQRS three-context split. A FastEndpoints `ICommandHandler` resolves the lookup via the SharedKernel generic read repository + an Ardalis specification (no per-entity repository — unlike the reference, which we improve to satisfy our arch rules). The write context owns an initial EF Core migration with a seeded dev tenant.

**Tech Stack:** .NET 10, FastEndpoints + FastEndpoints.Messaging.Remote/Core, EF Core 10 (Npgsql), Finbuckle.MultiTenant, WolverineFx, Ardalis.Specification, ErrorOr, xUnit v3, ArchUnitNET.

## Global Constraints

- Mirror the `order` service exactly for structure/naming: project folders `Customer.Domain`, `Customer.Application`, `Customer.Host`; namespaces pluralized like order (`Customers.Domain`, `Customers.Application`, `Customers.Host`) — match how `Order.*` csproj uses `<RootNamespace>Orders.*</RootNamespace>`.
- `net10.0`, nullable + implicit usings; `TreatWarningsAsErrors=true`; allowlist `.editorconfig` (XML docs on public members, ordered usings, file-scoped namespaces).
- Repository + Unit of Work only: handlers depend on `IGenericReadRepository<T,TId>` / `IGenericWriteRepository<T,TId>` + `IUnitOfWork`; query logic in `Specification` classes under `Application/Tenants/ReadModels/`. No per-entity repos, no `IRepositoryBase`, no concrete `DbContext` in Application handlers.
- CQRS three-context split: `TenantDbContextBase` (Application) → `CustomerDbContext` (write leaf, Application, migration target) + `CustomerReadDbContext` (Host, `NoTracking`).
- Migrations live in `Customer.Host/Database/Migrations/`; backward-compatible; applied via `--migrate` / JasperFx command mode like `order`.
- Conventional commits; never tag or run `nx release`. Run `nx affected -t build test lint` before a task is done.
- Spec reference: `docs/superpowers/specs/2026-06-28-platform-auth-architecture-design.md` §6.

---

### Task 1: gRPC/remote contract in `SharedKernel.Grpc.Contracts`

**Files:**
- Create: `src/shared/SharedKernel.Grpc.Contracts/Remote/V1/Tenants/GetTenantDatabaseInfoCommand.cs`
- Create: `src/shared/SharedKernel.Grpc.Contracts/Remote/V1/Tenants/TenantDatabaseInfoRpcResult.cs`
- Test: `tests/unit/SharedKernel.UnitTests/Grpc/TenantContractTests.cs`

**Interfaces:**
- Produces: `GetTenantDatabaseInfoCommand : ICommand<TenantDatabaseInfoRpcResult>` with `string TenantId`, `string ServiceName`; `TenantDatabaseInfoRpcResult` with `bool Found`, `string TenantId`, `string Identifier`, `string DatabaseStrategy`, `string DatabaseProvider`, `bool HasReadReplicas`, `string? ErrorDetail`. Consumed by Task 5 (handler), and by Phase B's gateway resolver.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/unit/SharedKernel.UnitTests/Grpc/TenantContractTests.cs
using SharedKernel.Grpc.Contracts.Remote.V1.Tenants;
using Xunit;

namespace SharedKernel.UnitTests.Grpc;

public sealed class TenantContractTests
{
    [Fact]
    public void Result_DefaultsAreSafe()
    {
        var result = new TenantDatabaseInfoRpcResult();
        Assert.False(result.Found);
        Assert.Equal(string.Empty, result.TenantId);
        Assert.Null(result.ErrorDetail);
    }

    [Fact]
    public void Command_CarriesTenantAndServiceName()
    {
        var command = new GetTenantDatabaseInfoCommand { TenantId = "abc", ServiceName = "order" };
        Assert.Equal("abc", command.TenantId);
        Assert.Equal("order", command.ServiceName);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `nx test --project=SharedKernel.UnitTests`
Expected: FAIL — types don't exist. (Add a project reference from `SharedKernel.UnitTests` to `SharedKernel.Grpc.Contracts`.)

- [ ] **Step 3: Implement the contract**

```csharp
// src/shared/SharedKernel.Grpc.Contracts/Remote/V1/Tenants/GetTenantDatabaseInfoCommand.cs
using FastEndpoints;

namespace SharedKernel.Grpc.Contracts.Remote.V1.Tenants;

/// <summary>Requests tenant database metadata from the customer service.</summary>
public sealed class GetTenantDatabaseInfoCommand : ICommand<TenantDatabaseInfoRpcResult>
{
    /// <summary>Gets or sets the tenant identifier (GUID string).</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets or sets the downstream service name requesting the metadata.</summary>
    public string ServiceName { get; set; } = string.Empty;
}
```

```csharp
// src/shared/SharedKernel.Grpc.Contracts/Remote/V1/Tenants/TenantDatabaseInfoRpcResult.cs
namespace SharedKernel.Grpc.Contracts.Remote.V1.Tenants;

/// <summary>Tenant database metadata returned by the customer service.</summary>
public sealed class TenantDatabaseInfoRpcResult
{
    /// <summary>Gets or sets a value indicating whether the tenant was found.</summary>
    public bool Found { get; set; }

    /// <summary>Gets or sets the tenant identifier.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets or sets the tenant's unique identifier slug.</summary>
    public string Identifier { get; set; } = string.Empty;

    /// <summary>Gets or sets the tenant's database strategy (e.g. "shared", "dedicated").</summary>
    public string DatabaseStrategy { get; set; } = string.Empty;

    /// <summary>Gets or sets the tenant's database provider (e.g. "postgres").</summary>
    public string DatabaseProvider { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the tenant has read replicas.</summary>
    public bool HasReadReplicas { get; set; }

    /// <summary>Gets or sets a human-readable error detail when <see cref="Found"/> is false.</summary>
    public string? ErrorDetail { get; set; }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `nx test --project=SharedKernel.UnitTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/shared/SharedKernel.Grpc.Contracts/Remote tests/unit/SharedKernel.UnitTests
git commit -m "feat(contracts): add GetTenantDatabaseInfo remote tenant contract"
```

---

### Task 2: `Tenant` aggregate (Customer.Domain)

**Files:**
- Create: `src/services/commerce/customer/Customer.Domain/Customer.Domain.csproj` (mirror `Order.Domain.csproj`, `<RootNamespace>Customers.Domain</RootNamespace>`)
- Create: `src/services/commerce/customer/Customer.Domain/Entities/Tenant.cs`
- Test: `tests/unit/Customer.UnitTests/Customer.UnitTests.csproj` (mirror `Order.UnitTests.csproj`)
- Test: `tests/unit/Customer.UnitTests/Domain/TenantTests.cs`
- Modify: `Teck.Platform.slnx`

**Interfaces:**
- Produces: `Tenant` aggregate with `static Tenant Create(Guid id, string identifier, string databaseStrategy, string databaseProvider, bool hasReadReplicas)`, read-only props `Id`, `Identifier`, `DatabaseStrategy`, `DatabaseProvider`, `HasReadReplicas`, `Status`. Consumed by Tasks 3–5.

> Note: `Tenant` is the global tenant registry record — it is **not** `ITenantScoped` (it is the authority for tenant identity, not a tenant-owned row). So the customer architecture tests must **not** assert `ITenantScoped` on it.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/unit/Customer.UnitTests/Domain/TenantTests.cs
using Customers.Domain.Entities;
using Xunit;

namespace Customer.UnitTests.Domain;

public sealed class TenantTests
{
    [Fact]
    public void Create_SetsProvidedValues()
    {
        var id = Guid.NewGuid();
        var tenant = Tenant.Create(id, "acme", "shared", "postgres", hasReadReplicas: false);

        Assert.Equal(id, tenant.Id);
        Assert.Equal("acme", tenant.Identifier);
        Assert.Equal("shared", tenant.DatabaseStrategy);
        Assert.Equal("postgres", tenant.DatabaseProvider);
        Assert.False(tenant.HasReadReplicas);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RejectsBlankIdentifier(string identifier)
    {
        Assert.Throws<ArgumentException>(() =>
            Tenant.Create(Guid.NewGuid(), identifier, "shared", "postgres", false));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `nx test --project=Customer.UnitTests`
Expected: FAIL — `Tenant` not defined.

- [ ] **Step 3: Implement `Tenant`** (mirror an `order` aggregate: inherit the SharedKernel base entity used by `Orders.Domain.Entities.Order`, private setters, static `Create`)

```csharp
// src/services/commerce/customer/Customer.Domain/Entities/Tenant.cs
using SharedKernel.Core.Domain;

namespace Customers.Domain.Entities;

/// <summary>The global tenant registry record and authority for per-tenant database strategy.</summary>
public sealed class Tenant : BaseEntity<Guid>, IAggregateRoot
{
    private Tenant(Guid id, string identifier, string databaseStrategy, string databaseProvider, bool hasReadReplicas)
        : base(id)
    {
        Identifier = identifier;
        DatabaseStrategy = databaseStrategy;
        DatabaseProvider = databaseProvider;
        HasReadReplicas = hasReadReplicas;
        Status = "active";
    }

    private Tenant() { } // EF

    /// <summary>Gets the tenant's unique identifier slug.</summary>
    public string Identifier { get; private set; } = string.Empty;

    /// <summary>Gets the tenant's database strategy (e.g. "shared", "dedicated").</summary>
    public string DatabaseStrategy { get; private set; } = string.Empty;

    /// <summary>Gets the tenant's database provider (e.g. "postgres").</summary>
    public string DatabaseProvider { get; private set; } = string.Empty;

    /// <summary>Gets a value indicating whether the tenant has read replicas.</summary>
    public bool HasReadReplicas { get; private set; }

    /// <summary>Gets the tenant status.</summary>
    public string Status { get; private set; } = string.Empty;

    /// <summary>Creates a new tenant registry record.</summary>
    /// <param name="id">The tenant identifier.</param>
    /// <param name="identifier">The unique identifier slug.</param>
    /// <param name="databaseStrategy">The database strategy.</param>
    /// <param name="databaseProvider">The database provider.</param>
    /// <param name="hasReadReplicas">Whether read replicas exist.</param>
    /// <returns>The created <see cref="Tenant"/>.</returns>
    public static Tenant Create(Guid id, string identifier, string databaseStrategy, string databaseProvider, bool hasReadReplicas)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            throw new ArgumentException("Identifier must be provided.", nameof(identifier));
        }

        return new Tenant(id, identifier, databaseStrategy, databaseProvider, hasReadReplicas);
    }
}
```

> Implementer: confirm the exact SharedKernel base type name/signature used by `Orders.Domain.Entities.Order` (open `Order.cs`) and match it — it may be `BaseEntity<TId>` or an aggregate base; mirror precisely including the protected ctor.

- [ ] **Step 4: Run to verify it passes**

Run: `nx test --project=Customer.UnitTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/services/commerce/customer/Customer.Domain tests/unit/Customer.UnitTests Teck.Platform.slnx
git commit -m "feat(customer): add Tenant aggregate"
```

---

### Task 3: Customer persistence stack (Application + Host contexts, repos, EF config)

**Files:**
- Create: `src/services/commerce/customer/Customer.Application/Customer.Application.csproj` (mirror `Order.Application.csproj`)
- Create: `src/services/commerce/customer/Customer.Application/Database/TenantDbContextBase.cs`
- Create: `src/services/commerce/customer/Customer.Application/Database/CustomerDbContext.cs`
- Create: `src/services/commerce/customer/Customer.Application/Database/Configurations/TenantConfiguration.cs`
- Create: `src/services/commerce/customer/Customer.Host/Customer.Host.csproj` (mirror `Order.Host.csproj`)
- Create: `src/services/commerce/customer/Customer.Host/Database/CustomerReadDbContext.cs`
- Create: `src/services/commerce/customer/Customer.Host/Database/CustomerReadRepository.cs`
- Create: `src/services/commerce/customer/Customer.Host/Database/CustomerWriteRepository.cs` (mirror order's write repo)
- Create: `src/services/commerce/customer/Customer.Host/Database/CustomerPersistenceExtensions.cs`

**Interfaces:**
- Produces: `CustomerDbContext` (write leaf, migration target), `CustomerReadDbContext` (NoTracking), `AddCustomerPersistence(this WebApplicationBuilder)`. Consumed by Tasks 4–6.

- [ ] **Step 1: Create the context trio** mirroring `order` exactly (see `OrderDbContextBase.cs`, `OrderDbContext.cs`, `OrderReadDbContext.cs`), substituting `Tenant` for `Order` and `Customers.*` for `Orders.*`:

```csharp
// src/services/commerce/customer/Customer.Application/Database/TenantDbContextBase.cs
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Customers.Domain.Entities;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.MultiTenant;

namespace Customers.Application.Database;

/// <summary>Abstract customer context defining the entity model once for the read/write leaves.</summary>
/// <param name="options">The context options.</param>
/// <param name="tenantContextAccessor">The current-tenant accessor.</param>
public abstract class TenantDbContextBase(DbContextOptions options, IMultiTenantContextAccessor<TenantDetails> tenantContextAccessor)
    : BaseDbContext(options, tenantAccessor: tenantContextAccessor)
{
    /// <summary>Gets the set of tenants (global registry; not tenant-filtered).</summary>
    public DbSet<Tenant> Tenants => Set<Tenant>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TenantDbContextBase).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
```

```csharp
// src/services/commerce/customer/Customer.Application/Database/CustomerDbContext.cs
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Infrastructure.MultiTenant;

namespace Customers.Application.Database;

/// <summary>The customer write context (tracking enabled). Owns EF Core migrations.</summary>
/// <param name="options">The context options.</param>
/// <param name="tenantContextAccessor">The current-tenant accessor.</param>
public class CustomerDbContext(DbContextOptions options, IMultiTenantContextAccessor<TenantDetails> tenantContextAccessor)
    : TenantDbContextBase(options, tenantContextAccessor);
```

```csharp
// src/services/commerce/customer/Customer.Host/Database/CustomerReadDbContext.cs
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Customers.Application.Database;
using SharedKernel.Infrastructure.MultiTenant;

namespace Customers.Host.Database;

/// <summary>The customer read context (NoTracking).</summary>
/// <param name="options">The context options.</param>
/// <param name="tenantContextAccessor">The current-tenant accessor.</param>
public class CustomerReadDbContext(DbContextOptions options, IMultiTenantContextAccessor<TenantDetails> tenantContextAccessor)
    : TenantDbContextBase(options, tenantContextAccessor)
{
    /// <inheritdoc/>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    }
}
```

- [ ] **Step 2: EF configuration for `Tenant`** (a unique index on `Identifier`)

```csharp
// src/services/commerce/customer/Customer.Application/Database/Configurations/TenantConfiguration.cs
using Customers.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Customers.Application.Database.Configurations;

/// <summary>EF Core configuration for the <see cref="Tenant"/> registry.</summary>
public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");
        builder.HasKey(tenant => tenant.Id);
        builder.Property(tenant => tenant.Identifier).IsRequired().HasMaxLength(128);
        builder.HasIndex(tenant => tenant.Identifier).IsUnique();
        builder.Property(tenant => tenant.DatabaseStrategy).IsRequired().HasMaxLength(64);
        builder.Property(tenant => tenant.DatabaseProvider).IsRequired().HasMaxLength(64);
        builder.Property(tenant => tenant.Status).IsRequired().HasMaxLength(32);
    }
}
```

- [ ] **Step 3: Read/write repositories + persistence extension** — mirror `OrderReadRepository.cs`, order's write repo, and `OrderPersistenceExtensions.cs`, substituting `Customer*` types and connection-string keys `CustomerWrite`/`CustomerRead`, `serviceName: "customer"`:

```csharp
// src/services/commerce/customer/Customer.Host/Database/CustomerPersistenceExtensions.cs
using Customers.Application.Database;
using SharedKernel.Core.Database;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.Database.MultiTenant;

namespace Customers.Host.Database;

/// <summary>Registers the customer persistence stack (tenant-aware read/write contexts, repos, UoW).</summary>
public static class CustomerPersistenceExtensions
{
    /// <summary>Adds the customer read/write contexts, repositories and unit of work.</summary>
    /// <param name="builder">The web application builder.</param>
    /// <returns>The same builder for chaining.</returns>
    public static WebApplicationBuilder AddCustomerPersistence(this WebApplicationBuilder builder)
    {
        var write = builder.Configuration.GetConnectionString("CustomerWrite")
            ?? builder.Configuration.GetConnectionString("Default")
            ?? throw new System.InvalidOperationException("Missing 'CustomerWrite'/'Default' connection string.");
        var read = builder.Configuration.GetConnectionString("CustomerRead") ?? write;

        builder.AddHybridMultiTenantDbContexts<CustomerDbContext, CustomerReadDbContext>(
            migrationsAssembly: typeof(Program).Assembly,
            defaultWriteConnectionString: write,
            defaultReadConnectionString: read,
            serviceName: "customer");

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped(typeof(IGenericReadRepository<,>), typeof(CustomerReadRepository<,>));
        builder.Services.AddScoped(typeof(IGenericWriteRepository<,>), typeof(CustomerWriteRepository<,>));
        builder.Services.AddScoped<IUnitOfWork>(sp => new UnitOfWork<CustomerDbContext>(sp.GetRequiredService<CustomerDbContext>()));

        return builder;
    }
}
```

- [ ] **Step 4: Build**

Run: `nx build Customer.Host`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/services/commerce/customer
git commit -m "feat(customer): add CQRS three-context persistence stack"
```

---

### Task 4: Tenant read specification + lookup mapping

**Files:**
- Create: `src/services/commerce/customer/Customer.Application/Tenants/ReadModels/TenantByIdSpec.cs`
- Create: `src/services/commerce/customer/Customer.Application/Tenants/ReadModels/TenantDatabaseInfo.cs`
- Test: `tests/unit/Customer.UnitTests/Tenants/TenantByIdSpecTests.cs`

**Interfaces:**
- Produces: `TenantByIdSpec : Specification<Tenant>` (filters by `Id`); `TenantDatabaseInfo` projection record. Consumed by Task 5.

- [ ] **Step 1: Write the failing spec test** (mirror how `order` tests specs; assert the spec's `WhereExpressions` match the id — if order has no spec test as a pattern, assert by evaluating the compiled criteria against an in-memory list)

```csharp
// tests/unit/Customer.UnitTests/Tenants/TenantByIdSpecTests.cs
using Ardalis.Specification;
using Customers.Application.Tenants.ReadModels;
using Customers.Domain.Entities;
using Xunit;

namespace Customer.UnitTests.Tenants;

public sealed class TenantByIdSpecTests
{
    [Fact]
    public void Matches_OnlyTheTenantWithTheGivenId()
    {
        var wanted = Tenant.Create(Guid.NewGuid(), "acme", "shared", "postgres", false);
        var other = Tenant.Create(Guid.NewGuid(), "other", "dedicated", "postgres", true);
        var spec = new TenantByIdSpec(wanted.Id);

        var result = spec.Evaluate(new[] { wanted, other }).ToList();

        Assert.Single(result);
        Assert.Equal(wanted.Id, result[0].Id);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `nx test --project=Customer.UnitTests`
Expected: FAIL — `TenantByIdSpec` not defined.

- [ ] **Step 3: Implement the spec + projection**

```csharp
// src/services/commerce/customer/Customer.Application/Tenants/ReadModels/TenantByIdSpec.cs
using Ardalis.Specification;
using Customers.Domain.Entities;

namespace Customers.Application.Tenants.ReadModels;

/// <summary>Selects the tenant matching the supplied identifier.</summary>
public sealed class TenantByIdSpec : Specification<Tenant>
{
    /// <summary>Initializes a new instance of the <see cref="TenantByIdSpec"/> class.</summary>
    /// <param name="tenantId">The tenant identifier to match.</param>
    public TenantByIdSpec(Guid tenantId) => Query.Where(tenant => tenant.Id == tenantId);
}
```

```csharp
// src/services/commerce/customer/Customer.Application/Tenants/ReadModels/TenantDatabaseInfo.cs
namespace Customers.Application.Tenants.ReadModels;

/// <summary>Projection of the tenant fields needed for a database-strategy lookup.</summary>
/// <param name="TenantId">The tenant identifier.</param>
/// <param name="Identifier">The unique slug.</param>
/// <param name="DatabaseStrategy">The database strategy.</param>
/// <param name="DatabaseProvider">The database provider.</param>
/// <param name="HasReadReplicas">Whether read replicas exist.</param>
public sealed record TenantDatabaseInfo(
    Guid TenantId,
    string Identifier,
    string DatabaseStrategy,
    string DatabaseProvider,
    bool HasReadReplicas);
```

- [ ] **Step 4: Run to verify it passes**

Run: `nx test --project=Customer.UnitTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/services/commerce/customer/Customer.Application/Tenants
git commit -m "feat(customer): add tenant lookup specification and projection"
```

---

### Task 5: Remote command handler (gRPC server side)

**Files:**
- Create: `src/services/commerce/customer/Customer.Host/Grpc/V1/GetTenantDatabaseInfoCommandHandler.cs`
- Test: `tests/unit/Customer.UnitTests/Grpc/GetTenantDatabaseInfoCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `GetTenantDatabaseInfoCommand`/`TenantDatabaseInfoRpcResult` (Task 1), `IGenericReadRepository<Tenant,Guid>`, `TenantByIdSpec` (Task 4).
- Produces: `GetTenantDatabaseInfoCommandHandler : FastEndpoints.ICommandHandler<GetTenantDatabaseInfoCommand, TenantDatabaseInfoRpcResult>`. Registered in Task 6.

- [ ] **Step 1: Write the failing test** (in-memory fake repository)

```csharp
// tests/unit/Customer.UnitTests/Grpc/GetTenantDatabaseInfoCommandHandlerTests.cs
using Customers.Domain.Entities;
using Customers.Host.Grpc.V1;
using SharedKernel.Grpc.Contracts.Remote.V1.Tenants;
using Xunit;

namespace Customer.UnitTests.Grpc;

public sealed class GetTenantDatabaseInfoCommandHandlerTests
{
    [Fact]
    public async Task ReturnsNotFound_ForInvalidGuid()
    {
        var handler = new GetTenantDatabaseInfoCommandHandler(new FakeTenantReadRepository(null));
        var result = await handler.ExecuteAsync(new GetTenantDatabaseInfoCommand { TenantId = "not-a-guid" }, default);

        Assert.False(result.Found);
        Assert.Contains("GUID", result.ErrorDetail);
    }

    [Fact]
    public async Task ReturnsNotFound_WhenTenantMissing()
    {
        var handler = new GetTenantDatabaseInfoCommandHandler(new FakeTenantReadRepository(null));
        var result = await handler.ExecuteAsync(
            new GetTenantDatabaseInfoCommand { TenantId = Guid.NewGuid().ToString() }, default);

        Assert.False(result.Found);
    }

    [Fact]
    public async Task ReturnsStrategy_WhenTenantExists()
    {
        var id = Guid.NewGuid();
        var tenant = Tenant.Create(id, "acme", "shared", "postgres", false);
        var handler = new GetTenantDatabaseInfoCommandHandler(new FakeTenantReadRepository(tenant));

        var result = await handler.ExecuteAsync(
            new GetTenantDatabaseInfoCommand { TenantId = id.ToString(), ServiceName = "order" }, default);

        Assert.True(result.Found);
        Assert.Equal("shared", result.DatabaseStrategy);
        Assert.Equal("acme", result.Identifier);
    }
}
```

> Implementer: `FakeTenantReadRepository` implements `IGenericReadRepository<Tenant,Guid>` returning the seeded tenant from `FirstOrDefaultAsync(spec, ct)` and throwing `NotImplementedException` on unused members. Place it under `tests/unit/Customer.UnitTests/Grpc/FakeTenantReadRepository.cs`. Match the exact `IGenericReadRepository<,>` member signatures from `SharedKernel.Core.Database`.

- [ ] **Step 2: Run to verify it fails**

Run: `nx test --project=Customer.UnitTests`
Expected: FAIL — handler not defined.

- [ ] **Step 3: Implement the handler** (uses the generic read repository + spec — no per-entity repo, satisfying our arch rules)

```csharp
// src/services/commerce/customer/Customer.Host/Grpc/V1/GetTenantDatabaseInfoCommandHandler.cs
using Customers.Application.Tenants.ReadModels;
using Customers.Domain.Entities;
using FastEndpoints;
using SharedKernel.Core.Database;
using SharedKernel.Grpc.Contracts.Remote.V1.Tenants;

namespace Customers.Host.Grpc.V1;

/// <summary>Handles remote tenant database-metadata lookups for the gateway.</summary>
/// <param name="repository">The generic tenant read repository.</param>
public sealed class GetTenantDatabaseInfoCommandHandler(IGenericReadRepository<Tenant, Guid> repository)
    : ICommandHandler<GetTenantDatabaseInfoCommand, TenantDatabaseInfoRpcResult>
{
    private readonly IGenericReadRepository<Tenant, Guid> repository = repository;

    /// <inheritdoc/>
    public async Task<TenantDatabaseInfoRpcResult> ExecuteAsync(GetTenantDatabaseInfoCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!Guid.TryParse(command.TenantId, out Guid tenantId))
        {
            return new TenantDatabaseInfoRpcResult { Found = false, ErrorDetail = "tenant_id must be a valid GUID." };
        }

        Tenant? tenant = await repository.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct).ConfigureAwait(false);

        if (tenant is null)
        {
            return new TenantDatabaseInfoRpcResult
            {
                Found = false,
                TenantId = command.TenantId,
                ErrorDetail = $"Tenant '{command.TenantId}' was not found.",
            };
        }

        return new TenantDatabaseInfoRpcResult
        {
            Found = true,
            TenantId = tenant.Id.ToString(),
            Identifier = tenant.Identifier,
            DatabaseStrategy = tenant.DatabaseStrategy,
            DatabaseProvider = tenant.DatabaseProvider,
            HasReadReplicas = tenant.HasReadReplicas,
        };
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `nx test --project=Customer.UnitTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/services/commerce/customer/Customer.Host/Grpc tests/unit/Customer.UnitTests/Grpc
git commit -m "feat(customer): add remote tenant database-info command handler"
```

---

### Task 6: Customer host wiring + remote handler server + migration

**Files:**
- Create: `src/services/commerce/customer/Customer.Host/Program.cs` (mirror `Order.Host/Program.cs`)
- Create: `src/services/commerce/customer/Customer.Host/appsettings.json` (+ `appsettings.Development.json`)
- Create: `src/services/commerce/customer/Customer.Host/Database/Migrations/` (generated)
- Modify: `Teck.Platform.slnx` (add the three customer projects)
- Modify: `src/services/commerce/AGENTS.md` (document `customer` as the tenant authority)

**Interfaces:**
- Consumes: `AddCustomerPersistence` (Task 3), `GetTenantDatabaseInfoCommandHandler` (Task 5).

- [ ] **Step 1: Write `Program.cs`** — mirror `Order.Host/Program.cs`, adding the FastEndpoints remote handler server. Order's Program is the template; customer adds `AddHandlerServer()` + `MapHandlers`:

```csharp
using Customers.Host.Database;
using Customers.Host.Grpc.V1;
using FastEndpoints;
using SharedKernel.Grpc.Contracts.Remote.V1.Tenants;
using SharedKernel.Infrastructure.Behaviors;
using SharedKernel.Infrastructure.Hosting;
using SharedKernel.Infrastructure.Messaging.DeadLetter;
using SharedKernel.Infrastructure.Observability;
using Wolverine;

var builder = WebApplication.CreateBuilder(args);
builder.AddTeckCloudObservability();
builder.Services.AddTeckService(typeof(Program).Assembly, builder.Configuration);
builder.AddCustomerPersistence();
builder.Services.AddFastEndpoints();
builder.AddHandlerServer();                       // FastEndpoints messaging-remote gRPC server
builder.Host.UseWolverine(opts =>
{
    opts.AddTeckBehaviors();
    opts.AddTeckDeadLetterPolicy(new DeadLetterOptions());
});

var app = builder.Build();
app.UseTeckService();
app.MapHandlers(registry =>
    registry.Register<GetTenantDatabaseInfoCommand, GetTenantDatabaseInfoCommandHandler, TenantDatabaseInfoRpcResult>());
app.Run();
```

> Implementer: confirm `AddHandlerServer()` / `MapHandlers(...)` names against FastEndpoints 8.1.0 messaging-remote (the reference uses exactly these). If `order`'s `AddTeckService`/`UseTeckService` already calls `AddFastEndpoints`/`UseFastEndpoints`, drop the duplicate here and only add the remote-server pieces. Ensure `Customer.Host.csproj` references `FastEndpoints.Messaging.Remote`.

- [ ] **Step 2: Add the migration** (write context is `CustomerDbContext`; migrations land in the Host assembly per `AddHybridMultiTenantDbContexts(migrationsAssembly: typeof(Program).Assembly, …)`)

Run:
```bash
dotnet ef migrations add InitialCustomer \
  --project src/services/commerce/customer/Customer.Application/Customer.Application.csproj \
  --startup-project src/services/commerce/customer/Customer.Host/Customer.Host.csproj \
  --context CustomerDbContext \
  --output-dir Database/Migrations
```
Expected: a migration is generated under `Customer.Host/Database/Migrations/` creating the `tenants` table with the unique `Identifier` index.

> Implementer: confirm `--output-dir` resolves into the Host project (mirror however `order` is configured; if order has no migration yet, the `migrationsAssembly` is the Host, so pass `--startup-project` = Host and the default output lands in Host). Verify the generated `Up()` matches `TenantConfiguration`.

- [ ] **Step 3: Seed a development tenant** — add an idempotent seed in `Program.cs` migrate path or via `modelBuilder.Entity<Tenant>().HasData(...)` in `TenantConfiguration` (preferred — travels with the migration). Add to `TenantConfiguration.Configure`:

```csharp
        builder.HasData(new
        {
            Id = Guid.Parse("00000000-0000-0000-0000-0000000000a1"),
            Identifier = "dev",
            DatabaseStrategy = "shared",
            DatabaseProvider = "postgres",
            HasReadReplicas = false,
            Status = "active",
        });
```

Re-run the migration add (or add a second migration `SeedDevTenant`) so the seed is captured. Expected: `InsertData` for the dev tenant in the migration.

- [ ] **Step 4: Verify the migration applies on a fresh database**

Run (against a disposable Postgres — use the integration harness's Testcontainers or a local container):
```bash
dotnet ef database update \
  --project src/services/commerce/customer/Customer.Application/Customer.Application.csproj \
  --startup-project src/services/commerce/customer/Customer.Host/Customer.Host.csproj \
  --context CustomerDbContext
```
Expected: applies cleanly; `tenants` table exists with the seeded `dev` row.

- [ ] **Step 5: Integration test — remote handler resolves the seeded tenant**

Create `tests/integration/Customer.IntegrationTests/Customer.IntegrationTests.csproj` (mirror `Order.IntegrationTests`) and a test that boots the customer host against a Testcontainers Postgres, applies migrations, and invokes the handler through the in-process command bus:

```csharp
// tests/integration/Customer.IntegrationTests/GetTenantDatabaseInfoTests.cs
[Fact]
public async Task RemoteHandler_ResolvesSeededDevTenant()
{
    // Arrange: WebApplicationFactory<Customers.Host.Program> with Testcontainers Postgres,
    // run migrations on startup (Program --migrate path).
    var command = new GetTenantDatabaseInfoCommand
    {
        TenantId = "00000000-0000-0000-0000-0000000000a1",
        ServiceName = "order",
    };

    // Act: resolve via the host's command bus (FastEndpoints) or call the handler with a scoped repo.
    var result = await command.ExecuteAsync(ct: default); // in-process FE command bus

    // Assert
    Assert.True(result.Found);
    Assert.Equal("shared", result.DatabaseStrategy);
}
```

> Implementer: prefer driving through the FastEndpoints command bus in-process; if the remote gRPC channel is needed, map `MapRemote` to the test server address as Phase B's gateway does. Reuse `Teck.Platform.IntegrationTests.Shared` for the Postgres container fixture.

Run: `nx test --project=Customer.IntegrationTests`
Expected: PASS.

- [ ] **Step 6: Customer architecture tests** — create `tests/architecture/Customer.Architecture.UnitTests/` mirroring `Order.Architecture.UnitTests`, calling `SharedArchitectureRules.AssertAll(...)`. **Do not** add the `ITenantScoped` aggregate assertion (Tenant is global). Add the endpoint rule call only if the customer host exposes FastEndpoints HTTP endpoints (it does not yet — skip).

Run: `nx test --project=Customer.Architecture.UnitTests`
Expected: PASS.

- [ ] **Step 7: Document + full gate + commit**

Update `src/services/commerce/AGENTS.md` `customer` row to note it is the platform tenant authority (serves `GetTenantDatabaseInfoCommand`).

Run: `nx affected -t build test lint`
Expected: PASS.

```bash
git add src/services/commerce/customer tests/integration/Customer.IntegrationTests \
        tests/architecture/Customer.Architecture.UnitTests Teck.Platform.slnx \
        src/services/commerce/AGENTS.md
git commit -m "feat(customer): host remote tenant-authority handler with initial migration"
```

---

## Self-Review

- **Spec §6 coverage:** contract (Task 1), `Tenant` aggregate (Task 2), three-context persistence + migration target (Task 3, 6), spec/projection (Task 4), remote handler via generic repo — improving on the reference's per-entity repo (Task 5), host wiring + `MapHandlers` + initial migration + seeded tenant + `AGENTS.md` (Task 6). ✓
- **Arch-rule compliance:** handler depends on `IGenericReadRepository` + `Specification`, not a per-entity repo or `DbContext`; `Tenant` correctly excluded from `ITenantScoped`. ✓
- **Placeholder scan:** remaining notes are explicit implementer verifications (FE remote API member names, base-entity signature, EF `--output-dir`) — not deferred work. No TBD/TODO.
- **Type consistency:** `GetTenantDatabaseInfoCommand`/`TenantDatabaseInfoRpcResult` fields, `Tenant.Create(...)`, `TenantByIdSpec`, `AddCustomerPersistence`, `CustomerDbContext`/`CustomerReadDbContext` used identically across tasks. ✓
- **Risk flagged:** Task 6 Step 1 hosting wireup depends on `order`'s `AddTeckService`/`UseTeckService` internals; implementer reconciles duplicate FastEndpoints registration against the real `TeckServiceExtensions`.
