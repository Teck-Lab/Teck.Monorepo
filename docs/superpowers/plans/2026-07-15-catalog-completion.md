# Catalog Completion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Finish the `catalog` service to shippable Tier-0 parity by giving its 12 existing WolverineFx handlers an HTTP surface, an EF Core migration, integration/handler test coverage, and (if the platform mechanism exists) a generated OpenAPI spec.

**Architecture:** Catalog's Domain + Application layers already exist and conform to current conventions (repository + `IUnitOfWork`, Mapperly, Ardalis specs, ServiceScan DI, inline integration-event publishing). This plan only adds the missing **Host** pieces: FastEndpoints (Request + Validator + Endpoint per feature, dispatch-only via `IMessageBus.InvokeAsync`), the `InitialCatalog` migration (write context `CatalogDbContext`), and a `Catalog.IntegrationTests` project that boots `Catalog.Host` over Testcontainers Postgres. It mirrors `basket` and `pricing` exactly.

**Tech Stack:** .NET 10, FastEndpoints, WolverineFx, EF Core (Npgsql), Mapperly, ErrorOr, FluentValidation, xUnit v3, Testcontainers, Aspire ServiceDefaults, Finbuckle multi-tenancy, Keycloak auth.

## Global Constraints

- **Isolation:** all work happens in a dedicated git worktree (created at execution time via `superpowers:using-git-worktrees`). Branch base: `main`.
- **Signed commits only:** committer email `jl@tecklab.dk` (matches the GPG key); never bypass signing.
- **Analyzers are build errors** (`TreatWarningsAsErrors=true`); the root `.editorconfig` is an allowlist. Document every public type/member with XML docs; file-scoped namespaces; ordered usings. No new blanket suppressions.
- **Endpoints are dispatch-only.** No mapping, no business logic in the Host — endpoints build the command/query and call `bus.InvokeAsync<T>`. Mapping stays in `Application/**/Mapping/` (Mapperly).
- **ErrorOr→HTTP status is a known, deferred platform gap.** Errored `ErrorOr<T>` returns from a handler surface as HTTP 200 + null body (Wolverine result-type codegen pre-unwraps `ErrorOr<T>`→`T`). Catalog matches its siblings; do **not** attempt a catalog-local fix. Endpoints for `ErrorOr<T>` handlers invoke the **inner** type `T`.
- **Endpoints are auto-discovered** by FastEndpoints via `AddTeckService`/`UseTeckService` (already wired in `Catalog.Host/Program.cs`). No manual endpoint registration.
- **Migrations live in `Catalog.Host`** (`migrationsAssembly: typeof(Program).Assembly`), context is `CatalogDbContext` (declared in `Catalog.Application/Database/`). Migrations must be backward-compatible.
- **Every commit:** run `dotnet build` on the touched project (must be warning-clean) before committing.
- **Route/permission convention** (from `basket`/`pricing`): write endpoints use `Permission => new("catalog", "manage", "public")`; read endpoints `new("catalog", "read", "public")`. `Version(0)`.
- **Reference design:** `docs/superpowers/specs/2026-07-15-catalog-completion-design.md` and the original `2026-06-23-catalog-service-design.md`.

---

## File Structure

**Created — `src/services/commerce/catalog/Catalog.Host/`:**
- `Database/CatalogDbContextDesignTimeFactory.cs` — design-time factory for EF tooling.
- `Database/Migrations/*` — `InitialCatalog` migration + model snapshot (EF-generated).
- `Endpoints/Products/{Feature}Endpoint.cs`, `{Feature}Request.cs`, `{Feature}RequestValidator.cs` — 6 features.
- `Endpoints/Suppliers/{Feature}Endpoint.cs`, `{Feature}Request.cs`, `{Feature}RequestValidator.cs` — 6 features.

**Created — `tests/integration/Catalog.IntegrationTests/`:**
- `Catalog.IntegrationTests.csproj`, `CatalogIntegrationTestBase.cs`, `SharedTestcontainersCollection.cs`, `MockBearerAuthenticationHandler.cs`, and per-flow `*Tests.cs`.

**Created — `tests/unit/Catalog.UnitTests/Application/`:** additional `*HandlerTests.cs` for write/domain-logic handlers.

**Possibly created — `deploy/catalog/base/`:** K8s base manifests (verify; create from template if missing).

**Not modified:** Domain, Application (handlers/DTOs/mappers/specs), existing DbContexts — all already correct.

---

## Task 1: Design-time factory + Initial migration

**Files:**
- Create: `src/services/commerce/catalog/Catalog.Host/Database/CatalogDbContextDesignTimeFactory.cs`
- Create: `src/services/commerce/catalog/Catalog.Host/Database/Migrations/` (EF-generated)

**Interfaces:**
- Consumes: `CatalogDbContext` (`Catalog.Application.Database`).
- Produces: an applied `InitialCatalog` migration; the integration-test fixture (Task 2) relies on migrations existing in `Catalog.Host`.

- [ ] **Step 1: Create the design-time factory** (mirror `BasketDbContextDesignTimeFactory`)

```csharp
using Catalog.Application.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Catalog.Host.Database;

/// <summary>
/// Design-time factory for <see cref="CatalogDbContext"/> used by EF Core migrations tooling.
/// Provides a stub context with a no-op tenant accessor so <c>dotnet ef migrations add</c>
/// can construct the context without a running application host.
/// </summary>
public sealed class CatalogDbContextDesignTimeFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    /// <inheritdoc/>
    public CatalogDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("CATALOG_DESIGN_TIME_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=catalog_design;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<CatalogDbContext>();
        optionsBuilder.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsAssembly(typeof(CatalogDbContextDesignTimeFactory).Assembly.FullName));

        return new CatalogDbContext(optionsBuilder.Options, tenantContextAccessor: null!);
    }
}
```

- [ ] **Step 2: Build the Host to confirm the factory compiles**

Run: `dotnet build src/services/commerce/catalog/Catalog.Host/Catalog.Host.csproj`
Expected: Build succeeded, 0 warnings. If it fails on a missing `Microsoft.EntityFrameworkCore.Design` reference, add `<PackageReference Include="Microsoft.EntityFrameworkCore.Design" />` to `Catalog.Host.csproj` (central-versioned) and rebuild.

- [ ] **Step 3: Generate the Initial migration**

Run:
```bash
dotnet ef migrations add InitialCatalog \
  --project src/services/commerce/catalog/Catalog.Host/Catalog.Host.csproj \
  --startup-project src/services/commerce/catalog/Catalog.Host/Catalog.Host.csproj \
  --context CatalogDbContext \
  --output-dir Database/Migrations
```
Expected: creates `Database/Migrations/<timestamp>_InitialCatalog.cs`, `.Designer.cs`, and `CatalogDbContextModelSnapshot.cs`. (Requires `dotnet-ef`: `dotnet tool restore` or `dotnet tool install --global dotnet-ef` if missing.)

- [ ] **Step 4: Inspect the generated migration for model fidelity**

Open the generated `_InitialCatalog.cs` and confirm the schema matches the domain model:
- `Products`, `Categories` (self-ref `ParentId`), `Suppliers` tables.
- Variants as an owned collection of Product; VariantSuppliers owned by Variant; SupplierPriceHistory owned by VariantSupplier (nested owned tables).
- `Money` (`SellPrice`, `CostPrice`) mapped as owned/complex columns (Amount + Currency).
- `TenantId` column present on tenant-scoped tables.

Expected: all present. If an owned relationship or `Money` is missing, the EF configuration in `Catalog.Application/Database/Configurations/` is incomplete — fix the configuration, delete the migration (`dotnet ef migrations remove`), and regenerate. Do not hand-edit the migration.

- [ ] **Step 5: Commit**

```bash
git add src/services/commerce/catalog/Catalog.Host/Database
git commit -m "feat(catalog): design-time factory + InitialCatalog EF migration"
```

---

## Task 2: Integration-test harness

**Files:**
- Create: `tests/integration/Catalog.IntegrationTests/Catalog.IntegrationTests.csproj`
- Create: `tests/integration/Catalog.IntegrationTests/SharedTestcontainersCollection.cs`
- Create: `tests/integration/Catalog.IntegrationTests/MockBearerAuthenticationHandler.cs`
- Create: `tests/integration/Catalog.IntegrationTests/CatalogIntegrationTestBase.cs`
- Create: `tests/integration/Catalog.IntegrationTests/HostBootTests.cs`

**Interfaces:**
- Consumes: `Catalog.Host` `Program` (public partial, already present), `CatalogDbContext`, `Teck.Platform.IntegrationTests.Shared` (`SharedTestcontainersFixture`).
- Produces: `CatalogIntegrationTestBase` exposing `protected HttpClient Client` — every endpoint integration test (Tasks 3–14) derives from it.

- [ ] **Step 1: Copy `MockBearerAuthenticationHandler.cs` and `SharedTestcontainersCollection.cs`**

Copy both files verbatim from `tests/integration/Basket.IntegrationTests/`, changing only the `namespace` to `Catalog.IntegrationTests`. (`MockBearerAuthenticationHandler` is auth infrastructure identical across services; `SharedTestcontainersCollection` re-declares the shared xUnit collection in this assembly.)

- [ ] **Step 2: Create the project file**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>Catalog.IntegrationTests</RootNamespace>
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
    <ProjectReference Include="..\..\..\src\services\commerce\catalog\Catalog.Application\Catalog.Application.csproj" />
    <ProjectReference Include="..\..\..\src\services\commerce\catalog\Catalog.Host\Catalog.Host.csproj" />
    <ProjectReference Include="..\Teck.Platform.IntegrationTests.Shared\Teck.Platform.IntegrationTests.Shared.csproj" />
    <ProjectReference Include="..\..\..\src\shared\SharedKernel.Infrastructure\SharedKernel.Infrastructure.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Create `CatalogIntegrationTestBase.cs`** (adapt `BasketIntegrationTestBase`; catalog endpoints are not tenant/basket-identity specific, so no identity accessor is needed)

```csharp
// <copyright file="CatalogIntegrationTestBase.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using Finbuckle.MultiTenant.Extensions;
using JasperFx.CommandLine;
using Keycloak.AuthServices.Authorization.Requirements;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Infrastructure.MultiTenant;
using Teck.Platform.IntegrationTests.Shared;

namespace Catalog.IntegrationTests;

/// <summary>
/// Boots <c>Catalog.Host</c> in-memory via <see cref="WebApplicationFactory{TEntryPoint}"/> against a
/// Testcontainers-backed Postgres database, replacing Keycloak JWT auth with a mock handler that always
/// authenticates the request and a permissive protected-resource handler.
/// </summary>
public abstract class CatalogIntegrationTestBase : IDisposable
{
    private readonly SharedTestcontainersFixture fixture;
    private readonly string databaseConnectionString;
    private readonly WebApplicationFactory<Program> factory;

    /// <summary>Initializes a new instance of the <see cref="CatalogIntegrationTestBase"/> class.</summary>
    /// <param name="fixture">The shared Testcontainers fixture.</param>
    protected CatalogIntegrationTestBase(SharedTestcontainersFixture fixture)
    {
        this.fixture = fixture;

        databaseConnectionString = fixture
            .CreateSharedTestDatabaseAsync(
                typeof(Catalog.Application.Database.CatalogDbContext),
                "Catalog.Host")
            .GetAwaiter()
            .GetResult();

        factory = new CatalogWebApplicationFactory(databaseConnectionString);
        Client = factory.CreateClient();
    }

    /// <summary>Gets the HTTP client bound to the in-memory Catalog host.</summary>
    protected HttpClient Client { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        Client.Dispose();
        factory.Dispose();
        fixture.TruncateAllTablesAsync(databaseConnectionString).GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    private sealed class CatalogWebApplicationFactory(string databaseConnectionString)
        : WebApplicationFactory<Program>
    {
        static CatalogWebApplicationFactory() => JasperFxEnvironment.AutoStartHost = true;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:CatalogWrite", databaseConnectionString);
            builder.UseSetting("ConnectionStrings:CatalogRead", databaseConnectionString);
            builder.UseSetting("ConnectionStrings:Default", databaseConnectionString);
            builder.UseSetting("Keycloak:realm", "test");
            builder.UseSetting("Keycloak:auth-server-url", "http://localhost:8080");
            builder.UseSetting("Keycloak:resource", "catalog-api");

            builder.ConfigureTestServices(services =>
            {
                services.AddMultiTenant<TenantDetails>();

                services.AddTransient<MockBearerAuthenticationHandler>();
                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    var bearerScheme = options.Schemes
                        .FirstOrDefault(s => s.Name == MockBearerAuthenticationHandler.SchemeName);
                    if (bearerScheme is not null)
                    {
                        bearerScheme.HandlerType = typeof(MockBearerAuthenticationHandler);
                    }

                    options.DefaultAuthenticateScheme = MockBearerAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = MockBearerAuthenticationHandler.SchemeName;
                    options.DefaultForbidScheme = MockBearerAuthenticationHandler.SchemeName;
                });

                var keycloakHandlerDescriptor = services.FirstOrDefault(
                    d => d.ImplementationType?.Name == "ParameterizedProtectedResourceRequirementHandler");
                if (keycloakHandlerDescriptor is not null)
                {
                    services.Remove(keycloakHandlerDescriptor);
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
```

- [ ] **Step 4: Write the boot smoke test**

```csharp
// <copyright file="HostBootTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using System.Net;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Catalog.IntegrationTests;

[Collection("SharedTestcontainers")]
public sealed class HostBootTests : CatalogIntegrationTestBase
{
    public HostBootTests(SharedTestcontainersFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Host_Boots_AndReturns404_ForUnknownRoute()
    {
        var response = await Client.GetAsync(new Uri("/does-not-exist", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
```

- [ ] **Step 5: Register the project in the solution, run the test**

Run:
```bash
dotnet sln Teck.Platform.slnx add tests/integration/Catalog.IntegrationTests/Catalog.IntegrationTests.csproj
dotnet test tests/integration/Catalog.IntegrationTests/Catalog.IntegrationTests.csproj
```
Expected: `HostBootTests` PASSES (host boots against Testcontainers Postgres, applies `InitialCatalog`, returns 404). This proves the migration from Task 1 applies cleanly end-to-end.

- [ ] **Step 6: Commit**

```bash
git add tests/integration/Catalog.IntegrationTests Teck.Platform.slnx
git commit -m "test(catalog): integration-test harness + host boot smoke test"
```

---

## Endpoint tasks (3–14) — shared shape

Each endpoint task creates three files under `Catalog.Host/Endpoints/{Products|Suppliers}/`:
a `Request` record (route/body-bound), a `Validator<TRequest>`, and an `Endpoint` deriving from
`AuthenticatedEndpoint<TRequest, TResponse>`. TDD order: write the failing integration test first
(route 404s), then add the three files, then the test passes. `TResponse` is the handler's return
type — for `ErrorOr<T>` handlers use the **inner** `T`; for `ErrorOr<Success>` use `Success` and
return 204. FastEndpoints binds route parameters to same-named request properties.

---

## Task 3: CreateProduct endpoint

**Files:**
- Create: `Catalog.Host/Endpoints/Products/CreateProductRequest.cs`, `CreateProductRequestValidator.cs`, `CreateProductEndpoint.cs`
- Test: `tests/integration/Catalog.IntegrationTests/ProductLifecycleTests.cs`

**Interfaces:**
- Consumes: `CreateProductCommand(string Name, string? Description, Guid? CategoryId, string Sku, decimal SellPriceAmount, string SellPriceCurrency) : ICommand<ProductDto>`; `ProductDto` (has `Id`, `Name`, `IsActive`, `Variants`); `VariantDto` (has `IsDefault`, `SellPriceAmount`).
- Produces: `POST /products` → 201 with `ProductDto`. Task 4 reads back the created product.

- [ ] **Step 1: Write the failing integration test**

```csharp
// <copyright file="ProductLifecycleTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using System.Net;
using System.Net.Http.Json;
using Catalog.Application.Products.Responses;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Catalog.IntegrationTests;

[Collection("SharedTestcontainers")]
public sealed class ProductLifecycleTests : CatalogIntegrationTestBase
{
    public ProductLifecycleTests(SharedTestcontainersFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task CreateProduct_ReturnsCreated_WithDefaultVariant()
    {
        var response = await Client.PostAsJsonAsync("/products", new
        {
            Name = "Widget",
            Description = "A widget",
            CategoryId = (Guid?)null,
            Sku = "WIDGET-1",
            SellPriceAmount = 9.99m,
            SellPriceCurrency = "USD",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var product = await response.Content.ReadFromJsonAsync<ProductDto>();
        Assert.NotNull(product);
        Assert.Equal("Widget", product!.Name);
        Assert.NotEqual(Guid.Empty, product.Id);
        var variant = Assert.Single(product.Variants);
        Assert.True(variant.IsDefault);
    }
}
```

- [ ] **Step 2: Run it — expect FAIL (404)**

Run: `dotnet test tests/integration/Catalog.IntegrationTests --filter CreateProduct_ReturnsCreated_WithDefaultVariant`
Expected: FAIL — 404 (route not mapped).

- [ ] **Step 3: Create the Request**

```csharp
namespace Catalog.Host.Endpoints.Products;

/// <summary>Request to create a product with a single default variant.</summary>
/// <param name="Name">The product name.</param>
/// <param name="Description">The optional product description.</param>
/// <param name="CategoryId">The optional owning category.</param>
/// <param name="Sku">The default variant SKU.</param>
/// <param name="SellPriceAmount">The default variant sell price amount.</param>
/// <param name="SellPriceCurrency">The ISO currency code for the sell price.</param>
public sealed record CreateProductRequest(
    string Name,
    string? Description,
    Guid? CategoryId,
    string Sku,
    decimal SellPriceAmount,
    string SellPriceCurrency);
```

- [ ] **Step 4: Create the Validator**

```csharp
using FastEndpoints;
using FluentValidation;

namespace Catalog.Host.Endpoints.Products;

/// <summary>Validates <see cref="CreateProductRequest"/> instances.</summary>
public sealed class CreateProductRequestValidator : Validator<CreateProductRequest>
{
    /// <summary>Initializes a new instance of the <see cref="CreateProductRequestValidator"/> class.</summary>
    public CreateProductRequestValidator()
    {
        RuleFor(request => request.Name).NotEmpty();
        RuleFor(request => request.Sku).NotEmpty();
        RuleFor(request => request.SellPriceAmount).GreaterThanOrEqualTo(0);
        RuleFor(request => request.SellPriceCurrency).NotEmpty().Length(3);
    }
}
```

- [ ] **Step 5: Create the Endpoint**

```csharp
using Catalog.Application.Products.Features.CreateProduct.V1;
using Catalog.Application.Products.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Catalog.Host.Endpoints.Products;

/// <summary>Creates a product.</summary>
/// <param name="bus">The message bus.</param>
public sealed class CreateProductEndpoint(IMessageBus bus)
    : AuthenticatedEndpoint<CreateProductRequest, ProductDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("catalog", "manage", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(CreateProductRequest request, CancellationToken ct)
    {
        var command = new CreateProductCommand(
            request.Name, request.Description, request.CategoryId,
            request.Sku, request.SellPriceAmount, request.SellPriceCurrency);
        var result = await bus.InvokeAsync<ProductDto>(command, ct);
        HttpContext.Response.Headers.Location = $"/products/{result.Id}";
        await Send.ResponseAsync(result, StatusCodes.Status201Created, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Post("/products");
        Version(0);
    }
}
```

- [ ] **Step 6: Run test — expect PASS**

Run: `dotnet test tests/integration/Catalog.IntegrationTests --filter CreateProduct_ReturnsCreated_WithDefaultVariant`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/services/commerce/catalog/Catalog.Host/Endpoints/Products tests/integration/Catalog.IntegrationTests/ProductLifecycleTests.cs
git commit -m "feat(catalog): POST /products endpoint"
```

---

## Task 4: GetProduct endpoint

**Files:**
- Create: `Catalog.Host/Endpoints/Products/GetProductRequest.cs`, `GetProductRequestValidator.cs`, `GetProductEndpoint.cs`
- Test: extend `ProductLifecycleTests.cs`

**Interfaces:**
- Consumes: `GetProductQuery(Guid ProductId) : IQuery<ProductDto>` (handler returns `ErrorOr<ProductDto>` → invoke inner `ProductDto`).
- Produces: `GET /products/{productId}` → 200 `ProductDto`.

- [ ] **Step 1: Add the failing test to `ProductLifecycleTests`**

```csharp
    [Fact]
    public async Task GetProduct_AfterCreate_ReturnsProduct()
    {
        var created = await Client.PostAsJsonAsync("/products", new
        {
            Name = "Gadget", Description = (string?)null, CategoryId = (Guid?)null,
            Sku = "GADGET-1", SellPriceAmount = 5m, SellPriceCurrency = "USD",
        });
        var product = await created.Content.ReadFromJsonAsync<ProductDto>();

        var fetched = await Client.GetAsync(new Uri($"/products/{product!.Id}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        var body = await fetched.Content.ReadFromJsonAsync<ProductDto>();
        Assert.Equal(product.Id, body!.Id);
        Assert.Equal("Gadget", body.Name);
    }
```

- [ ] **Step 2: Run — expect FAIL (404).** `dotnet test ... --filter GetProduct_AfterCreate_ReturnsProduct`

- [ ] **Step 3: Create the Request**

```csharp
namespace Catalog.Host.Endpoints.Products;

/// <summary>Request to fetch a product by identifier.</summary>
/// <param name="ProductId">The product identifier.</param>
public sealed record GetProductRequest(Guid ProductId);
```

- [ ] **Step 4: Create the Validator**

```csharp
using FastEndpoints;
using FluentValidation;

namespace Catalog.Host.Endpoints.Products;

/// <summary>Validates <see cref="GetProductRequest"/> instances.</summary>
public sealed class GetProductRequestValidator : Validator<GetProductRequest>
{
    /// <summary>Initializes a new instance of the <see cref="GetProductRequestValidator"/> class.</summary>
    public GetProductRequestValidator() => RuleFor(request => request.ProductId).NotEmpty();
}
```

- [ ] **Step 5: Create the Endpoint**

```csharp
using Catalog.Application.Products.Features.GetProduct.V1;
using Catalog.Application.Products.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Catalog.Host.Endpoints.Products;

/// <summary>Fetches a product by identifier.</summary>
/// <param name="bus">The message bus.</param>
public sealed class GetProductEndpoint(IMessageBus bus)
    : AuthenticatedEndpoint<GetProductRequest, ProductDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("catalog", "read", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(GetProductRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<ProductDto>(new GetProductQuery(request.ProductId), ct);
        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Get("/products/{productId}");
        Version(0);
    }
}
```

- [ ] **Step 6: Run — expect PASS.**

- [ ] **Step 7: Commit** `feat(catalog): GET /products/{productId} endpoint`

---

## Task 5: ListProducts endpoint

**Files:** `Catalog.Host/Endpoints/Products/ListProductsRequest.cs`, `ListProductsRequestValidator.cs`, `ListProductsEndpoint.cs`; test in `ProductLifecycleTests.cs`.

**Interfaces:** Consumes `ListProductsQuery(Guid? CategoryId) : IQuery<IReadOnlyList<ProductSummaryDto>>` (handler returns `ErrorOr<IReadOnlyList<ProductSummaryDto>>`). Produces `GET /products?categoryId=` → 200 list.

- [ ] **Step 1: Failing test**

```csharp
    [Fact]
    public async Task ListProducts_AfterCreate_IncludesProduct()
    {
        await Client.PostAsJsonAsync("/products", new
        {
            Name = "Listed", Description = (string?)null, CategoryId = (Guid?)null,
            Sku = "LIST-1", SellPriceAmount = 1m, SellPriceCurrency = "USD",
        });

        var list = await Client.GetFromJsonAsync<List<ProductSummaryDto>>("/products");

        Assert.NotNull(list);
        Assert.Contains(list!, p => p.Name == "Listed");
    }
```

- [ ] **Step 2: Run — expect FAIL.**
- [ ] **Step 3: Request** (query-string bound; `CategoryId` optional)

```csharp
namespace Catalog.Host.Endpoints.Products;

/// <summary>Request to list products, optionally filtered by category.</summary>
/// <param name="CategoryId">The optional category filter.</param>
public sealed record ListProductsRequest(Guid? CategoryId);
```

- [ ] **Step 4: Validator** (no rules — optional filter; still declared for consistency)

```csharp
using FastEndpoints;
using FluentValidation;

namespace Catalog.Host.Endpoints.Products;

/// <summary>Validates <see cref="ListProductsRequest"/> instances.</summary>
public sealed class ListProductsRequestValidator : Validator<ListProductsRequest>
{
    /// <summary>Initializes a new instance of the <see cref="ListProductsRequestValidator"/> class.</summary>
    public ListProductsRequestValidator()
    {
    }
}
```

- [ ] **Step 5: Endpoint**

```csharp
using Catalog.Application.Products.Features.ListProducts.V1;
using Catalog.Application.Products.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Catalog.Host.Endpoints.Products;

/// <summary>Lists products.</summary>
/// <param name="bus">The message bus.</param>
public sealed class ListProductsEndpoint(IMessageBus bus)
    : AuthenticatedEndpoint<ListProductsRequest, IReadOnlyList<ProductSummaryDto>>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("catalog", "read", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(ListProductsRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<IReadOnlyList<ProductSummaryDto>>(
            new ListProductsQuery(request.CategoryId), ct);
        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Get("/products");
        Version(0);
    }
}
```

- [ ] **Step 6: Run — expect PASS.**
- [ ] **Step 7: Commit** `feat(catalog): GET /products (list) endpoint`

---

## Task 6: CreateCategory endpoint (+ handler unit test)

**Files:** `Catalog.Host/Endpoints/Products/CreateCategoryRequest.cs`, `CreateCategoryRequestValidator.cs`, `CreateCategoryEndpoint.cs`; unit test `tests/unit/Catalog.UnitTests/Application/CreateCategoryHandlerTests.cs`; integration in `ProductLifecycleTests.cs`.

**Interfaces:** Consumes `CreateCategoryCommand(string Name, string Slug, Guid? ParentId) : ICommand<CategoryDto>`; handler `Handle(CreateCategoryCommand, IGenericWriteRepository<Category, Guid>, IUnitOfWork, CancellationToken) → CategoryDto`; `CategoryDto(Guid Id, string Name, string Slug, Guid? ParentId)`. Produces `POST /categories` → 201 `CategoryDto`.

- [ ] **Step 1: Failing handler unit test**

```csharp
using Catalog.Application.Products.Features.CreateCategory.V1;
using Catalog.Domain.Entities;
using Catalog.UnitTests.TestContext;
using Xunit;

namespace Catalog.UnitTests.Application;

public sealed class CreateCategoryHandlerTests
{
    [Fact]
    public async Task Handle_WithValidCommand_PersistsCategory()
    {
        using var db = CatalogTestContext.CreateInMemory();
        var repository = CatalogTestContext.WriteRepo<Category>(db);
        var unitOfWork = CatalogTestContext.UnitOfWork(db);
        var command = new CreateCategoryCommand("Tools", "tools", null);

        var dto = await CreateCategoryHandler.Handle(command, repository, unitOfWork, CancellationToken.None);

        Assert.Equal("Tools", dto.Name);
        Assert.Equal("tools", dto.Slug);
        Assert.NotEqual(Guid.Empty, dto.Id);
    }
}
```

- [ ] **Step 2: Run — expect PASS** (handler already exists; this back-fills coverage). `dotnet test tests/unit/Catalog.UnitTests --filter CreateCategoryHandlerTests`. If it fails to compile due to a signature mismatch, correct the arrange/act lines to match `CreateCategoryHandler.Handle`.

- [ ] **Step 3: Create Request**

```csharp
namespace Catalog.Host.Endpoints.Products;

/// <summary>Request to create a category.</summary>
/// <param name="Name">The category name.</param>
/// <param name="Slug">The URL slug.</param>
/// <param name="ParentId">The optional parent category.</param>
public sealed record CreateCategoryRequest(string Name, string Slug, Guid? ParentId);
```

- [ ] **Step 4: Create Validator**

```csharp
using FastEndpoints;
using FluentValidation;

namespace Catalog.Host.Endpoints.Products;

/// <summary>Validates <see cref="CreateCategoryRequest"/> instances.</summary>
public sealed class CreateCategoryRequestValidator : Validator<CreateCategoryRequest>
{
    /// <summary>Initializes a new instance of the <see cref="CreateCategoryRequestValidator"/> class.</summary>
    public CreateCategoryRequestValidator()
    {
        RuleFor(request => request.Name).NotEmpty();
        RuleFor(request => request.Slug).NotEmpty();
    }
}
```

- [ ] **Step 5: Create Endpoint**

```csharp
using Catalog.Application.Products.Features.CreateCategory.V1;
using Catalog.Application.Products.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Catalog.Host.Endpoints.Products;

/// <summary>Creates a category.</summary>
/// <param name="bus">The message bus.</param>
public sealed class CreateCategoryEndpoint(IMessageBus bus)
    : AuthenticatedEndpoint<CreateCategoryRequest, CategoryDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("catalog", "manage", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(CreateCategoryRequest request, CancellationToken ct)
    {
        var command = new CreateCategoryCommand(request.Name, request.Slug, request.ParentId);
        var result = await bus.InvokeAsync<CategoryDto>(command, ct);
        HttpContext.Response.Headers.Location = $"/categories/{result.Id}";
        await Send.ResponseAsync(result, StatusCodes.Status201Created, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Post("/categories");
        Version(0);
    }
}
```

- [ ] **Step 6: Add integration test to `ProductLifecycleTests`**

```csharp
    [Fact]
    public async Task CreateCategory_ReturnsCreated()
    {
        var response = await Client.PostAsJsonAsync("/categories", new
        {
            Name = "Hardware", Slug = "hardware", ParentId = (Guid?)null,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var category = await response.Content.ReadFromJsonAsync<CategoryDto>();
        Assert.Equal("hardware", category!.Slug);
    }
```
Add `using Catalog.Application.Products.Responses;` if not already present (it is).

- [ ] **Step 7: Run both tests — expect PASS.** `dotnet test tests/unit/Catalog.UnitTests --filter CreateCategoryHandlerTests` and `dotnet test tests/integration/Catalog.IntegrationTests --filter CreateCategory_ReturnsCreated`.

- [ ] **Step 8: Commit** `feat(catalog): POST /categories endpoint + handler test`

---

## Task 7: AddVariant endpoint (+ handler unit test)

**Files:** `Catalog.Host/Endpoints/Products/AddVariantRequest.cs`, `AddVariantRequestValidator.cs`, `AddVariantEndpoint.cs`; unit test `tests/unit/Catalog.UnitTests/Application/AddVariantHandlerTests.cs`; integration in `ProductLifecycleTests.cs`.

**Interfaces:** Consumes `AddVariantCommand(Guid ProductId, string Sku, decimal SellPriceAmount, string SellPriceCurrency, IReadOnlyList<VariantAttributeInput> Attributes) : ICommand<ErrorOr<VariantDto>>`; `VariantAttributeInput(string Name, string Value)`; handler `Handle(AddVariantCommand, IGenericWriteRepository<Product, Guid>, IUnitOfWork, IMessageBus, CancellationToken) → ErrorOr<VariantDto>`. Produces `POST /products/{productId}/variants` → 201 `VariantDto`.

- [ ] **Step 1: Failing handler unit test** (load-then-mutate → seed with `CreateInMemory(name)`, act with `CreateWithStubbedSave(name)`; assert on returned DTO per the harness contract)

```csharp
using Catalog.Application.Products.Features.AddVariant.V1;
using Catalog.Application.Products.Features.CreateProduct.V1;
using Catalog.Domain.Entities;
using Catalog.UnitTests.TestContext;
using NSubstitute;
using Wolverine;
using Xunit;

namespace Catalog.UnitTests.Application;

public sealed class AddVariantHandlerTests
{
    [Fact]
    public async Task Handle_AddsVariant_WithAttributes()
    {
        var name = $"catalog-{Guid.NewGuid()}";
        Guid productId;
        using (var seed = CatalogTestContext.CreateInMemory(name))
        {
            var repo = CatalogTestContext.WriteRepo<Product>(seed);
            var uow = CatalogTestContext.UnitOfWork(seed);
            var bus = Substitute.For<IMessageBus>();
            var created = await CreateProductHandler.Handle(
                new CreateProductCommand("P", null, null, "P-1", 1m, "USD"), repo, uow, bus, CancellationToken.None);
            productId = created.Id;
        }

        using var act = CatalogTestContext.CreateWithStubbedSave(name);
        var actRepo = CatalogTestContext.WriteRepo<Product>(act);
        var actUow = CatalogTestContext.UnitOfWork(act);
        var actBus = Substitute.For<IMessageBus>();
        var command = new AddVariantCommand(
            productId, "P-1-RED", 2m, "USD",
            new[] { new VariantAttributeInput("Color", "Red") });

        var result = await AddVariantHandler.Handle(command, actRepo, actUow, actBus, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("P-1-RED", result.Value.Sku);
        Assert.False(result.Value.IsDefault);
    }
}
```
(Confirm `VariantDto` exposes `Sku` and `IsDefault`; adjust the two asserts to the DTO's actual property names if they differ.)

- [ ] **Step 2: Run — expect PASS** (handler exists). Fix arrange/act if signature differs.

- [ ] **Step 3: Create Request**

```csharp
using Catalog.Application.Products.Features.AddVariant.V1;

namespace Catalog.Host.Endpoints.Products;

/// <summary>Request to add a variant to an existing product.</summary>
/// <param name="ProductId">The owning product identifier.</param>
/// <param name="Sku">The variant SKU.</param>
/// <param name="SellPriceAmount">The variant sell price amount.</param>
/// <param name="SellPriceCurrency">The ISO currency code.</param>
/// <param name="Attributes">The distinguishing attributes.</param>
public sealed record AddVariantRequest(
    Guid ProductId,
    string Sku,
    decimal SellPriceAmount,
    string SellPriceCurrency,
    IReadOnlyList<VariantAttributeInput> Attributes);
```

- [ ] **Step 4: Create Validator**

```csharp
using FastEndpoints;
using FluentValidation;

namespace Catalog.Host.Endpoints.Products;

/// <summary>Validates <see cref="AddVariantRequest"/> instances.</summary>
public sealed class AddVariantRequestValidator : Validator<AddVariantRequest>
{
    /// <summary>Initializes a new instance of the <see cref="AddVariantRequestValidator"/> class.</summary>
    public AddVariantRequestValidator()
    {
        RuleFor(request => request.ProductId).NotEmpty();
        RuleFor(request => request.Sku).NotEmpty();
        RuleFor(request => request.SellPriceAmount).GreaterThanOrEqualTo(0);
        RuleFor(request => request.SellPriceCurrency).NotEmpty().Length(3);
    }
}
```

- [ ] **Step 5: Create Endpoint**

```csharp
using Catalog.Application.Products.Features.AddVariant.V1;
using Catalog.Application.Products.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Catalog.Host.Endpoints.Products;

/// <summary>Adds a variant to a product.</summary>
/// <param name="bus">The message bus.</param>
public sealed class AddVariantEndpoint(IMessageBus bus)
    : AuthenticatedEndpoint<AddVariantRequest, VariantDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("catalog", "manage", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(AddVariantRequest request, CancellationToken ct)
    {
        var command = new AddVariantCommand(
            request.ProductId, request.Sku, request.SellPriceAmount, request.SellPriceCurrency, request.Attributes);
        var result = await bus.InvokeAsync<VariantDto>(command, ct);
        await Send.ResponseAsync(result, StatusCodes.Status201Created, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Post("/products/{productId}/variants");
        Version(0);
    }
}
```

- [ ] **Step 6: Add integration test to `ProductLifecycleTests`** (create product → add variant → 201)

```csharp
    [Fact]
    public async Task AddVariant_ToExistingProduct_ReturnsCreated()
    {
        var created = await Client.PostAsJsonAsync("/products", new
        {
            Name = "Shirt", Description = (string?)null, CategoryId = (Guid?)null,
            Sku = "SHIRT", SellPriceAmount = 20m, SellPriceCurrency = "USD",
        });
        var product = await created.Content.ReadFromJsonAsync<ProductDto>();

        var response = await Client.PostAsJsonAsync($"/products/{product!.Id}/variants", new
        {
            ProductId = product.Id,
            Sku = "SHIRT-RED-L",
            SellPriceAmount = 22m,
            SellPriceCurrency = "USD",
            Attributes = new[] { new { Name = "Color", Value = "Red" }, new { Name = "Size", Value = "L" } },
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var variant = await response.Content.ReadFromJsonAsync<VariantDto>();
        Assert.Equal("SHIRT-RED-L", variant!.Sku);
    }
```

- [ ] **Step 7: Run unit + integration — expect PASS.**
- [ ] **Step 8: Commit** `feat(catalog): POST /products/{productId}/variants endpoint + handler test`

---

## Task 8: UpdateSellPrice endpoint

**Files:** `Catalog.Host/Endpoints/Products/UpdateSellPriceRequest.cs`, `UpdateSellPriceRequestValidator.cs`, `UpdateSellPriceEndpoint.cs`; integration in `ProductLifecycleTests.cs`. (Handler unit test already exists: `UpdateSellPriceHandlerTests`.)

**Interfaces:** Consumes `UpdateSellPriceCommand(Guid ProductId, Guid VariantId, decimal Amount, string Currency) : ICommand<ErrorOr<VariantDto>>`. Produces `PUT /products/{productId}/variants/{variantId}/sell-price` → 200 `VariantDto`.

- [ ] **Step 1: Failing integration test** (create product → read default variant id → update price → GET shows new price)

```csharp
    [Fact]
    public async Task UpdateSellPrice_ChangesDefaultVariantPrice()
    {
        var created = await Client.PostAsJsonAsync("/products", new
        {
            Name = "Priced", Description = (string?)null, CategoryId = (Guid?)null,
            Sku = "PRICED-1", SellPriceAmount = 10m, SellPriceCurrency = "USD",
        });
        var product = await created.Content.ReadFromJsonAsync<ProductDto>();
        var variantId = product!.Variants[0].Id;

        var updated = await Client.PutAsJsonAsync(
            $"/products/{product.Id}/variants/{variantId}/sell-price",
            new { ProductId = product.Id, VariantId = variantId, Amount = 15m, Currency = "USD" });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var reFetched = await Client.GetFromJsonAsync<ProductDto>($"/products/{product.Id}");
        Assert.Equal(15m, reFetched!.Variants[0].SellPriceAmount);
    }
```
(Confirm `VariantDto` exposes `Id` and `SellPriceAmount`; adjust if names differ.)

- [ ] **Step 2: Run — expect FAIL (404).**
- [ ] **Step 3: Request**

```csharp
namespace Catalog.Host.Endpoints.Products;

/// <summary>Request to change a variant's sell price.</summary>
/// <param name="ProductId">The owning product identifier.</param>
/// <param name="VariantId">The variant identifier.</param>
/// <param name="Amount">The new sell price amount.</param>
/// <param name="Currency">The ISO currency code.</param>
public sealed record UpdateSellPriceRequest(Guid ProductId, Guid VariantId, decimal Amount, string Currency);
```

- [ ] **Step 4: Validator**

```csharp
using FastEndpoints;
using FluentValidation;

namespace Catalog.Host.Endpoints.Products;

/// <summary>Validates <see cref="UpdateSellPriceRequest"/> instances.</summary>
public sealed class UpdateSellPriceRequestValidator : Validator<UpdateSellPriceRequest>
{
    /// <summary>Initializes a new instance of the <see cref="UpdateSellPriceRequestValidator"/> class.</summary>
    public UpdateSellPriceRequestValidator()
    {
        RuleFor(request => request.ProductId).NotEmpty();
        RuleFor(request => request.VariantId).NotEmpty();
        RuleFor(request => request.Amount).GreaterThanOrEqualTo(0);
        RuleFor(request => request.Currency).NotEmpty().Length(3);
    }
}
```

- [ ] **Step 5: Endpoint**

```csharp
using Catalog.Application.Products.Features.UpdateSellPrice.V1;
using Catalog.Application.Products.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Catalog.Host.Endpoints.Products;

/// <summary>Changes a variant's sell price.</summary>
/// <param name="bus">The message bus.</param>
public sealed class UpdateSellPriceEndpoint(IMessageBus bus)
    : AuthenticatedEndpoint<UpdateSellPriceRequest, VariantDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("catalog", "manage", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(UpdateSellPriceRequest request, CancellationToken ct)
    {
        var command = new UpdateSellPriceCommand(request.ProductId, request.VariantId, request.Amount, request.Currency);
        var result = await bus.InvokeAsync<VariantDto>(command, ct);
        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Put("/products/{productId}/variants/{variantId}/sell-price");
        Version(0);
    }
}
```

- [ ] **Step 6: Run — expect PASS.**
- [ ] **Step 7: Commit** `feat(catalog): PUT sell-price endpoint`

---

## Task 9: CreateSupplier endpoint (+ handler unit test)

**Files:** `Catalog.Host/Endpoints/Suppliers/CreateSupplierRequest.cs`, `CreateSupplierRequestValidator.cs`, `CreateSupplierEndpoint.cs`; unit test `CreateSupplierHandlerTests.cs`; integration `tests/integration/Catalog.IntegrationTests/SupplierSourcingTests.cs`.

**Interfaces:** Consumes `CreateSupplierCommand(string Name, string? ContactEmail, string? ContactPhone) : ICommand<SupplierDto>`; handler `Handle(CreateSupplierCommand, IGenericWriteRepository<Supplier, Guid>, IUnitOfWork, CancellationToken) → SupplierDto`; `SupplierDto(Guid Id, string Name, string? ContactEmail, string? ContactPhone, bool IsActive)`. Produces `POST /suppliers` → 201 `SupplierDto`.

- [ ] **Step 1: Failing handler unit test**

```csharp
using Catalog.Application.Suppliers.Features.CreateSupplier.V1;
using Catalog.Domain.Entities;
using Catalog.UnitTests.TestContext;
using Xunit;

namespace Catalog.UnitTests.Application;

public sealed class CreateSupplierHandlerTests
{
    [Fact]
    public async Task Handle_WithValidCommand_PersistsSupplier()
    {
        using var db = CatalogTestContext.CreateInMemory();
        var repository = CatalogTestContext.WriteRepo<Supplier>(db);
        var unitOfWork = CatalogTestContext.UnitOfWork(db);
        var command = new CreateSupplierCommand("Acme", "sales@acme.test", null);

        var dto = await CreateSupplierHandler.Handle(command, repository, unitOfWork, CancellationToken.None);

        Assert.Equal("Acme", dto.Name);
        Assert.True(dto.IsActive);
    }
}
```

- [ ] **Step 2: Run — expect PASS.**
- [ ] **Step 3: Request**

```csharp
namespace Catalog.Host.Endpoints.Suppliers;

/// <summary>Request to create a supplier.</summary>
/// <param name="Name">The supplier name.</param>
/// <param name="ContactEmail">The optional contact email.</param>
/// <param name="ContactPhone">The optional contact phone.</param>
public sealed record CreateSupplierRequest(string Name, string? ContactEmail, string? ContactPhone);
```

- [ ] **Step 4: Validator**

```csharp
using FastEndpoints;
using FluentValidation;

namespace Catalog.Host.Endpoints.Suppliers;

/// <summary>Validates <see cref="CreateSupplierRequest"/> instances.</summary>
public sealed class CreateSupplierRequestValidator : Validator<CreateSupplierRequest>
{
    /// <summary>Initializes a new instance of the <see cref="CreateSupplierRequestValidator"/> class.</summary>
    public CreateSupplierRequestValidator()
    {
        RuleFor(request => request.Name).NotEmpty();
        When(request => request.ContactEmail is not null, () =>
            RuleFor(request => request.ContactEmail!).EmailAddress());
    }
}
```

- [ ] **Step 5: Endpoint**

```csharp
using Catalog.Application.Suppliers.Features.CreateSupplier.V1;
using Catalog.Application.Suppliers.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Catalog.Host.Endpoints.Suppliers;

/// <summary>Creates a supplier.</summary>
/// <param name="bus">The message bus.</param>
public sealed class CreateSupplierEndpoint(IMessageBus bus)
    : AuthenticatedEndpoint<CreateSupplierRequest, SupplierDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("catalog", "manage", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(CreateSupplierRequest request, CancellationToken ct)
    {
        var command = new CreateSupplierCommand(request.Name, request.ContactEmail, request.ContactPhone);
        var result = await bus.InvokeAsync<SupplierDto>(command, ct);
        HttpContext.Response.Headers.Location = $"/suppliers/{result.Id}";
        await Send.ResponseAsync(result, StatusCodes.Status201Created, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Post("/suppliers");
        Version(0);
    }
}
```

- [ ] **Step 6: Failing integration test** (new file `SupplierSourcingTests.cs`)

```csharp
// <copyright file="SupplierSourcingTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using System.Net;
using System.Net.Http.Json;
using Catalog.Application.Products.Responses;
using Catalog.Application.Suppliers.Responses;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Catalog.IntegrationTests;

[Collection("SharedTestcontainers")]
public sealed class SupplierSourcingTests : CatalogIntegrationTestBase
{
    public SupplierSourcingTests(SharedTestcontainersFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task CreateSupplier_ReturnsCreated()
    {
        var response = await Client.PostAsJsonAsync("/suppliers", new
        {
            Name = "Acme", ContactEmail = "sales@acme.test", ContactPhone = (string?)null,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var supplier = await response.Content.ReadFromJsonAsync<SupplierDto>();
        Assert.Equal("Acme", supplier!.Name);
    }
}
```

- [ ] **Step 7: Run unit + integration — expect PASS.**
- [ ] **Step 8: Commit** `feat(catalog): POST /suppliers endpoint + handler test`

---

## Task 10: GetSupplier endpoint

**Files:** `Catalog.Host/Endpoints/Suppliers/GetSupplierRequest.cs`, `GetSupplierRequestValidator.cs`, `GetSupplierEndpoint.cs`; integration in `SupplierSourcingTests.cs`. (Handler unit test already exists: `GetSupplierHandlerTests`.)

**Interfaces:** Consumes `GetSupplierQuery(Guid SupplierId) : IQuery<SupplierDto>`. Produces `GET /suppliers/{supplierId}` → 200 `SupplierDto`.

- [ ] **Step 1: Failing integration test** (add to `SupplierSourcingTests`)

```csharp
    [Fact]
    public async Task GetSupplier_AfterCreate_ReturnsSupplier()
    {
        var created = await Client.PostAsJsonAsync("/suppliers", new
        {
            Name = "Globex", ContactEmail = (string?)null, ContactPhone = (string?)null,
        });
        var supplier = await created.Content.ReadFromJsonAsync<SupplierDto>();

        var fetched = await Client.GetFromJsonAsync<SupplierDto>($"/suppliers/{supplier!.Id}");

        Assert.Equal(supplier.Id, fetched!.Id);
        Assert.Equal("Globex", fetched.Name);
    }
```

- [ ] **Step 2: Run — expect FAIL (404).**
- [ ] **Step 3: Request**

```csharp
namespace Catalog.Host.Endpoints.Suppliers;

/// <summary>Request to fetch a supplier by identifier.</summary>
/// <param name="SupplierId">The supplier identifier.</param>
public sealed record GetSupplierRequest(Guid SupplierId);
```

- [ ] **Step 4: Validator**

```csharp
using FastEndpoints;
using FluentValidation;

namespace Catalog.Host.Endpoints.Suppliers;

/// <summary>Validates <see cref="GetSupplierRequest"/> instances.</summary>
public sealed class GetSupplierRequestValidator : Validator<GetSupplierRequest>
{
    /// <summary>Initializes a new instance of the <see cref="GetSupplierRequestValidator"/> class.</summary>
    public GetSupplierRequestValidator() => RuleFor(request => request.SupplierId).NotEmpty();
}
```

- [ ] **Step 5: Endpoint**

```csharp
using Catalog.Application.Suppliers.Features.GetSupplier.V1;
using Catalog.Application.Suppliers.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Catalog.Host.Endpoints.Suppliers;

/// <summary>Fetches a supplier by identifier.</summary>
/// <param name="bus">The message bus.</param>
public sealed class GetSupplierEndpoint(IMessageBus bus)
    : AuthenticatedEndpoint<GetSupplierRequest, SupplierDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("catalog", "read", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(GetSupplierRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<SupplierDto>(new GetSupplierQuery(request.SupplierId), ct);
        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Get("/suppliers/{supplierId}");
        Version(0);
    }
}
```

- [ ] **Step 6: Run — expect PASS.**
- [ ] **Step 7: Commit** `feat(catalog): GET /suppliers/{supplierId} endpoint`

---

## Task 11: LinkVariantSupplier endpoint (+ handler unit test)

**Files:** `Catalog.Host/Endpoints/Suppliers/LinkVariantSupplierRequest.cs`, `LinkVariantSupplierRequestValidator.cs`, `LinkVariantSupplierEndpoint.cs`; unit test `LinkVariantSupplierHandlerTests.cs`; integration in `SupplierSourcingTests.cs`.

**Interfaces:** Consumes `LinkVariantSupplierCommand(Guid VariantId, Guid SupplierId, decimal CostAmount, string CostCurrency, string SupplierSku, int LeadTimeDays, int MinOrderQuantity, bool IsPreferred) : ICommand<ErrorOr<VariantSupplierDto>>`; handler `Handle(LinkVariantSupplierCommand, IGenericWriteRepository<Product, Guid>, IUnitOfWork, CancellationToken) → ErrorOr<VariantSupplierDto>`. Produces `POST /variants/{variantId}/suppliers` → 201 `VariantSupplierDto`.

- [ ] **Step 1: Failing handler unit test** (seed product + supplier, then link — load-then-mutate on Product)

```csharp
using Catalog.Application.Products.Features.CreateProduct.V1;
using Catalog.Application.Suppliers.Features.CreateSupplier.V1;
using Catalog.Application.Suppliers.Features.LinkVariantSupplier.V1;
using Catalog.Domain.Entities;
using Catalog.UnitTests.TestContext;
using NSubstitute;
using Wolverine;
using Xunit;

namespace Catalog.UnitTests.Application;

public sealed class LinkVariantSupplierHandlerTests
{
    [Fact]
    public async Task Handle_LinksSupplierToVariant()
    {
        var name = $"catalog-{Guid.NewGuid()}";
        Guid variantId;
        Guid supplierId;
        using (var seed = CatalogTestContext.CreateInMemory(name))
        {
            var bus = Substitute.For<IMessageBus>();
            var product = await CreateProductHandler.Handle(
                new CreateProductCommand("P", null, null, "P-1", 1m, "USD"),
                CatalogTestContext.WriteRepo<Product>(seed), CatalogTestContext.UnitOfWork(seed), bus, CancellationToken.None);
            variantId = product.Variants[0].Id;
            var supplier = await CreateSupplierHandler.Handle(
                new CreateSupplierCommand("Acme", null, null),
                CatalogTestContext.WriteRepo<Supplier>(seed), CatalogTestContext.UnitOfWork(seed), CancellationToken.None);
            supplierId = supplier.Id;
        }

        using var act = CatalogTestContext.CreateWithStubbedSave(name);
        var command = new LinkVariantSupplierCommand(variantId, supplierId, 4m, "USD", "ACME-SKU", 7, 10, true);

        var result = await LinkVariantSupplierHandler.Handle(
            command, CatalogTestContext.WriteRepo<Product>(act), CatalogTestContext.UnitOfWork(act), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(supplierId, result.Value.SupplierId);
    }
}
```
(Confirm `VariantSupplierDto` exposes `SupplierId`; adjust if the property name differs.)

- [ ] **Step 2: Run — expect PASS** (fix arrange/act if `CreateProductHandler`'s returned `ProductDto.Variants[0].Id` name differs).
- [ ] **Step 3: Request**

```csharp
namespace Catalog.Host.Endpoints.Suppliers;

/// <summary>Request to link a supplier to a variant.</summary>
/// <param name="VariantId">The variant identifier.</param>
/// <param name="SupplierId">The supplier identifier.</param>
/// <param name="CostAmount">The cost price amount.</param>
/// <param name="CostCurrency">The ISO currency code.</param>
/// <param name="SupplierSku">The supplier's SKU for the variant.</param>
/// <param name="LeadTimeDays">The sourcing lead time in days.</param>
/// <param name="MinOrderQuantity">The minimum order quantity.</param>
/// <param name="IsPreferred">Whether this link is the preferred supplier.</param>
public sealed record LinkVariantSupplierRequest(
    Guid VariantId,
    Guid SupplierId,
    decimal CostAmount,
    string CostCurrency,
    string SupplierSku,
    int LeadTimeDays,
    int MinOrderQuantity,
    bool IsPreferred);
```

- [ ] **Step 4: Validator**

```csharp
using FastEndpoints;
using FluentValidation;

namespace Catalog.Host.Endpoints.Suppliers;

/// <summary>Validates <see cref="LinkVariantSupplierRequest"/> instances.</summary>
public sealed class LinkVariantSupplierRequestValidator : Validator<LinkVariantSupplierRequest>
{
    /// <summary>Initializes a new instance of the <see cref="LinkVariantSupplierRequestValidator"/> class.</summary>
    public LinkVariantSupplierRequestValidator()
    {
        RuleFor(request => request.VariantId).NotEmpty();
        RuleFor(request => request.SupplierId).NotEmpty();
        RuleFor(request => request.CostAmount).GreaterThanOrEqualTo(0);
        RuleFor(request => request.CostCurrency).NotEmpty().Length(3);
        RuleFor(request => request.LeadTimeDays).GreaterThanOrEqualTo(0);
        RuleFor(request => request.MinOrderQuantity).GreaterThan(0);
    }
}
```

- [ ] **Step 5: Endpoint**

```csharp
using Catalog.Application.Suppliers.Features.LinkVariantSupplier.V1;
using Catalog.Application.Suppliers.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Catalog.Host.Endpoints.Suppliers;

/// <summary>Links a supplier to a variant.</summary>
/// <param name="bus">The message bus.</param>
public sealed class LinkVariantSupplierEndpoint(IMessageBus bus)
    : AuthenticatedEndpoint<LinkVariantSupplierRequest, VariantSupplierDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("catalog", "manage", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(LinkVariantSupplierRequest request, CancellationToken ct)
    {
        var command = new LinkVariantSupplierCommand(
            request.VariantId, request.SupplierId, request.CostAmount, request.CostCurrency,
            request.SupplierSku, request.LeadTimeDays, request.MinOrderQuantity, request.IsPreferred);
        var result = await bus.InvokeAsync<VariantSupplierDto>(command, ct);
        await Send.ResponseAsync(result, StatusCodes.Status201Created, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Post("/variants/{variantId}/suppliers");
        Version(0);
    }
}
```

- [ ] **Step 6: Integration test** — deferred to Task 12 (link → update cost → history is one flow). Skip a standalone link test here to avoid duplication.
- [ ] **Step 7: Run unit test — expect PASS.**
- [ ] **Step 8: Commit** `feat(catalog): POST /variants/{variantId}/suppliers endpoint + handler test`

---

## Task 12: UpdateSupplierCost endpoint (+ cost→history integration test)

**Files:** `Catalog.Host/Endpoints/Suppliers/UpdateSupplierCostRequest.cs`, `UpdateSupplierCostRequestValidator.cs`, `UpdateSupplierCostEndpoint.cs`; integration in `SupplierSourcingTests.cs`.

**Interfaces:** Consumes `UpdateSupplierCostCommand(Guid VariantId, Guid SupplierId, decimal CostAmount, string CostCurrency) : ICommand<ErrorOr<VariantSupplierDto>>`. Produces `PUT /variants/{variantId}/suppliers/{supplierId}/cost` → 200 `VariantSupplierDto`. This task's integration test also exercises Task 11's link endpoint and Task 14's history endpoint, proving the design's "update cost → history row written" invariant end-to-end.

- [ ] **Step 1: Failing integration test** (create product+supplier → link → update cost → GET history has ≥1 row)

```csharp
    [Fact]
    public async Task UpdateSupplierCost_WritesHistoryRow()
    {
        var createdProduct = await Client.PostAsJsonAsync("/products", new
        {
            Name = "Sourced", Description = (string?)null, CategoryId = (Guid?)null,
            Sku = "SRC-1", SellPriceAmount = 12m, SellPriceCurrency = "USD",
        });
        var product = await createdProduct.Content.ReadFromJsonAsync<ProductDto>();
        var variantId = product!.Variants[0].Id;

        var createdSupplier = await Client.PostAsJsonAsync("/suppliers", new
        {
            Name = "CostCo", ContactEmail = (string?)null, ContactPhone = (string?)null,
        });
        var supplier = await createdSupplier.Content.ReadFromJsonAsync<SupplierDto>();

        var link = await Client.PostAsJsonAsync($"/variants/{variantId}/suppliers", new
        {
            VariantId = variantId, SupplierId = supplier!.Id, CostAmount = 4m, CostCurrency = "USD",
            SupplierSku = "CC-1", LeadTimeDays = 5, MinOrderQuantity = 1, IsPreferred = true,
        });
        Assert.Equal(HttpStatusCode.Created, link.StatusCode);

        var updated = await Client.PutAsJsonAsync(
            $"/variants/{variantId}/suppliers/{supplier.Id}/cost",
            new { VariantId = variantId, SupplierId = supplier.Id, CostAmount = 5m, CostCurrency = "USD" });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var history = await Client.GetFromJsonAsync<List<SupplierPriceHistoryDto>>(
            $"/variants/{variantId}/suppliers/{supplier.Id}/history");
        Assert.NotNull(history);
        Assert.NotEmpty(history!);
    }
```
Add `using Catalog.Application.Suppliers.Responses;` (present). This test stays red until Tasks 11 and 14 endpoints also exist; run it green at the end of Task 14.

- [ ] **Step 2: Run — expect FAIL (404 on cost route).**
- [ ] **Step 3: Request**

```csharp
namespace Catalog.Host.Endpoints.Suppliers;

/// <summary>Request to change a variant-supplier cost price.</summary>
/// <param name="VariantId">The variant identifier.</param>
/// <param name="SupplierId">The supplier identifier.</param>
/// <param name="CostAmount">The new cost price amount.</param>
/// <param name="CostCurrency">The ISO currency code.</param>
public sealed record UpdateSupplierCostRequest(Guid VariantId, Guid SupplierId, decimal CostAmount, string CostCurrency);
```

- [ ] **Step 4: Validator**

```csharp
using FastEndpoints;
using FluentValidation;

namespace Catalog.Host.Endpoints.Suppliers;

/// <summary>Validates <see cref="UpdateSupplierCostRequest"/> instances.</summary>
public sealed class UpdateSupplierCostRequestValidator : Validator<UpdateSupplierCostRequest>
{
    /// <summary>Initializes a new instance of the <see cref="UpdateSupplierCostRequestValidator"/> class.</summary>
    public UpdateSupplierCostRequestValidator()
    {
        RuleFor(request => request.VariantId).NotEmpty();
        RuleFor(request => request.SupplierId).NotEmpty();
        RuleFor(request => request.CostAmount).GreaterThanOrEqualTo(0);
        RuleFor(request => request.CostCurrency).NotEmpty().Length(3);
    }
}
```

- [ ] **Step 5: Endpoint**

```csharp
using Catalog.Application.Suppliers.Features.UpdateSupplierCost.V1;
using Catalog.Application.Suppliers.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Catalog.Host.Endpoints.Suppliers;

/// <summary>Changes a variant-supplier cost price.</summary>
/// <param name="bus">The message bus.</param>
public sealed class UpdateSupplierCostEndpoint(IMessageBus bus)
    : AuthenticatedEndpoint<UpdateSupplierCostRequest, VariantSupplierDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("catalog", "manage", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(UpdateSupplierCostRequest request, CancellationToken ct)
    {
        var command = new UpdateSupplierCostCommand(
            request.VariantId, request.SupplierId, request.CostAmount, request.CostCurrency);
        var result = await bus.InvokeAsync<VariantSupplierDto>(command, ct);
        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Put("/variants/{variantId}/suppliers/{supplierId}/cost");
        Version(0);
    }
}
```

- [ ] **Step 6: Commit** (test still red until Task 14) `feat(catalog): PUT variant-supplier cost endpoint`

---

## Task 13: SetPreferredSupplier endpoint (+ handler unit test)

**Files:** `Catalog.Host/Endpoints/Suppliers/SetPreferredSupplierRequest.cs`, `SetPreferredSupplierRequestValidator.cs`, `SetPreferredSupplierEndpoint.cs`; unit test `SetPreferredSupplierHandlerTests.cs`.

**Interfaces:** Consumes `SetPreferredSupplierCommand(Guid VariantId, Guid SupplierId) : ICommand<ErrorOr<Success>>`; handler returns `ErrorOr<Success>`. Produces `PUT /variants/{variantId}/suppliers/{supplierId}/preferred` → 204 No Content. Uses `AuthenticatedEndpoint<TRequest, Success>` + `Send.NoContentAsync` (mirrors `RemoveExchangeRateEndpoint`).

- [ ] **Step 1: Failing handler unit test** (link two suppliers, set second preferred, assert success; domain enforces single-preferred)

```csharp
using Catalog.Application.Products.Features.CreateProduct.V1;
using Catalog.Application.Suppliers.Features.CreateSupplier.V1;
using Catalog.Application.Suppliers.Features.LinkVariantSupplier.V1;
using Catalog.Application.Suppliers.Features.SetPreferredSupplier.V1;
using Catalog.Domain.Entities;
using Catalog.UnitTests.TestContext;
using NSubstitute;
using Wolverine;
using Xunit;

namespace Catalog.UnitTests.Application;

public sealed class SetPreferredSupplierHandlerTests
{
    [Fact]
    public async Task Handle_SetsPreferredSupplier_Succeeds()
    {
        var name = $"catalog-{Guid.NewGuid()}";
        Guid variantId;
        Guid supplierId;
        using (var seed = CatalogTestContext.CreateInMemory(name))
        {
            var bus = Substitute.For<IMessageBus>();
            var product = await CreateProductHandler.Handle(
                new CreateProductCommand("P", null, null, "P-1", 1m, "USD"),
                CatalogTestContext.WriteRepo<Product>(seed), CatalogTestContext.UnitOfWork(seed), bus, CancellationToken.None);
            variantId = product.Variants[0].Id;
            var supplier = await CreateSupplierHandler.Handle(
                new CreateSupplierCommand("Acme", null, null),
                CatalogTestContext.WriteRepo<Supplier>(seed), CatalogTestContext.UnitOfWork(seed), CancellationToken.None);
            supplierId = supplier.Id;
            await LinkVariantSupplierHandler.Handle(
                new LinkVariantSupplierCommand(variantId, supplierId, 4m, "USD", "SKU", 3, 1, false),
                CatalogTestContext.WriteRepo<Product>(seed), CatalogTestContext.UnitOfWork(seed), CancellationToken.None);
        }

        using var act = CatalogTestContext.CreateWithStubbedSave(name);
        var result = await SetPreferredSupplierHandler.Handle(
            new SetPreferredSupplierCommand(variantId, supplierId),
            CatalogTestContext.WriteRepo<Product>(act), CatalogTestContext.UnitOfWork(act), CancellationToken.None);

        Assert.False(result.IsError);
    }
}
```

- [ ] **Step 2: Run — expect PASS.**
- [ ] **Step 3: Request**

```csharp
namespace Catalog.Host.Endpoints.Suppliers;

/// <summary>Request to set the preferred supplier for a variant.</summary>
/// <param name="VariantId">The variant identifier.</param>
/// <param name="SupplierId">The supplier identifier to mark preferred.</param>
public sealed record SetPreferredSupplierRequest(Guid VariantId, Guid SupplierId);
```

- [ ] **Step 4: Validator**

```csharp
using FastEndpoints;
using FluentValidation;

namespace Catalog.Host.Endpoints.Suppliers;

/// <summary>Validates <see cref="SetPreferredSupplierRequest"/> instances.</summary>
public sealed class SetPreferredSupplierRequestValidator : Validator<SetPreferredSupplierRequest>
{
    /// <summary>Initializes a new instance of the <see cref="SetPreferredSupplierRequestValidator"/> class.</summary>
    public SetPreferredSupplierRequestValidator()
    {
        RuleFor(request => request.VariantId).NotEmpty();
        RuleFor(request => request.SupplierId).NotEmpty();
    }
}
```

- [ ] **Step 5: Endpoint** (no-content pattern — invoke `Success`, return 204)

```csharp
using Catalog.Application.Suppliers.Features.SetPreferredSupplier.V1;
using ErrorOr;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Catalog.Host.Endpoints.Suppliers;

/// <summary>Sets the preferred supplier for a variant.</summary>
/// <param name="bus">The message bus.</param>
public sealed class SetPreferredSupplierEndpoint(IMessageBus bus)
    : AuthenticatedEndpoint<SetPreferredSupplierRequest, Success>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("catalog", "manage", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(SetPreferredSupplierRequest request, CancellationToken ct)
    {
        var command = new SetPreferredSupplierCommand(request.VariantId, request.SupplierId);
        await bus.InvokeAsync<Success>(command, ct);
        await Send.NoContentAsync(ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Put("/variants/{variantId}/suppliers/{supplierId}/preferred");
        Version(0);
    }
}
```

- [ ] **Step 6: Run unit test — expect PASS.**
- [ ] **Step 7: Commit** `feat(catalog): PUT preferred-supplier endpoint + handler test`

---

## Task 14: GetSupplierPriceHistory endpoint

**Files:** `Catalog.Host/Endpoints/Suppliers/GetSupplierPriceHistoryRequest.cs`, `GetSupplierPriceHistoryRequestValidator.cs`, `GetSupplierPriceHistoryEndpoint.cs`. Green-lights Task 12's integration test.

**Interfaces:** Consumes `GetSupplierPriceHistoryQuery(Guid VariantId, Guid SupplierId) : IQuery<IReadOnlyList<SupplierPriceHistoryDto>>`. Produces `GET /variants/{variantId}/suppliers/{supplierId}/history` → 200 list.

- [ ] **Step 1: Create Request**

```csharp
namespace Catalog.Host.Endpoints.Suppliers;

/// <summary>Request to fetch a variant-supplier cost history.</summary>
/// <param name="VariantId">The variant identifier.</param>
/// <param name="SupplierId">The supplier identifier.</param>
public sealed record GetSupplierPriceHistoryRequest(Guid VariantId, Guid SupplierId);
```

- [ ] **Step 2: Create Validator**

```csharp
using FastEndpoints;
using FluentValidation;

namespace Catalog.Host.Endpoints.Suppliers;

/// <summary>Validates <see cref="GetSupplierPriceHistoryRequest"/> instances.</summary>
public sealed class GetSupplierPriceHistoryRequestValidator : Validator<GetSupplierPriceHistoryRequest>
{
    /// <summary>Initializes a new instance of the <see cref="GetSupplierPriceHistoryRequestValidator"/> class.</summary>
    public GetSupplierPriceHistoryRequestValidator()
    {
        RuleFor(request => request.VariantId).NotEmpty();
        RuleFor(request => request.SupplierId).NotEmpty();
    }
}
```

- [ ] **Step 3: Create Endpoint**

```csharp
using Catalog.Application.Suppliers.Features.GetSupplierPriceHistory.V1;
using Catalog.Application.Suppliers.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Catalog.Host.Endpoints.Suppliers;

/// <summary>Fetches the cost history for a variant-supplier link.</summary>
/// <param name="bus">The message bus.</param>
public sealed class GetSupplierPriceHistoryEndpoint(IMessageBus bus)
    : AuthenticatedEndpoint<GetSupplierPriceHistoryRequest, IReadOnlyList<SupplierPriceHistoryDto>>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("catalog", "read", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(GetSupplierPriceHistoryRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<IReadOnlyList<SupplierPriceHistoryDto>>(
            new GetSupplierPriceHistoryQuery(request.VariantId, request.SupplierId), ct);
        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Get("/variants/{variantId}/suppliers/{supplierId}/history");
        Version(0);
    }
}
```

- [ ] **Step 4: Run the full integration suite — Task 12's `UpdateSupplierCost_WritesHistoryRow` now goes green**

Run: `dotnet test tests/integration/Catalog.IntegrationTests`
Expected: all integration tests PASS (including the cost→history flow that needed Tasks 11, 12, 14).

- [ ] **Step 5: Commit** `feat(catalog): GET variant-supplier history endpoint`

---

## Task 15: OpenAPI spec — establish or defer

**Files:** possibly `specs/catalog-v1-public.json`.

**Context:** `specs/AGENTS.md` says specs are "auto-generated from .NET API endpoints" and consumed by `bun run generate`, gated by `nx validate-specs`. **No sibling service (`order`/`basket`/`pricing`/`inventory`) has a committed spec yet** — the generation step is not established in this repo.

- [ ] **Step 1: Determine whether a generation mechanism exists**

Run: `grep -rn "validate-specs\|generate" nx.json package.json tools/ 2>/dev/null` and inspect `src/packages/api-client/scripts/generate.ts`.
- If a documented target produces `specs/{service}-v1-public.json` from the running Host's OpenAPI document (FastEndpoints groups by the `"public"` audience via `OpenApiAudienceMetadata`), run it for catalog, then `nx validate-specs`, then `bun run generate`. Commit the generated `specs/catalog-v1-public.json` (never hand-edited).

- [ ] **Step 2: If no platform mechanism exists, DEFER (do not invent one)**

Since no sibling has a committed spec, do **not** build a bespoke catalog-only export pipeline. Record the gap in the design doc's follow-ups and skip spec generation. `log`/note this explicitly in the task's completion so it is not silently dropped. (This matches the "match siblings" decision in the completion design.)

- [ ] **Step 3: Commit only if a spec was generated** `chore(catalog): generate catalog-v1-public OpenAPI spec`

---

## Task 16: Deploy base + final full-repo verification

**Files:** possibly `deploy/catalog/base/{deployment,service,kustomization}.yaml`.

- [ ] **Step 1: Verify/create deploy base manifests**

Run: `ls deploy/catalog/base/ 2>/dev/null`.
- If present, confirm image `ghcr.io/teck-lab/teck-monorepo/commerce/catalog` and the `--migrate` init-container pattern.
- If absent, create from `deploy/_template/base` with `SERVICE_NAME=Catalog`, `GROUP=commerce` (mirror an existing service's base such as `deploy/basket/base/`). Base K8s only — no overlays/Helm (those live in Teck.GitOps / Teck.Terraform).

- [ ] **Step 2: Run the full PR gate**

Run:
```bash
nx affected -t build test lint typecheck
nx run Catalog.Architecture.UnitTests:test
```
Expected: all green. The architecture tests confirm no per-entity repos, no LINQ in handlers, Request/Validator types are in the Host (not Application), and layer direction holds.

- [ ] **Step 3: Confirm the service boots under Aspire (optional smoke)**

Run: `aspire run` (from `src/aspire/Teck.AppHost`) and confirm catalog serves `POST /products` + `GET /products/{id}` via the dashboard. Skip if Aspire dependencies are unavailable in the execution environment; the integration suite already proves boot + endpoints against Testcontainers.

- [ ] **Step 4: Final commit / ready for PR**

```bash
git add -A
git commit -m "chore(catalog): deploy base manifests + finalize completion"
```

---

## Self-Review

**Spec coverage (design §3 gap table):**
1. HTTP endpoints (all 12) → Tasks 3–14. ✓
2. Initial EF migration → Task 1. ✓
3. OpenAPI spec → Task 15 (establish-or-defer, honestly scoped since no sibling has one). ✓
4. Test coverage → handler unit tests (Tasks 6, 7, 9, 11, 13) for write/domain-logic handlers; integration tests (Tasks 2–14) for lifecycle, sourcing, cost→history; read handlers covered end-to-end via integration. Coverage split stated explicitly, not silent. ✓
5. Verify wiring (supplier cost → history, all handlers dispatch, boot/migrate) → Task 12 integration test + Task 2 boot + Task 16 gate. ✓

**Design decisions honored:** ErrorOr deferred (endpoints invoke inner `T`, no catalog-local fix); inline event publishing kept (no `EventHandlers/` folder introduced); worktree + signed commits in Global Constraints; endpoints dispatch-only.

**Placeholder scan:** no TBD/TODO. Each code step shows full code. Where a DTO property name can't be confirmed from signatures alone (e.g. `VariantDto.Sku`/`IsDefault`, `VariantSupplierDto.SupplierId`), the step names the exact assertion and instructs adjusting to the actual property — a bounded, explicit verification, not a placeholder.

**Type consistency:** endpoint response generics match handler return inner-types (`ErrorOr<VariantDto>`→`VariantDto`, `ErrorOr<Success>`→`Success`+204); command constructor argument order matches the captured command records; handler-test `Handle(...)` argument lists match the captured signatures (write handlers: repo + `IUnitOfWork` [+ `IMessageBus` for CreateProduct/AddVariant]; read handlers: repo only).

**Known execution dependency:** integration tests require Docker (Testcontainers), as `basket`/`pricing` already do.
