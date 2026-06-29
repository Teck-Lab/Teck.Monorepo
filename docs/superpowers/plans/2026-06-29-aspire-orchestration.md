# Aspire Orchestration + ServiceDefaults Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up .NET Aspire for local orchestration — an AppHost that runs Postgres + Keycloak + Redis + RabbitMQ + the four service hosts, plus a `Teck.ServiceDefaults` project that composes the existing rich Serilog/OpenTelemetry setup and adds Aspire service discovery + standard HTTP resilience — so `aspire run` boots the platform with full traceability in the dashboard.

**Architecture:** A new `Teck.ServiceDefaults` class library exposes `AddServiceDefaults()`, which calls the existing `AddTeckCloudObservability()` (Serilog + OTEL, unchanged) and adds `AddServiceDiscovery()` + `ConfigureHttpClientDefaults(...)`. A new `Teck.AppHost` (`Aspire.AppHost.Sdk`) declares the infra resources and the four projects, wiring connection strings under the names services already read. Services' OTLP exporters already key off `OTEL_EXPORTER_OTLP_ENDPOINT` (injected by the AppHost), so traces/metrics/logs flow to the dashboard with no observability code change.

**Tech Stack:** .NET 10, .NET Aspire 13.4.6 (Keycloak hosting 13.4.6-preview), Microsoft.Extensions.ServiceDiscovery / .Yarp / Http.Resilience 10.7.0, YARP, WolverineFx, Serilog, OpenTelemetry, xunit.v3.

## Global Constraints

- Target framework `net10.0`; nullable + implicit usings on (root `Directory.Build.props`).
- Central package management — **all versions go in `Directory.Packages.props`**, never inline in csproj.
- `TreatWarningsAsErrors=true`; `.editorconfig` allowlist: file-scoped namespaces, ordered usings, **XML docs on public types/members**, IDE0005 (unused usings) is an error. Test projects set `CodeAnalysisTreatWarningsAsErrors=false`.
- Aspire hosting packages pinned to **13.4.6**; `Aspire.Hosting.Keycloak` to **13.4.6-preview.1.26319.6** (only preview exists). `Microsoft.Extensions.ServiceDiscovery`, `Microsoft.Extensions.ServiceDiscovery.Yarp`, `Microsoft.Extensions.Http.Resilience` pinned to **10.7.0**. `Aspire.AppHost.Sdk` **13.4.6** in `global.json`.
- New Aspire projects live under `src/aspire/` (`src/aspire/Teck.ServiceDefaults`, `src/aspire/Teck.AppHost`).
- WolverineFx generated code under `**/Internal/Generated/` is gitignored — never commit it.
- Conventional commits; never create tags or run `nx release` from this branch.
- Frontend (web/Bun) and Redis/RabbitMQ *consumers* are out of scope — Redis/RabbitMQ run as resources and have their connection strings injected, but no service code is wired to them in this plan.

---

### Task 1: Central package versions + AppHost SDK bump

**Files:**
- Modify: `Directory.Packages.props` (Aspire `ItemGroup` + add service-discovery/resilience entries)
- Modify: `global.json` (`Aspire.AppHost.Sdk` version)

**Interfaces:**
- Produces: package versions referenced by all later tasks — `Aspire.Hosting.AppHost` 13.4.6, `Aspire.Hosting.PostgreSQL`/`RabbitMQ`/`Redis`/`Testing` 13.4.6, `Aspire.Hosting.Keycloak` 13.4.6-preview.1.26319.6, `Microsoft.Extensions.ServiceDiscovery`/`.Yarp`/`Http.Resilience` 10.7.0, `CommunityToolkit.Aspire.Hosting.Bun` 13.4.0.

- [ ] **Step 1: Bump existing Aspire versions in `Directory.Packages.props`**

In the `.NET Aspire` `ItemGroup`, set versions:
```xml
<PackageVersion Include="CommunityToolkit.Aspire.Hosting.Bun" Version="13.4.0" />
<PackageVersion Include="Aspire.Hosting.Testing" Version="13.4.6" />
<PackageVersion Include="Aspire.Hosting.Keycloak" Version="13.4.6-preview.1.26319.6" />
<PackageVersion Include="Aspire.Hosting.PostgreSQL" Version="13.4.6" />
<PackageVersion Include="Aspire.Hosting.RabbitMQ" Version="13.4.6" />
<PackageVersion Include="Aspire.Hosting.Redis" Version="13.4.6" />
<PackageVersion Include="Aspire.Npgsql.EntityFrameworkCore.PostgreSQL" Version="13.4.6" />
<PackageVersion Include="Aspire.StackExchange.Redis" Version="13.4.6" />
<PackageVersion Include="Aspire.StackExchange.Redis.DistributedCaching" Version="13.4.6" />
```

- [ ] **Step 2: Add the new package entries** (AppHost SDK package + service discovery + resilience) to the same `ItemGroup`:

```xml
<PackageVersion Include="Aspire.Hosting.AppHost" Version="13.4.6" />
<PackageVersion Include="Microsoft.Extensions.ServiceDiscovery" Version="10.7.0" />
<PackageVersion Include="Microsoft.Extensions.ServiceDiscovery.Yarp" Version="10.7.0" />
<PackageVersion Include="Microsoft.Extensions.Http.Resilience" Version="10.7.0" />
```

- [ ] **Step 3: Bump the AppHost SDK in `global.json`**

```json
"Aspire.AppHost.Sdk": "13.4.6"
```

- [ ] **Step 4: Restore + build to verify nothing breaks**

Run: `dotnet restore Teck.Platform.slnx && dotnet build Teck.Platform.slnx -c Debug --nologo -v q`
Expected: `Build succeeded.` (the Aspire client packages already referenced now resolve at 13.4.6).

- [ ] **Step 5: Commit**

```bash
git add Directory.Packages.props global.json
git commit -m "build(aspire): bump Aspire to 13.4.6 and add service-discovery/resilience versions"
```

---

### Task 2: `Teck.ServiceDefaults` project

**Files:**
- Create: `src/aspire/Teck.ServiceDefaults/Teck.ServiceDefaults.csproj`
- Create: `src/aspire/Teck.ServiceDefaults/TeckServiceDefaultsExtensions.cs`
- Create: `tests/unit/Teck.ServiceDefaults.UnitTests/Teck.ServiceDefaults.UnitTests.csproj`
- Create: `tests/unit/Teck.ServiceDefaults.UnitTests/AddServiceDefaultsTests.cs`

**Interfaces:**
- Consumes: `SharedKernel.Infrastructure.Observability.Extensions.AddTeckCloudObservability(IHostApplicationBuilder)`.
- Produces: `Teck.ServiceDefaults.TeckServiceDefaultsExtensions.AddServiceDefaults(this IHostApplicationBuilder) : IHostApplicationBuilder` and `MapDefaultEndpoints(this WebApplication) : WebApplication`.

- [ ] **Step 1: Create the project file**

`src/aspire/Teck.ServiceDefaults/Teck.ServiceDefaults.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsAspireSharedProject>true</IsAspireSharedProject>
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <PackageReference Include="Microsoft.Extensions.ServiceDiscovery" />
    <PackageReference Include="Microsoft.Extensions.Http.Resilience" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\shared\SharedKernel.Infrastructure\SharedKernel.Infrastructure.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write the failing unit test**

`tests/unit/Teck.ServiceDefaults.UnitTests/AddServiceDefaultsTests.cs`:
```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.ServiceDiscovery;
using Teck.ServiceDefaults;
using Xunit;

namespace Teck.ServiceDefaults.UnitTests;

public sealed class AddServiceDefaultsTests
{
    [Fact]
    public void AddServiceDefaults_RegistersServiceDiscovery()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = string.Empty;

        builder.AddServiceDefaults();
        using var app = builder.Build();

        // Service discovery registers ServiceEndpointResolver in DI.
        Assert.NotNull(app.Services.GetService<ServiceEndpointResolver>());
    }
}
```

`tests/unit/Teck.ServiceDefaults.UnitTests/Teck.ServiceDefaults.UnitTests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>Teck.ServiceDefaults.UnitTests</RootNamespace>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\..\src\aspire\Teck.ServiceDefaults\Teck.ServiceDefaults.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test tests/unit/Teck.ServiceDefaults.UnitTests/Teck.ServiceDefaults.UnitTests.csproj --nologo`
Expected: FAIL — `AddServiceDefaults` / `TeckServiceDefaultsExtensions` does not exist (compile error).

- [ ] **Step 4: Implement `TeckServiceDefaultsExtensions`**

`src/aspire/Teck.ServiceDefaults/TeckServiceDefaultsExtensions.cs`:
```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using SharedKernel.Infrastructure.Observability;

namespace Teck.ServiceDefaults;

/// <summary>
/// Aspire service defaults for Teck hosts: composes the existing Serilog + OpenTelemetry
/// observability and adds Aspire service discovery and standard HTTP resilience.
/// </summary>
public static class TeckServiceDefaultsExtensions
{
    /// <summary>
    /// Adds the Teck service defaults: rich observability (via <c>AddTeckCloudObservability</c>),
    /// service discovery, and standard HTTP resilience for all <see cref="System.Net.Http.HttpClient"/>s.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The same builder for chaining.</returns>
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddTeckCloudObservability();

        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        // Aspire liveness convention, in addition to the existing /health and /ready from AddTeckService.
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    /// <summary>
    /// Maps the Aspire liveness endpoint <c>/alive</c> (checks tagged <c>live</c>). The existing
    /// <c>/health</c> and <c>/ready</c> endpoints are mapped by <c>UseTeckService</c>.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <returns>The same application for chaining.</returns>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapHealthChecks("/alive", new()
        {
            Predicate = registration => registration.Tags.Contains("live"),
        });

        return app;
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/unit/Teck.ServiceDefaults.UnitTests/Teck.ServiceDefaults.UnitTests.csproj --nologo`
Expected: PASS (1 test).

- [ ] **Step 6: Add a second test for resilience + observability composition**

Append to `AddServiceDefaultsTests.cs`:
```csharp
    [Fact]
    public void AddServiceDefaults_ComposesObservabilityAndResilience()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = string.Empty;

        builder.AddServiceDefaults();
        using var app = builder.Build();

        // Serilog logger is registered by AddTeckCloudObservability.
        Assert.NotNull(app.Services.GetService<Serilog.ILogger>());
        // IHttpClientFactory exists because ConfigureHttpClientDefaults was called.
        Assert.NotNull(app.Services.GetService<IHttpClientFactory>());
    }
```

Run: `dotnet test tests/unit/Teck.ServiceDefaults.UnitTests/Teck.ServiceDefaults.UnitTests.csproj --nologo`
Expected: PASS (2 tests).

- [ ] **Step 7: Commit**

```bash
git add src/aspire/Teck.ServiceDefaults tests/unit/Teck.ServiceDefaults.UnitTests
git commit -m "feat(aspire): add Teck.ServiceDefaults composing observability + service discovery"
```

---

### Task 3: Wire `AddServiceDefaults` into the four hosts

**Files:**
- Modify: `src/services/commerce/order/Order.Host/Order.Host.csproj` (project ref), `.../Order.Host/Program.cs`
- Modify: `src/services/commerce/customer/Customer.Host/...` (csproj + Program.cs)
- Modify: `src/services/commerce/catalog/Catalog.Host/...` (csproj + Program.cs)
- Modify: `src/services/gateway/public/Gateway.Public.csproj` + `.../public/Program.cs`

**Interfaces:**
- Consumes: `Teck.ServiceDefaults.TeckServiceDefaultsExtensions.AddServiceDefaults` / `MapDefaultEndpoints`.

- [ ] **Step 1: Add the ServiceDefaults project reference to each host csproj**

In each of the four host `.csproj` files, add inside an `<ItemGroup>`:
```xml
<ProjectReference Include="..\..\..\..\aspire\Teck.ServiceDefaults\Teck.ServiceDefaults.csproj" />
```
(Gateway path is `..\..\aspire\Teck.ServiceDefaults\Teck.ServiceDefaults.csproj` — adjust `..\` depth to reach `src/aspire/` from each project; verify by building.)

- [ ] **Step 2: Swap the observability call in each WolverineFx host**

In `Order.Host/Program.cs`, `Customer.Host/Program.cs`, `Catalog.Host/Program.cs`, replace:
```csharp
builder.AddTeckCloudObservability();
```
with:
```csharp
builder.AddServiceDefaults();
```
and add `using Teck.ServiceDefaults;` (ordered alphabetically among usings). After `app.UseTeckService();` add `app.MapDefaultEndpoints();`.

- [ ] **Step 3: Same swap in the gateway**

In `src/services/gateway/public/Program.cs`, replace the `AddTeckCloudObservability()` call with `builder.AddServiceDefaults();`, add `using Teck.ServiceDefaults;`, and add `app.MapDefaultEndpoints();` after the existing service pipeline setup.

- [ ] **Step 4: Build all hosts**

Run: `dotnet build Teck.Platform.slnx -c Debug --nologo -v q`
Expected: `Build succeeded.`

- [ ] **Step 5: Run the existing integration tests to confirm no regression**

Run:
```bash
dotnet test tests/integration/Order.IntegrationTests/Order.IntegrationTests.csproj --nologo
dotnet test tests/integration/Customer.IntegrationTests/Customer.IntegrationTests.csproj --nologo
dotnet test tests/integration/Gateway.Public.IntegrationTests/Gateway.Public.IntegrationTests.csproj --nologo
```
Expected: all PASS (3, 1, 3 respectively).

- [ ] **Step 6: Verify `/alive` works on a booted host** (codegen tolerance unaffected)

Run: `(cd src/services/commerce/order/Order.Host && ASPNETCORE_ENVIRONMENT=Production dotnet run -c Debug --no-build -- codegen write)` and confirm `EXIT 0` (host still constructs with ServiceDefaults during codegen). Then clean: `find src/services -type d -path '*Internal/Generated' -exec rm -rf {} +`.
Expected: codegen writes handlers, exit 0.

- [ ] **Step 7: Commit**

```bash
git add src/services
git commit -m "feat(aspire): wire AddServiceDefaults + MapDefaultEndpoints into all hosts"
```

---

### Task 4: Gateway YARP service discovery

**Files:**
- Modify: `src/services/gateway/public/Gateway.Public.csproj` (add `Microsoft.Extensions.ServiceDiscovery.Yarp`)
- Modify: `src/services/gateway/public/Program.cs` (add destination resolver)

**Interfaces:**
- Consumes: YARP `IReverseProxyBuilder` from the existing `AddReverseProxy()` call.

- [ ] **Step 1: Add the YARP service-discovery package reference**

In `Gateway.Public.csproj`:
```xml
<PackageReference Include="Microsoft.Extensions.ServiceDiscovery.Yarp" />
```

- [ ] **Step 2: Add the destination resolver to the reverse proxy chain**

In `Program.cs`, on the existing `builder.Services.AddReverseProxy()....` chain, add `.AddServiceDiscoveryDestinationResolver()`:
```csharp
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();
```
(Match the existing chain; only add the resolver call. Add `using Microsoft.Extensions.DependencyInjection;` if not already present — it usually is.)

- [ ] **Step 3: Build**

Run: `dotnet build src/services/gateway/public/Gateway.Public.csproj -c Debug --nologo -v q`
Expected: `Build succeeded.`

- [ ] **Step 4: Run gateway integration tests (E2E flow must still pass)**

Run: `dotnet test tests/integration/Gateway.Public.IntegrationTests/Gateway.Public.IntegrationTests.csproj --nologo`
Expected: PASS (3). The tests use a stub destination, so the resolver is inert there; this confirms no regression.

- [ ] **Step 5: Commit**

```bash
git add src/services/gateway/public
git commit -m "feat(aspire): resolve YARP destinations via service discovery"
```

---

### Task 5: `Teck.AppHost` project

**Files:**
- Create: `src/aspire/Teck.AppHost/Teck.AppHost.csproj`
- Create: `src/aspire/Teck.AppHost/AppHost.cs`
- Create: `src/aspire/Teck.AppHost/realms/teck-realm.json`
- Create: `src/aspire/Teck.AppHost/Properties/launchSettings.json`

**Interfaces:**
- Consumes: the four host projects (as `Projects.Order_Host`, `Projects.Customer_Host`, `Projects.Catalog_Host`, `Projects.Gateway_Public` generated metadata).
- Produces: a runnable `DistributedApplication` named for `DistributedApplicationTestingBuilder` in Task 6 via `Projects.Teck_AppHost`.

- [ ] **Step 1: Create the AppHost project file**

`src/aspire/Teck.AppHost/Teck.AppHost.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Sdk Name="Aspire.AppHost.Sdk" Version="13.4.6" />
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <IsAspireHost>true</IsAspireHost>
    <UserSecretsId>teck-apphost</UserSecretsId>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Aspire.Hosting.AppHost" />
    <PackageReference Include="Aspire.Hosting.PostgreSQL" />
    <PackageReference Include="Aspire.Hosting.RabbitMQ" />
    <PackageReference Include="Aspire.Hosting.Redis" />
    <PackageReference Include="Aspire.Hosting.Keycloak" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\services\commerce\order\Order.Host\Order.Host.csproj" />
    <ProjectReference Include="..\..\services\commerce\customer\Customer.Host\Customer.Host.csproj" />
    <ProjectReference Include="..\..\services\commerce\catalog\Catalog.Host\Catalog.Host.csproj" />
    <ProjectReference Include="..\..\services\gateway\public\Gateway.Public.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create the minimal dev Keycloak realm**

`src/aspire/Teck.AppHost/realms/teck-realm.json` (minimal realm so the issuer/JWKS resolve in dev; not the operator realm):
```json
{
  "realm": "teck",
  "enabled": true,
  "sslRequired": "none",
  "clients": [
    { "clientId": "order-api", "enabled": true, "publicClient": true, "protocol": "openid-connect" },
    { "clientId": "catalog-api", "enabled": true, "publicClient": true, "protocol": "openid-connect" },
    { "clientId": "public-gateway", "enabled": true, "publicClient": true, "protocol": "openid-connect" }
  ]
}
```

- [ ] **Step 3: Write the AppHost composition**

`src/aspire/Teck.AppHost/AppHost.cs`:
```csharp
var builder = DistributedApplication.CreateBuilder(args);

// Infrastructure
var postgres = builder.AddPostgres("postgres").WithDataVolume();
var orderDb = postgres.AddDatabase("order");
var customerDb = postgres.AddDatabase("customer");
var catalogDb = postgres.AddDatabase("catalog");

var rabbitmq = builder.AddRabbitMQ("rabbitmq").WithManagementPlugin();
var redis = builder.AddRedis("redis");

var keycloak = builder.AddKeycloak("keycloak")
    .WithDataVolume()
    .WithRealmImport("./realms");

// Services. ConnectionStrings__{Name} env vars match what each persistence extension reads
// (OrderWrite/OrderRead/Default etc.); redis + rabbitmq references inject their own connection
// names for when a consumer is wired.
var order = builder.AddProject<Projects.Order_Host>("order")
    .WithEnvironment("ConnectionStrings__OrderWrite", orderDb)
    .WithEnvironment("ConnectionStrings__OrderRead", orderDb)
    .WithReference(rabbitmq).WithReference(redis).WithReference(keycloak)
    .WaitFor(orderDb).WaitFor(keycloak);

var customer = builder.AddProject<Projects.Customer_Host>("customer")
    .WithEnvironment("ConnectionStrings__CustomerWrite", customerDb)
    .WithEnvironment("ConnectionStrings__CustomerRead", customerDb)
    .WithReference(rabbitmq).WithReference(redis).WithReference(keycloak)
    .WaitFor(customerDb);

var catalog = builder.AddProject<Projects.Catalog_Host>("catalog")
    .WithEnvironment("ConnectionStrings__CatalogWrite", catalogDb)
    .WithEnvironment("ConnectionStrings__CatalogRead", catalogDb)
    .WithReference(rabbitmq).WithReference(redis).WithReference(keycloak)
    .WaitFor(catalogDb).WaitFor(keycloak);

builder.AddProject<Projects.Gateway_Public>("gateway")
    .WithReference(order).WithReference(customer).WithReference(catalog)
    .WithReference(keycloak).WithReference(redis)
    .WaitFor(order).WaitFor(customer).WaitFor(catalog)
    .WithExternalHttpEndpoints();

builder.Build().Run();
```

- [ ] **Step 4: Add launch settings (Aspire dashboard port already forwarded by devcontainer)**

`src/aspire/Teck.AppHost/Properties/launchSettings.json`:
```json
{
  "$schema": "https://json.schemastore.org/launchsettings.json",
  "profiles": {
    "https": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": true,
      "applicationUrl": "https://localhost:17080;http://localhost:15080",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development",
        "DOTNET_ENVIRONMENT": "Development",
        "ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL": "https://localhost:18889",
        "ASPIRE_DASHBOARD_URL": "https://localhost:18888"
      }
    }
  }
}
```

- [ ] **Step 5: Build the AppHost** (the `Projects.*` metadata is generated from the project references)

Run: `dotnet build src/aspire/Teck.AppHost/Teck.AppHost.csproj -c Debug --nologo -v q`
Expected: `Build succeeded.` (If a `Projects.X_Host` name differs, the build error names the correct generated identifier — use it.)

- [ ] **Step 6: Commit**

```bash
git add src/aspire/Teck.AppHost
git commit -m "feat(aspire): add Teck.AppHost orchestrating infra + services"
```

---

### Task 6: Aspire smoke test

**Files:**
- Create: `tests/integration/Aspire.AppHost.IntegrationTests/Aspire.AppHost.IntegrationTests.csproj`
- Create: `tests/integration/Aspire.AppHost.IntegrationTests/AppHostSmokeTests.cs`

**Interfaces:**
- Consumes: `Projects.Teck_AppHost` and `Aspire.Hosting.Testing.DistributedApplicationTestingBuilder`.

- [ ] **Step 1: Create the test project**

`tests/integration/Aspire.AppHost.IntegrationTests/Aspire.AppHost.IntegrationTests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>Aspire.AppHost.IntegrationTests</RootNamespace>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Aspire.Hosting.Testing" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\..\src\aspire\Teck.AppHost\Teck.AppHost.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write the smoke test**

`tests/integration/Aspire.AppHost.IntegrationTests/AppHostSmokeTests.cs`:
```csharp
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Aspire.AppHost.IntegrationTests;

public sealed class AppHostSmokeTests
{
    [Fact]
    public async Task Gateway_ReportsHealthy_WhenAppHostStarts()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Teck_AppHost>();

        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        await app.ResourceNotifications
            .WaitForResourceHealthyAsync("gateway")
            .WaitAsync(TimeSpan.FromMinutes(5));

        using var client = app.CreateHttpClient("gateway");
        var response = await client.GetAsync("/health");

        Assert.True(response.IsSuccessStatusCode);
    }
}
```

- [ ] **Step 3: Run the smoke test (requires Docker; pulls images on first run)**

Run: `dotnet test tests/integration/Aspire.AppHost.IntegrationTests/Aspire.AppHost.IntegrationTests.csproj --nologo`
Expected: PASS (1). If the gateway health route differs, adjust to `/alive` or the route mapped by `UseTeckService`.

- [ ] **Step 4: Commit**

```bash
git add tests/integration/Aspire.AppHost.IntegrationTests
git commit -m "test(aspire): smoke test booting the AppHost and checking gateway health"
```

---

### Task 7: Solution + docs

**Files:**
- Modify: `Teck.Platform.slnx` (add 3 new projects)
- Modify: `CLAUDE.md` and/or root `AGENTS.md` (short `aspire run` note)

**Interfaces:** none.

- [ ] **Step 1: Add the new projects to `Teck.Platform.slnx`**

Add `<Project Path="..."/>` entries for:
```
src/aspire/Teck.ServiceDefaults/Teck.ServiceDefaults.csproj
src/aspire/Teck.AppHost/Teck.AppHost.csproj
tests/unit/Teck.ServiceDefaults.UnitTests/Teck.ServiceDefaults.UnitTests.csproj
tests/integration/Aspire.AppHost.IntegrationTests/Aspire.AppHost.IntegrationTests.csproj
```

- [ ] **Step 2: Add a short note to `CLAUDE.md`** under Commands:

```markdown
### Local orchestration (Aspire)
`aspire run` (from `src/aspire/Teck.AppHost`) boots Postgres + Keycloak + Redis + RabbitMQ +
the four services with the Aspire dashboard on http://localhost:18888 (traces/metrics/logs).
Redis/RabbitMQ run but are not yet consumed; the web frontend is not yet orchestrated.
```

- [ ] **Step 3: Verify the whole solution builds and the unit/arch tests pass**

Run: `dotnet build Teck.Platform.slnx -c Debug --nologo -v q && dotnet test tests/unit/Teck.ServiceDefaults.UnitTests/Teck.ServiceDefaults.UnitTests.csproj --nologo`
Expected: `Build succeeded.` and PASS.

- [ ] **Step 4: Commit**

```bash
git add Teck.Platform.slnx CLAUDE.md
git commit -m "build(aspire): add Aspire projects to solution; document aspire run"
```

---

## Notes for the implementer

- **`Projects.*` identifiers** are generated by the `Aspire.AppHost.Sdk` from project references; the exact name replaces `.` with `_` (`Order.Host` → `Projects.Order_Host`). If a name doesn't resolve, the build error states the generated name — use it verbatim.
- **`WithReference(redis)` / `WithReference(rabbitmq)`** inject `ConnectionStrings__redis` and `ConnectionStrings__rabbitmq`. The Redis caching extension already reads `redis`; the messaging layer's RabbitMQ connection name is `rabbitmq` (confirm against `SharedKernel.Infrastructure.Messaging` when a consumer is wired — out of scope here).
- **Keycloak `WithRealmImport("./realms")`** imports every `*-realm.json` in the directory at container start. The realm name `teck` must match the `Keycloak:realm` services read; the dev `auth-server-url` is injected by `WithReference(keycloak)` — confirm the services pick up the Aspire-provided Keycloak URL, or set `Keycloak__auth-server-url` via `WithEnvironment` referencing the keycloak endpoint.
- **Service discovery + the gateway:** the YARP destinations (`http://order`) resolve to the Aspire-assigned endpoints because Task 4 added the discovery resolver and the AppHost references the services by those logical names.
```
