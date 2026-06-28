# Auth Phase B — Public Edge Gateway Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A YARP BFF (`Gateway.Public`) that authenticates callers (Keycloak JWT via the standard ASP.NET pipeline), enforces tenant identity from token claims, resolves per-tenant DB strategy from `customer`, exchanges the user token for a per-service audience token, and forwards trusted headers + the exchanged bearer to `order`.

**Architecture:** Standard `UseAuthentication`/`UseAuthorization` with YARP per-route `AuthorizationPolicy` (fail-closed) replaces the reference's imperative in-middleware auth. A small ordered pipeline of single-responsibility steps — `HeaderFirewall → ResolveTenant → ResolveDbStrategy → ExchangeToken` — runs in `EdgeEnforcementMiddleware`; each returns `Continue` or `Stop(problem)`. Route access policy is bound once at startup into a registry that **refuses to boot** if a non-anonymous route lacks an exchange audience. The `customer` dependency is wrapped in FusionCache fail-safe + circuit breaker so an outage doesn't take the edge down.

**Tech Stack:** .NET 10, YARP (`Yarp.ReverseProxy` + ServiceDiscovery.Yarp), FastEndpoints.Messaging.Remote (gRPC client), Keycloak.AuthServices, FusionCache, Polly (circuit breaker), Treyt.Yarp swagger, xUnit v3.

## Global Constraints

- Project: `src/services/gateway/public/Gateway.Public.csproj`, `<RootNamespace>Gateway.Public</RootNamespace>`, `net10.0`, nullable + implicit usings, `TreatWarningsAsErrors=true`, allowlist `.editorconfig` (XML docs, ordered usings, file-scoped namespaces).
- No service project references — the gateway proxies via YARP and talks to `customer` only over the remote command bus. References allowed: `SharedKernel.Infrastructure`, `SharedKernel.Core`, `SharedKernel.Grpc.Contracts`, `Teck.Cloud.ServiceDefaults`.
- Internal trust headers: `X-TenantId`, `X-Tenant-DbStrategy`; exchanged bearer replaces inbound `Authorization`. Header names are configurable via `EdgeTenantOptions`.
- Reuse SharedKernel: `AddKeycloak`, `IServiceTokenExchangeService`/`ServiceTokenExchangeService`, `ITenantTokenContextResolver`. Do not re-implement them.
- Errors are RFC-7807 `application/problem+json` with `traceId` + stable error codes (spec §8).
- Conventional commits; never tag/release. Run `nx affected -t build test lint` before a task is done.
- Spec reference: `docs/superpowers/specs/2026-06-28-platform-auth-architecture-design.md` §5 and §8. Depends on Phase C (the `GetTenantDatabaseInfoCommand` contract + a running `customer`) and Phase A (endpoint convention, already applied to `order`).

---

### Task 1: Gateway project scaffold + YARP config

**Files:**
- Create: `src/services/gateway/public/Gateway.Public.csproj`
- Create: `src/services/gateway/public/Program.cs` (minimal bootstrap; fleshed out in Task 9)
- Create: `src/services/gateway/public/appsettings.json`, `appsettings.Development.json`
- Modify: `Teck.Platform.slnx`

**Interfaces:**
- Produces: a buildable, runnable gateway host with a YARP route/cluster for `order`. Consumed by all later tasks.

- [ ] **Step 1: Create the csproj** (mirror the reference `Web.Public.Gateway.csproj`, adjusted to our package set)

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>Gateway.Public</RootNamespace>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Yarp.ReverseProxy" />
    <PackageReference Include="Treyt.Yarp.ReverseProxy.Swagger" />
    <PackageReference Include="FastEndpoints.Messaging.Remote" />
    <PackageReference Include="Keycloak.AuthServices.Authentication" />
    <PackageReference Include="Microsoft.Extensions.ServiceDiscovery" />
    <PackageReference Include="Microsoft.Extensions.ServiceDiscovery.Yarp" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\..\shared\SharedKernel.Infrastructure\SharedKernel.Infrastructure.csproj" />
    <ProjectReference Include="..\..\..\shared\SharedKernel.Core\SharedKernel.Core.csproj" />
    <ProjectReference Include="..\..\..\shared\SharedKernel.Grpc.Contracts\SharedKernel.Grpc.Contracts.csproj" />
  </ItemGroup>
</Project>
```

> Implementer: add `Polly` (or `Microsoft.Extensions.Http.Resilience`) to `Directory.Packages.props` + here if not already present (used in Task 6's circuit breaker). Verify `Teck.Cloud.ServiceDefaults` path/name in this repo and add it if observability/service-discovery defaults live there (mirror how `order` references service defaults).

- [ ] **Step 2: Minimal `Program.cs`** (just enough to boot + proxy; auth/middleware added in Task 9)

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();
var app = builder.Build();
app.UseRouting();
app.MapReverseProxy();
app.Run();

namespace Gateway.Public { public partial class Program { } } // for WebApplicationFactory
```

- [ ] **Step 3: `appsettings.json` — YARP routes/clusters for `order` with edge metadata**

```json
{
  "ReverseProxy": {
    "Routes": {
      "order-read": {
        "ClusterId": "order",
        "Match": { "Path": "/orders/{**catch-all}", "Methods": [ "GET" ] },
        "AuthorizationPolicy": "authenticated",
        "Metadata": { "EdgeAccess": "Authenticated" }
      },
      "order-write": {
        "ClusterId": "order",
        "Match": { "Path": "/orders/{**catch-all}", "Methods": [ "POST", "PUT", "DELETE" ] },
        "AuthorizationPolicy": "authenticated",
        "Metadata": { "EdgeAccess": "Authenticated" }
      }
    },
    "Clusters": {
      "order": {
        "Destinations": {
          "primary": { "Address": "http://order", "AccessTokenClientName": "order" }
        }
      }
    }
  },
  "MultiTenancy": {
    "TenantIdHeaderName": "X-TenantId",
    "OrganizationClaimName": "organization",
    "TenantIdClaimName": "tenant_id"
  },
  "Services": { "CustomerApi": { "Url": "http://customer" } }
}
```

- [ ] **Step 4: Build + register in solution**

Run: `nx build Gateway.Public`
Expected: PASS. Add to `Teck.Platform.slnx`; confirm `nx show projects | grep Gateway.Public`.

- [ ] **Step 5: Commit**

```bash
git add src/services/gateway/public Teck.Platform.slnx Directory.Packages.props
git commit -m "feat(gateway): scaffold Gateway.Public YARP host with order route"
```

---

### Task 2: `EdgeAccessPolicy` registry with fail-closed startup validation

**Files:**
- Create: `src/services/gateway/public/Edge/EdgeAccessPolicy.cs`
- Create: `src/services/gateway/public/Edge/EdgeAccessPolicyRegistry.cs`
- Create: `src/services/gateway/public/Edge/EdgeTenantOptions.cs`
- Test: `tests/unit/Gateway.Public.UnitTests/Gateway.Public.UnitTests.csproj` (mirror `Order.UnitTests`)
- Test: `tests/unit/Gateway.Public.UnitTests/Edge/EdgeAccessPolicyRegistryTests.cs`
- Modify: `Teck.Platform.slnx`

**Interfaces:**
- Produces: `enum EdgeAccessMode { Anonymous, TenantFromHeader, Authenticated }`; `record EdgeAccessPolicy(EdgeAccessMode Mode, string? ExchangeAudience)`; `IEdgeAccessPolicyRegistry.ForRoute(string routeId) : EdgeAccessPolicy?`; `EdgeAccessPolicyRegistry.Build(IConfiguration)` which **throws** `InvalidOperationException` if a non-anonymous route has no resolvable `ExchangeAudience`. `EdgeTenantOptions(TenantIdHeaderName, OrganizationClaimName, TenantIdClaimName)` + `GetEdgeTenantOptions(this IConfiguration)`. Consumed by Tasks 5–8, 9.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/unit/Gateway.Public.UnitTests/Edge/EdgeAccessPolicyRegistryTests.cs
using Gateway.Public.Edge;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Gateway.Public.UnitTests.Edge;

public sealed class EdgeAccessPolicyRegistryTests
{
    private static IConfiguration Config(string routeMode, bool withAudience)
    {
        var dict = new Dictionary<string, string?>
        {
            ["ReverseProxy:Routes:r1:ClusterId"] = "order",
            ["ReverseProxy:Routes:r1:Metadata:EdgeAccess"] = routeMode,
        };
        if (withAudience) dict["ReverseProxy:Clusters:order:Destinations:primary:AccessTokenClientName"] = "order";
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public void Build_BindsAudience_FromClusterDestination()
    {
        var registry = EdgeAccessPolicyRegistry.Build(Config("Authenticated", withAudience: true));
        var policy = registry.ForRoute("r1");
        Assert.NotNull(policy);
        Assert.Equal(EdgeAccessMode.Authenticated, policy!.Mode);
        Assert.Equal("order", policy.ExchangeAudience);
    }

    [Fact]
    public void Build_Throws_WhenNonAnonymousRouteHasNoAudience()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            EdgeAccessPolicyRegistry.Build(Config("Authenticated", withAudience: false)));
        Assert.Contains("r1", ex.Message);
    }

    [Fact]
    public void Build_AllowsAnonymousRoute_WithoutAudience()
    {
        var registry = EdgeAccessPolicyRegistry.Build(Config("Anonymous", withAudience: false));
        Assert.Equal(EdgeAccessMode.Anonymous, registry.ForRoute("r1")!.Mode);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `nx test --project=Gateway.Public.UnitTests`
Expected: FAIL — types undefined.

- [ ] **Step 3: Implement the policy + options + registry**

```csharp
// src/services/gateway/public/Edge/EdgeAccessPolicy.cs
namespace Gateway.Public.Edge;

/// <summary>How a route is authenticated and tenant-scoped at the edge.</summary>
public enum EdgeAccessMode
{
    /// <summary>No authentication; tenant resolved from header if present.</summary>
    Anonymous,

    /// <summary>Public route that still requires a tenant, resolved from the header.</summary>
    TenantFromHeader,

    /// <summary>Authenticated route; tenant resolved from token claims.</summary>
    Authenticated,
}

/// <summary>The resolved edge access policy for a YARP route.</summary>
/// <param name="Mode">The access mode.</param>
/// <param name="ExchangeAudience">The Keycloak audience to exchange the user token for (required unless Anonymous).</param>
public sealed record EdgeAccessPolicy(EdgeAccessMode Mode, string? ExchangeAudience);
```

```csharp
// src/services/gateway/public/Edge/EdgeAccessPolicyRegistry.cs
using Microsoft.Extensions.Configuration;

namespace Gateway.Public.Edge;

/// <summary>Resolves the <see cref="EdgeAccessPolicy"/> for a route id.</summary>
public interface IEdgeAccessPolicyRegistry
{
    /// <summary>Gets the policy for the given YARP route id, or null if unknown.</summary>
    /// <param name="routeId">The YARP route id.</param>
    /// <returns>The policy or null.</returns>
    EdgeAccessPolicy? ForRoute(string routeId);
}

/// <summary>Builds and holds the route-id → <see cref="EdgeAccessPolicy"/> map, validated at startup.</summary>
public sealed class EdgeAccessPolicyRegistry : IEdgeAccessPolicyRegistry
{
    private readonly IReadOnlyDictionary<string, EdgeAccessPolicy> policies;

    private EdgeAccessPolicyRegistry(IReadOnlyDictionary<string, EdgeAccessPolicy> policies) => this.policies = policies;

    /// <inheritdoc/>
    public EdgeAccessPolicy? ForRoute(string routeId) =>
        policies.TryGetValue(routeId, out EdgeAccessPolicy? policy) ? policy : null;

    /// <summary>Binds every route's edge policy from configuration; throws if a non-anonymous route lacks an audience.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The validated registry.</returns>
    public static EdgeAccessPolicyRegistry Build(IConfiguration configuration)
    {
        var map = new Dictionary<string, EdgeAccessPolicy>(StringComparer.OrdinalIgnoreCase);

        foreach (IConfigurationSection route in configuration.GetSection("ReverseProxy:Routes").GetChildren())
        {
            string routeId = route.Key;
            string modeText = route["Metadata:EdgeAccess"] ?? nameof(EdgeAccessMode.Authenticated);
            EdgeAccessMode mode = Enum.Parse<EdgeAccessMode>(modeText, ignoreCase: true);

            string? audience = null;
            if (mode != EdgeAccessMode.Anonymous)
            {
                string? clusterId = route["ClusterId"];
                audience = ResolveAudience(configuration, clusterId);
                if (string.IsNullOrWhiteSpace(audience))
                {
                    throw new InvalidOperationException(
                        $"Route '{routeId}' is '{mode}' but has no exchange audience " +
                        $"(set Clusters:{clusterId}:Destinations:*:AccessTokenClientName).");
                }
            }

            map[routeId] = new EdgeAccessPolicy(mode, audience);
        }

        return new EdgeAccessPolicyRegistry(map);
    }

    private static string? ResolveAudience(IConfiguration configuration, string? clusterId)
    {
        if (string.IsNullOrWhiteSpace(clusterId))
        {
            return null;
        }

        foreach (IConfigurationSection destination in
                 configuration.GetSection($"ReverseProxy:Clusters:{clusterId}:Destinations").GetChildren())
        {
            string? name = destination["AccessTokenClientName"];
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name.Trim();
            }
        }

        return null;
    }
}
```

```csharp
// src/services/gateway/public/Edge/EdgeTenantOptions.cs
using Microsoft.Extensions.Configuration;

namespace Gateway.Public.Edge;

/// <summary>Header and claim names used for tenant resolution at the edge.</summary>
/// <param name="TenantIdHeaderName">The trusted tenant id header.</param>
/// <param name="OrganizationClaimName">The organization claim name.</param>
/// <param name="TenantIdClaimName">The tenant id claim name.</param>
public sealed record EdgeTenantOptions(string TenantIdHeaderName, string OrganizationClaimName, string TenantIdClaimName);

/// <summary>Binds <see cref="EdgeTenantOptions"/> from configuration.</summary>
public static class EdgeTenantOptionsExtensions
{
    /// <summary>Reads the edge tenant options from the <c>MultiTenancy</c> section.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The bound options.</returns>
    public static EdgeTenantOptions GetEdgeTenantOptions(this IConfiguration configuration) => new(
        configuration["MultiTenancy:TenantIdHeaderName"] ?? "X-TenantId",
        configuration["MultiTenancy:OrganizationClaimName"] ?? "organization",
        configuration["MultiTenancy:TenantIdClaimName"] ?? "tenant_id");
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `nx test --project=Gateway.Public.UnitTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/services/gateway/public/Edge tests/unit/Gateway.Public.UnitTests Teck.Platform.slnx
git commit -m "feat(gateway): fail-closed edge access-policy registry"
```

---

### Task 3: Edge step abstraction + problem writer + constants

**Files:**
- Create: `src/services/gateway/public/Edge/EdgeStep.cs` (`IEdgeStep`, `EdgeStepResult`, `EdgeProblem`, `EdgeContext`)
- Create: `src/services/gateway/public/Edge/EdgeHeaders.cs` (header/item-key constants)
- Create: `src/services/gateway/public/Edge/EdgeProblemWriter.cs`
- Test: `tests/unit/Gateway.Public.UnitTests/Edge/EdgeProblemWriterTests.cs`

**Interfaces:**
- Produces: `IEdgeStep.ExecuteAsync(EdgeContext, CancellationToken) : Task<EdgeStepResult>`; `EdgeStepResult.Proceed` / `EdgeStepResult.Stop(EdgeProblem)`; `EdgeProblem(int StatusCode, string Title, string Detail, string ErrorCode)`; `EdgeContext` (wraps `HttpContext`, `EdgeAccessPolicy Policy`, mutable `ResolvedTenantId`, `DbStrategy`, `ExchangedToken`); `EdgeHeaders` constants; `EdgeProblemWriter.WriteAsync(HttpContext, EdgeProblem)`. Consumed by Tasks 4–9.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/unit/Gateway.Public.UnitTests/Edge/EdgeProblemWriterTests.cs
using System.Text.Json;
using Gateway.Public.Edge;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Gateway.Public.UnitTests.Edge;

public sealed class EdgeProblemWriterTests
{
    [Fact]
    public async Task WritesProblemJson_WithStatusAndErrorCode()
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();

        await EdgeProblemWriter.WriteAsync(ctx, new EdgeProblem(403, "Tenant mismatch", "nope", "tenant.mismatch"));

        Assert.Equal(403, ctx.Response.StatusCode);
        Assert.Equal("application/problem+json", ctx.Response.ContentType);
        ctx.Response.Body.Position = 0;
        using var doc = JsonDocument.Parse(ctx.Response.Body);
        Assert.Equal(403, doc.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("tenant.mismatch",
            doc.RootElement.GetProperty("errors")[0].GetProperty("name").GetString());
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `nx test --project=Gateway.Public.UnitTests`
Expected: FAIL.

- [ ] **Step 3: Implement the abstraction, constants, and writer**

```csharp
// src/services/gateway/public/Edge/EdgeHeaders.cs
namespace Gateway.Public.Edge;

/// <summary>Trusted internal header and HttpContext item keys used by the edge pipeline.</summary>
public static class EdgeHeaders
{
    /// <summary>Header carrying the resolved tenant id to downstream services.</summary>
    public const string TenantDbStrategy = "X-Tenant-DbStrategy";

    /// <summary>HttpContext.Items key for the exchanged downstream access token.</summary>
    public const string ExchangedTokenItemKey = "Edge:ExchangedAccessToken";

    /// <summary>HttpContext.Items key for the resolved tenant id.</summary>
    public const string ResolvedTenantIdItemKey = "Edge:ResolvedTenantId";
}
```

```csharp
// src/services/gateway/public/Edge/EdgeStep.cs
using Gateway.Public.Edge;
using Microsoft.AspNetCore.Http;

namespace Gateway.Public.Edge;

/// <summary>Mutable per-request edge state passed between steps.</summary>
public sealed class EdgeContext
{
    /// <summary>Initializes a new instance of the <see cref="EdgeContext"/> class.</summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="policy">The resolved route policy.</param>
    public EdgeContext(HttpContext httpContext, EdgeAccessPolicy policy)
    {
        HttpContext = httpContext;
        Policy = policy;
    }

    /// <summary>Gets the current HTTP context.</summary>
    public HttpContext HttpContext { get; }

    /// <summary>Gets the resolved route policy.</summary>
    public EdgeAccessPolicy Policy { get; }

    /// <summary>Gets or sets the resolved tenant id.</summary>
    public string? ResolvedTenantId { get; set; }

    /// <summary>Gets or sets the resolved tenant database strategy.</summary>
    public string? DbStrategy { get; set; }

    /// <summary>Gets or sets the exchanged downstream access token.</summary>
    public string? ExchangedToken { get; set; }
}

/// <summary>An edge problem mapped to RFC-7807 output.</summary>
/// <param name="StatusCode">The HTTP status code.</param>
/// <param name="Title">The problem title.</param>
/// <param name="Detail">The human-readable detail.</param>
/// <param name="ErrorCode">The stable machine error code.</param>
public sealed record EdgeProblem(int StatusCode, string Title, string Detail, string ErrorCode);

/// <summary>The outcome of an edge step.</summary>
public sealed record EdgeStepResult(bool Continue, EdgeProblem? Problem)
{
    /// <summary>A result that lets the pipeline proceed.</summary>
    public static EdgeStepResult Proceed { get; } = new(true, null);

    /// <summary>Creates a short-circuiting result carrying a problem.</summary>
    /// <param name="problem">The problem to write.</param>
    /// <returns>A stop result.</returns>
    public static EdgeStepResult Stop(EdgeProblem problem) => new(false, problem);
}

/// <summary>A single-responsibility step in the edge enforcement pipeline.</summary>
public interface IEdgeStep
{
    /// <summary>Executes the step.</summary>
    /// <param name="context">The edge context.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The step result.</returns>
    Task<EdgeStepResult> ExecuteAsync(EdgeContext context, CancellationToken ct);
}
```

```csharp
// src/services/gateway/public/Edge/EdgeProblemWriter.cs
using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Gateway.Public.Edge;

/// <summary>Writes an <see cref="EdgeProblem"/> as RFC-7807 problem+json.</summary>
public static class EdgeProblemWriter
{
    /// <summary>Writes the problem to the response (no-op if the response already started).</summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="problem">The problem.</param>
    /// <returns>A task.</returns>
    public static async Task WriteAsync(HttpContext context, EdgeProblem problem)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        string traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;

        var details = new ProblemDetails
        {
            Status = problem.StatusCode,
            Title = problem.Title,
            Detail = problem.Detail,
            Type = problem.StatusCode is 401 or 403
                ? "https://tools.ietf.org/html/rfc7235"
                : "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            Instance = context.Request.Path,
        };
        details.Extensions["traceId"] = traceId;
        details.Extensions["errors"] = new[] { new { name = problem.ErrorCode, reason = problem.Detail } };

        context.Response.StatusCode = problem.StatusCode;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(details));
    }
}
```

- [ ] **Step 4: Run to verify it passes** → `nx test --project=Gateway.Public.UnitTests` → PASS.

- [ ] **Step 5: Commit**

```bash
git add src/services/gateway/public/Edge
git commit -m "feat(gateway): edge step abstraction and problem writer"
```

---

### Task 4: `HeaderFirewallStep` (strip client-supplied trust headers)

**Files:**
- Create: `src/services/gateway/public/Edge/Steps/HeaderFirewallStep.cs`
- Test: `tests/unit/Gateway.Public.UnitTests/Edge/Steps/HeaderFirewallStepTests.cs`

**Interfaces:**
- Consumes: `EdgeTenantOptions` (Task 2), `EdgeHeaders`, `IEdgeStep` (Task 3).
- Produces: `HeaderFirewallStep : IEdgeStep`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/unit/Gateway.Public.UnitTests/Edge/Steps/HeaderFirewallStepTests.cs
using Gateway.Public.Edge;
using Gateway.Public.Edge.Steps;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Gateway.Public.UnitTests.Edge.Steps;

public sealed class HeaderFirewallStepTests
{
    [Fact]
    public async Task RemovesClientSuppliedTrustHeaders()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers["X-TenantId"] = "spoofed";
        http.Request.Headers["X-Tenant-DbStrategy"] = "spoofed";
        var options = new EdgeTenantOptions("X-TenantId", "organization", "tenant_id");
        var step = new HeaderFirewallStep(options);

        var result = await step.ExecuteAsync(new EdgeContext(http, new EdgeAccessPolicy(EdgeAccessMode.Authenticated, "order")), default);

        Assert.True(result.Continue);
        Assert.False(http.Request.Headers.ContainsKey("X-TenantId"));
        Assert.False(http.Request.Headers.ContainsKey("X-Tenant-DbStrategy"));
    }
}
```

- [ ] **Step 2: Run → FAIL.**

- [ ] **Step 3: Implement**

```csharp
// src/services/gateway/public/Edge/Steps/HeaderFirewallStep.cs
namespace Gateway.Public.Edge.Steps;

/// <summary>Strips client-supplied trusted internal headers so only the gateway can set them.</summary>
/// <param name="tenantOptions">The edge tenant options.</param>
public sealed class HeaderFirewallStep(EdgeTenantOptions tenantOptions) : IEdgeStep
{
    private readonly EdgeTenantOptions tenantOptions = tenantOptions;

    /// <inheritdoc/>
    public Task<EdgeStepResult> ExecuteAsync(EdgeContext context, CancellationToken ct)
    {
        context.HttpContext.Request.Headers.Remove(tenantOptions.TenantIdHeaderName);
        context.HttpContext.Request.Headers.Remove(EdgeHeaders.TenantDbStrategy);
        return Task.FromResult(EdgeStepResult.Proceed);
    }
}
```

- [ ] **Step 4: Run → PASS. Step 5: Commit** `feat(gateway): header firewall step`.

---

### Task 5: `ResolveTenantStep` (tenant from claims/header with mismatch enforcement)

**Files:**
- Create: `src/services/gateway/public/Edge/Steps/ResolveTenantStep.cs`
- Test: `tests/unit/Gateway.Public.UnitTests/Edge/Steps/ResolveTenantStepTests.cs`

**Interfaces:**
- Consumes: `ITenantTokenContextResolver` (SharedKernel), `EdgeTenantOptions`, `EdgeAccessMode`, `IEdgeStep`.
- Produces: `ResolveTenantStep : IEdgeStep` that sets `context.ResolvedTenantId` + the trusted `X-TenantId` request header, or stops with a problem.

Logic (spec §5/§7): `Anonymous` → no tenant required, proceed. `TenantFromHeader` → require `X-TenantId` header (400 `tenant.header.missing` if absent). `Authenticated` → read tenant ids from the principal via `ITenantTokenContextResolver.ResolveTenantIds(user, orgClaim, tenantClaim)`; 403 `tenant.token.missing` if none; if caller sent `X-TenantId` it must be in the allowed set (403 `tenant.mismatch`), else default to the first. On success set `context.ResolvedTenantId`, write it to the request header, and `HttpContext.Items[EdgeHeaders.ResolvedTenantIdItemKey]`.

- [ ] **Step 1: Write failing tests** — cover: authenticated-no-claim → 403 `tenant.token.missing`; header not in token → 403 `tenant.mismatch`; header in token → proceeds with that tenant; no header → first token tenant; `TenantFromHeader` without header → 400 `tenant.header.missing`. Build the principal with a `ClaimsPrincipal` carrying the org claim; use the real `ITenantTokenContextResolver` from SharedKernel (or a fake returning a fixed list).

```csharp
// tests/unit/Gateway.Public.UnitTests/Edge/Steps/ResolveTenantStepTests.cs  (one representative case)
[Fact]
public async Task Authenticated_HeaderNotInToken_Returns403Mismatch()
{
    var http = new DefaultHttpContext();
    http.User = TestPrincipals.WithOrganizations("tenant-a");   // helper builds ClaimsPrincipal
    http.Request.Headers["X-TenantId"] = "tenant-b";
    var step = new ResolveTenantStep(new FakeTokenResolver("tenant-a"),
        new EdgeTenantOptions("X-TenantId", "organization", "tenant_id"));

    var result = await step.ExecuteAsync(
        new EdgeContext(http, new EdgeAccessPolicy(EdgeAccessMode.Authenticated, "order")), default);

    Assert.False(result.Continue);
    Assert.Equal("tenant.mismatch", result.Problem!.ErrorCode);
    Assert.Equal(403, result.Problem.StatusCode);
}
```

- [ ] **Step 2: Run → FAIL.**

- [ ] **Step 3: Implement** (mirror the reference's authenticated/anonymous branches from `TenantEnforcementMiddleware`, but auth is already guaranteed by the ASP.NET pipeline for `Authenticated` routes, so no imperative `AuthenticateAsync`):

```csharp
// src/services/gateway/public/Edge/Steps/ResolveTenantStep.cs
using Microsoft.AspNetCore.Http;
using SharedKernel.Infrastructure.MultiTenant;

namespace Gateway.Public.Edge.Steps;

/// <summary>Resolves the tenant id from token claims or header and enforces tenant/token agreement.</summary>
/// <param name="tokenContextResolver">Resolves tenant ids from a principal.</param>
/// <param name="tenantOptions">The edge tenant options.</param>
public sealed class ResolveTenantStep(
    ITenantTokenContextResolver tokenContextResolver,
    EdgeTenantOptions tenantOptions) : IEdgeStep
{
    private readonly ITenantTokenContextResolver tokenContextResolver = tokenContextResolver;
    private readonly EdgeTenantOptions tenantOptions = tenantOptions;

    /// <inheritdoc/>
    public Task<EdgeStepResult> ExecuteAsync(EdgeContext context, CancellationToken ct)
    {
        HttpContext http = context.HttpContext;

        if (context.Policy.Mode == EdgeAccessMode.Anonymous)
        {
            return Task.FromResult(EdgeStepResult.Proceed);
        }

        string? headerTenant = TryGetHeader(http, tenantOptions.TenantIdHeaderName);

        if (context.Policy.Mode == EdgeAccessMode.TenantFromHeader)
        {
            if (string.IsNullOrWhiteSpace(headerTenant))
            {
                return Task.FromResult(EdgeStepResult.Stop(new EdgeProblem(
                    400, "Missing tenant header",
                    $"Provide '{tenantOptions.TenantIdHeaderName}' header.", "tenant.header.missing")));
            }

            return Task.FromResult(Apply(context, headerTenant));
        }

        // Authenticated: principal guaranteed by the ASP.NET auth pipeline.
        IReadOnlyList<string> tokenTenants = tokenContextResolver.ResolveTenantIds(
            http.User, tenantOptions.OrganizationClaimName, tenantOptions.TenantIdClaimName);

        if (tokenTenants.Count == 0)
        {
            return Task.FromResult(EdgeStepResult.Stop(new EdgeProblem(
                403, "Missing tenant in token",
                $"Token must contain '{tenantOptions.OrganizationClaimName}' or '{tenantOptions.TenantIdClaimName}'.",
                "tenant.token.missing")));
        }

        if (!string.IsNullOrWhiteSpace(headerTenant))
        {
            if (!tokenTenants.Contains(headerTenant, StringComparer.OrdinalIgnoreCase))
            {
                return Task.FromResult(EdgeStepResult.Stop(new EdgeProblem(
                    403, "Tenant mismatch",
                    $"Header '{tenantOptions.TenantIdHeaderName}' is not allowed by the token.", "tenant.mismatch")));
            }

            return Task.FromResult(Apply(context, headerTenant));
        }

        return Task.FromResult(Apply(context, tokenTenants[0]));
    }

    private EdgeStepResult Apply(EdgeContext context, string tenantId)
    {
        context.ResolvedTenantId = tenantId;
        context.HttpContext.Request.Headers[tenantOptions.TenantIdHeaderName] = tenantId;
        context.HttpContext.Items[EdgeHeaders.ResolvedTenantIdItemKey] = tenantId;
        return EdgeStepResult.Proceed;
    }

    private static string? TryGetHeader(HttpContext http, string name) =>
        http.Request.Headers.TryGetValue(name, out var values) && !string.IsNullOrWhiteSpace(values.ToString())
            ? values.ToString().Trim()
            : null;
}
```

> Implementer: confirm `ITenantTokenContextResolver.ResolveTenantIds` exact signature in `src/shared/SharedKernel.Infrastructure/MultiTenant/ITenantTokenContextResolver.cs` and adjust the call/fake accordingly.

- [ ] **Step 4: Run → PASS. Step 5: Commit** `feat(gateway): tenant resolution step with mismatch enforcement`.

---

### Task 6: `ResolveDbStrategyStep` + resilient remote resolver

**Files:**
- Create: `src/services/gateway/public/Edge/ITenantDatabaseStrategyResolver.cs`
- Create: `src/services/gateway/public/Edge/RemoteTenantDatabaseStrategyResolver.cs`
- Create: `src/services/gateway/public/Edge/Steps/ResolveDbStrategyStep.cs`
- Test: `tests/unit/Gateway.Public.UnitTests/Edge/Steps/ResolveDbStrategyStepTests.cs`

**Interfaces:**
- Consumes: `GetTenantDatabaseInfoCommand`/`TenantDatabaseInfoRpcResult` (Phase C), FusionCache, `EdgeHeaders`.
- Produces: `ITenantDatabaseStrategyResolver.ResolveAsync(tenantId, serviceName, ct) : Task<TenantDbStrategyResult>`; `ResolveDbStrategyStep : IEdgeStep` that sets `context.DbStrategy` + the `X-Tenant-DbStrategy` request header, or stops 503/404.

- [ ] **Step 1: Write the failing step test** (fake resolver; assert header set on success, 503 on failure)

```csharp
// tests/unit/Gateway.Public.UnitTests/Edge/Steps/ResolveDbStrategyStepTests.cs  (representative)
[Fact]
public async Task SetsDbStrategyHeader_OnSuccess()
{
    var http = new DefaultHttpContext();
    var ctx = new EdgeContext(http, new EdgeAccessPolicy(EdgeAccessMode.Authenticated, "order")) { ResolvedTenantId = "t1" };
    var step = new ResolveDbStrategyStep(new FakeResolver(TenantDbStrategyResult.Ok("shared")));

    var result = await step.ExecuteAsync(ctx, default);

    Assert.True(result.Continue);
    Assert.Equal("shared", http.Request.Headers["X-Tenant-DbStrategy"]);
}
```

- [ ] **Step 2: Run → FAIL.**

- [ ] **Step 3: Implement resolver + result + step.** The remote resolver mirrors the reference `RemoteTenantDatabaseStrategyResolver` (uses `command.RemoteExecuteAsync(...)`), wrapped with FusionCache **fail-safe** (serve stale on error) + a circuit breaker. Cache key = tenantId+serviceName; on `RpcException`/null → return the cached value if any (`tenant.lookup.unavailable` 503 only when no cached value).

```csharp
// src/services/gateway/public/Edge/ITenantDatabaseStrategyResolver.cs
namespace Gateway.Public.Edge;

/// <summary>The outcome of a tenant DB-strategy lookup.</summary>
/// <param name="Success">Whether the lookup succeeded.</param>
/// <param name="DatabaseStrategy">The resolved strategy.</param>
/// <param name="StatusCode">The HTTP status to map on failure.</param>
/// <param name="ErrorCode">The machine error code on failure.</param>
/// <param name="ErrorDetail">The human detail on failure.</param>
public sealed record TenantDbStrategyResult(bool Success, string? DatabaseStrategy, int? StatusCode, string? ErrorCode, string? ErrorDetail)
{
    /// <summary>Creates a successful result.</summary>
    /// <param name="strategy">The strategy.</param>
    /// <returns>A success result.</returns>
    public static TenantDbStrategyResult Ok(string strategy) => new(true, strategy, null, null, null);

    /// <summary>Creates a failure result.</summary>
    /// <param name="status">The HTTP status.</param>
    /// <param name="code">The error code.</param>
    /// <param name="detail">The detail.</param>
    /// <returns>A failure result.</returns>
    public static TenantDbStrategyResult Fail(int status, string code, string detail) => new(false, null, status, code, detail);
}

/// <summary>Resolves a tenant's database strategy for downstream routing.</summary>
public interface ITenantDatabaseStrategyResolver
{
    /// <summary>Resolves the database strategy for a tenant + service.</summary>
    /// <param name="tenantId">The tenant id.</param>
    /// <param name="serviceName">The downstream service name (cluster id).</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The lookup result.</returns>
    Task<TenantDbStrategyResult> ResolveAsync(string tenantId, string? serviceName, CancellationToken ct);
}
```

```csharp
// src/services/gateway/public/Edge/Steps/ResolveDbStrategyStep.cs
namespace Gateway.Public.Edge.Steps;

/// <summary>Resolves the tenant DB strategy and forwards it as a trusted header.</summary>
/// <param name="resolver">The strategy resolver.</param>
public sealed class ResolveDbStrategyStep(ITenantDatabaseStrategyResolver resolver) : IEdgeStep
{
    private readonly ITenantDatabaseStrategyResolver resolver = resolver;

    /// <inheritdoc/>
    public async Task<EdgeStepResult> ExecuteAsync(EdgeContext context, CancellationToken ct)
    {
        if (context.Policy.Mode == EdgeAccessMode.Anonymous || string.IsNullOrWhiteSpace(context.ResolvedTenantId))
        {
            return EdgeStepResult.Proceed;
        }

        string? clusterId = context.HttpContext.GetEndpoint()?.DisplayName; // see implementer note
        TenantDbStrategyResult result = await resolver
            .ResolveAsync(context.ResolvedTenantId!, clusterId, ct)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            return EdgeStepResult.Stop(new EdgeProblem(
                result.StatusCode ?? 503, "Tenant lookup failed",
                result.ErrorDetail ?? "Unable to resolve tenant database strategy.",
                result.ErrorCode ?? "tenant.lookup.unavailable"));
        }

        context.DbStrategy = result.DatabaseStrategy;
        context.HttpContext.Request.Headers[EdgeHeaders.TenantDbStrategy] = result.DatabaseStrategy;
        return EdgeStepResult.Proceed;
    }
}
```

> Implementer: derive the downstream service name from the matched YARP route's `ClusterId` (via `IReverseProxyFeature.Route.Config.ClusterId`), not `DisplayName`; pass the cluster id into the `EdgeContext` when the middleware builds it (add a `ClusterId` property to `EdgeContext`) rather than recomputing here. For the remote resolver, mirror the reference `RemoteTenantDatabaseStrategyResolver.cs` body verbatim for the RPC + error mapping, then wrap the call in `fusionCache.GetOrSetAsync(key, _ => rpc(), opts => opts.SetFailSafe(true))` and a Polly circuit breaker so a `customer` outage serves stale or returns a clean 503.

- [ ] **Step 4: Run → PASS. Step 5: Commit** `feat(gateway): resilient tenant DB-strategy resolution step`.

---

### Task 7: `ExchangeTokenStep` (reuse SharedKernel token exchange)

**Files:**
- Create: `src/services/gateway/public/Edge/Steps/ExchangeTokenStep.cs`
- Test: `tests/unit/Gateway.Public.UnitTests/Edge/Steps/ExchangeTokenStepTests.cs`

**Interfaces:**
- Consumes: `IServiceTokenExchangeService` (SharedKernel), `TokenExchangeException`, `EdgeAccessPolicy.ExchangeAudience`, `EdgeHeaders`.
- Produces: `ExchangeTokenStep : IEdgeStep` that sets `context.ExchangedToken` + `HttpContext.Items[EdgeHeaders.ExchangedTokenItemKey]`, or stops 401/403.

- [ ] **Step 1: Write failing tests** — success path sets the item; `TokenExchangeException` with `IsAuthFailure` + expired description → 401 `authorization.token.expired`; denied → 403 `authorization.token_exchange_denied`. Use a fake `IServiceTokenExchangeService`.

- [ ] **Step 2: Run → FAIL.**

- [ ] **Step 3: Implement** (mirror the reference `ExchangeTokenForRouteAsync` error mapping; audience comes from `context.Policy.ExchangeAudience`, which the registry guaranteed is non-null for non-anonymous routes):

```csharp
// src/services/gateway/public/Edge/Steps/ExchangeTokenStep.cs
using Microsoft.AspNetCore.Authentication;
using SharedKernel.Infrastructure.Auth;

namespace Gateway.Public.Edge.Steps;

/// <summary>Exchanges the inbound user token for a downstream audience token.</summary>
/// <param name="exchangeService">The token exchange service.</param>
public sealed class ExchangeTokenStep(IServiceTokenExchangeService exchangeService) : IEdgeStep
{
    private readonly IServiceTokenExchangeService exchangeService = exchangeService;

    /// <inheritdoc/>
    public async Task<EdgeStepResult> ExecuteAsync(EdgeContext context, CancellationToken ct)
    {
        string? audience = context.Policy.ExchangeAudience;
        if (string.IsNullOrWhiteSpace(audience))
        {
            return EdgeStepResult.Proceed; // anonymous route
        }

        string? inbound = ExtractBearer(context.HttpContext.Request.Headers.Authorization.ToString())
            ?? await context.HttpContext.GetTokenAsync("access_token").ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(inbound))
        {
            return EdgeStepResult.Proceed; // nothing to exchange (e.g. anonymous-but-tenant route)
        }

        try
        {
            ServiceTokenResult exchanged = await exchangeService
                .ExchangeTokenAsync(inbound, audience!, context.ResolvedTenantId ?? "edge-no-tenant", ct)
                .ConfigureAwait(false);

            context.ExchangedToken = exchanged.AccessToken;
            context.HttpContext.Items[EdgeHeaders.ExchangedTokenItemKey] = exchanged.AccessToken;
            return EdgeStepResult.Proceed;
        }
        catch (TokenExchangeException ex) when (ex.IsAuthFailure)
        {
            int status = ex.StatusCode is 401 or 403 ? ex.StatusCode : 401;
            bool expired = status == 401 && (ex.Description?.Contains("expired", StringComparison.OrdinalIgnoreCase) ?? false);
            return EdgeStepResult.Stop(new EdgeProblem(
                status,
                status == 401 ? "Unauthorized" : "Forbidden",
                expired ? "Bearer token expired or invalid. Re-authenticate and try again."
                        : ex.Description ?? "Unable to exchange token for downstream access.",
                expired ? "authorization.token.expired" : "authorization.token_exchange_denied"));
        }
    }

    private static string? ExtractBearer(string? header) =>
        !string.IsNullOrWhiteSpace(header) && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : null;
}
```

> Implementer: confirm `IServiceTokenExchangeService.ExchangeTokenAsync`, `ServiceTokenResult.AccessToken`, and `TokenExchangeException.IsAuthFailure`/`StatusCode`/`Description` signatures in `src/shared/SharedKernel.Infrastructure/Auth/`.

- [ ] **Step 4: Run → PASS. Step 5: Commit** `feat(gateway): token exchange step`.

---

### Task 8: `EdgeEnforcementMiddleware` orchestrator + YARP transforms

**Files:**
- Create: `src/services/gateway/public/Edge/EdgeEnforcementMiddleware.cs`
- Create: `src/services/gateway/public/Edge/ReverseProxyTransformExtensions.cs`
- Test: `tests/unit/Gateway.Public.UnitTests/Edge/EdgeEnforcementMiddlewareTests.cs`

**Interfaces:**
- Consumes: `IEdgeAccessPolicyRegistry`, the ordered `IEnumerable<IEdgeStep>`, `EdgeProblemWriter`.
- Produces: `EdgeEnforcementMiddleware` (runs steps; resolves route policy; writes problem on stop); `AddEdgeGatewayTransforms` (forwards `X-TenantId`, `X-Tenant-DbStrategy`, exchanged bearer).

- [ ] **Step 1: Write the failing test** — a middleware test where the first step stops with a 403 asserts the next delegate is NOT called and the response is the problem; an all-proceed run calls `next`. Build the middleware with a fake registry returning a policy and an in-memory ordered step list.

- [ ] **Step 2: Run → FAIL.**

- [ ] **Step 3: Implement orchestrator + transforms**

```csharp
// src/services/gateway/public/Edge/EdgeEnforcementMiddleware.cs
using Microsoft.AspNetCore.Http;
using Yarp.ReverseProxy.Model;

namespace Gateway.Public.Edge;

/// <summary>Runs the ordered edge step pipeline for proxied routes.</summary>
public sealed class EdgeEnforcementMiddleware
{
    private readonly RequestDelegate next;
    private readonly IEdgeAccessPolicyRegistry registry;
    private readonly IReadOnlyList<IEdgeStep> steps;

    /// <summary>Initializes a new instance of the <see cref="EdgeEnforcementMiddleware"/> class.</summary>
    /// <param name="next">The next delegate.</param>
    /// <param name="registry">The route policy registry.</param>
    /// <param name="steps">The ordered edge steps.</param>
    public EdgeEnforcementMiddleware(RequestDelegate next, IEdgeAccessPolicyRegistry registry, IEnumerable<IEdgeStep> steps)
    {
        this.next = next;
        this.registry = registry;
        this.steps = steps.ToList();
    }

    /// <summary>Executes the middleware.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A task.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        IReverseProxyFeature? proxy = context.Features.Get<IReverseProxyFeature>();
        string? routeId = proxy?.Route?.Config?.RouteId;

        EdgeAccessPolicy? policy = routeId is null ? null : registry.ForRoute(routeId);
        if (policy is null)
        {
            await next(context); // non-proxied (health, openapi) — pass through
            return;
        }

        var edge = new EdgeContext(context, policy);
        foreach (IEdgeStep step in steps)
        {
            EdgeStepResult result = await step.ExecuteAsync(edge, context.RequestAborted);
            if (!result.Continue)
            {
                await EdgeProblemWriter.WriteAsync(context, result.Problem!);
                return;
            }
        }

        await next(context);
    }
}
```

```csharp
// src/services/gateway/public/Edge/ReverseProxyTransformExtensions.cs
using Yarp.ReverseProxy.Transforms;

namespace Gateway.Public.Edge;

/// <summary>Forwards trusted edge headers + the exchanged bearer to the upstream.</summary>
public static class ReverseProxyTransformExtensions
{
    /// <summary>Adds the edge request transforms.</summary>
    /// <param name="builder">The reverse proxy builder.</param>
    /// <param name="tenantOptions">The edge tenant options.</param>
    /// <returns>The same builder.</returns>
    public static IReverseProxyBuilder AddEdgeGatewayTransforms(this IReverseProxyBuilder builder, EdgeTenantOptions tenantOptions) =>
        builder.AddTransforms(ctx => ctx.AddRequestTransform(transform =>
        {
            HttpContext http = transform.HttpContext;
            Forward(transform, http, tenantOptions.TenantIdHeaderName);
            Forward(transform, http, EdgeHeaders.TenantDbStrategy);

            if (http.Items.TryGetValue(EdgeHeaders.ExchangedTokenItemKey, out object? token) &&
                token is string exchanged && !string.IsNullOrWhiteSpace(exchanged))
            {
                transform.ProxyRequest.Headers.Authorization = new("Bearer", exchanged);
            }

            return ValueTask.CompletedTask;
        }));

    private static void Forward(RequestTransformContext transform, HttpContext http, string header)
    {
        if (http.Request.Headers.TryGetValue(header, out var values) && !string.IsNullOrWhiteSpace(values.ToString()))
        {
            transform.ProxyRequest.Headers.Remove(header);
            transform.ProxyRequest.Headers.TryAddWithoutValidation(header, values.ToString());
        }
    }
}
```

> Implementer: confirm `RouteConfig.RouteId` member name in YARP 2.3.0 (it may be `RouteId`); the reference resolves route config via `IReverseProxyFeature.Route.Config`.

- [ ] **Step 4: Run → PASS. Step 5: Commit** `feat(gateway): edge enforcement middleware and proxy transforms`.

---

### Task 9: Program wiring — auth pipeline, DI, YARP authz policies, remote client

**Files:**
- Modify: `src/services/gateway/public/Program.cs`
- Create: `src/services/gateway/public/Edge/EdgeServiceCollectionExtensions.cs`

**Interfaces:**
- Consumes: everything above; SharedKernel `AddKeycloak`, `IServiceTokenExchangeService`, `ITenantTokenContextResolver`; FastEndpoints `MapRemote`.

- [ ] **Step 1: DI extension** registering options, the validated registry (built at startup — throws on misconfig), the ordered steps, FusionCache, token exchange, tenant resolver, and the `customer` remote client:

```csharp
// src/services/gateway/public/Edge/EdgeServiceCollectionExtensions.cs
using FastEndpoints;
using Gateway.Public.Edge.Steps;
using SharedKernel.Grpc.Contracts.Remote.V1.Tenants;
using SharedKernel.Infrastructure.Auth;
using SharedKernel.Infrastructure.MultiTenant;

namespace Gateway.Public.Edge;

/// <summary>Registers edge pipeline services.</summary>
public static class EdgeServiceCollectionExtensions
{
    /// <summary>Adds the edge enforcement services to the container.</summary>
    /// <param name="builder">The web application builder.</param>
    /// <returns>The same builder.</returns>
    public static WebApplicationBuilder AddEdgePipeline(this WebApplicationBuilder builder)
    {
        EdgeTenantOptions tenantOptions = builder.Configuration.GetEdgeTenantOptions();
        builder.Services.AddSingleton(tenantOptions);

        // Fail-closed: throws here at startup if any non-anonymous route lacks an audience.
        builder.Services.AddSingleton<IEdgeAccessPolicyRegistry>(
            EdgeAccessPolicyRegistry.Build(builder.Configuration));

        builder.Services.AddFusionCache();
        builder.Services.AddHttpClient("KeycloakTokenClient");
        builder.Services.AddSingleton<IServiceTokenExchangeService, ServiceTokenExchangeService>();
        builder.Services.AddSingleton<ITenantTokenContextResolver, TenantTokenContextResolver>();
        builder.Services.AddSingleton<ITenantDatabaseStrategyResolver, RemoteTenantDatabaseStrategyResolver>();

        // Ordered steps (registration order == execution order).
        builder.Services.AddScoped<IEdgeStep, HeaderFirewallStep>();
        builder.Services.AddScoped<IEdgeStep, ResolveTenantStep>();
        builder.Services.AddScoped<IEdgeStep, ResolveDbStrategyStep>();
        builder.Services.AddScoped<IEdgeStep, ExchangeTokenStep>();

        return builder;
    }
}
```

> Implementer: confirm the concrete `TenantTokenContextResolver` exists in SharedKernel (the reference registered it); if only the interface is present, port the resolver too or register the available implementation. DI resolves `IEnumerable<IEdgeStep>` in registration order — keep the four `AddScoped` calls in pipeline order.

- [ ] **Step 2: Full `Program.cs`** — standard auth pipeline + YARP authz policies + transforms + remote client + edge middleware:

```csharp
using FastEndpoints;
using Gateway.Public.Edge;
using Keycloak.AuthServices.Authentication;
using Microsoft.AspNetCore.Authorization;
using SharedKernel.Grpc.Contracts.Remote.V1.Tenants;
using SharedKernel.Infrastructure.Auth;

var builder = WebApplication.CreateBuilder(args);

// AuthN (Keycloak JWT) via SharedKernel.
var keycloak = builder.Configuration.GetSection("Keycloak").Get<KeycloakAuthenticationOptions>()!;
builder.Services.AddKeycloak(builder.Configuration, builder.Environment, keycloak);

// AuthZ policies referenced by YARP routes (fail-closed: "authenticated" requires a user).
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("authenticated", policy => policy.RequireAuthenticatedUser());

builder.AddEdgePipeline();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver()
    .AddEdgeGatewayTransforms(builder.Configuration.GetEdgeTenantOptions());

var app = builder.Build();

// Remote client to the customer tenant authority.
app.MapRemote(builder.Configuration["Services:CustomerApi:Url"]!, remote =>
    remote.Register<GetTenantDatabaseInfoCommand, TenantDatabaseInfoRpcResult>());

app.UseAuthentication();
app.UseAuthorization();
app.UseRouting();
app.UseMiddleware<EdgeEnforcementMiddleware>();
app.MapReverseProxy();
await app.RunAsync();

namespace Gateway.Public { public partial class Program { } }
```

> Implementer: order matters — `UseAuthentication`/`UseAuthorization` must run before `UseMiddleware<EdgeEnforcementMiddleware>` so the principal exists and YARP's per-route `AuthorizationPolicy` rejects unauthenticated callers before tenant logic. Confirm `MapRemote`/`Register` signatures against FastEndpoints 8.1.0 (the reference uses exactly this in `Web.Public.Gateway/Program.cs`).

- [ ] **Step 3: Build + run smoke** → `nx build Gateway.Public`; boot locally and hit `/orders/{id}` without a token → expect 401 from the auth pipeline (not the edge steps). Commit `feat(gateway): wire auth pipeline, edge middleware, and customer remote client`.

---

### Task 10: End-to-end integration + mock auth (out of the production binary)

**Files:**
- Create: `tests/integration/Gateway.Public.IntegrationTests/Gateway.Public.IntegrationTests.csproj`
- Create: `tests/integration/Gateway.Public.IntegrationTests/MockBearerAuthenticationHandler.cs` (test assembly only)
- Create: `tests/integration/Gateway.Public.IntegrationTests/GatewayFlowTests.cs`
- Modify: `src/services/gateway/public/Program.cs` (hard-throw if mock auth requested in Production)

**Interfaces:**
- Consumes: the full gateway; a stubbed/`customer` test server.

- [ ] **Step 1: Production mock-auth guard (in the gateway)** — add near the top of `Program.cs`:

```csharp
bool mockAuth = builder.Configuration.GetValue<bool>("Testing:UseMockAuthentication");
if (mockAuth && builder.Environment.IsProduction())
{
    throw new InvalidOperationException("Mock authentication must never be enabled in Production.");
}
```

The actual `MockBearerAuthenticationHandler` lives in the **test** assembly and is injected via `WebApplicationFactory.ConfigureTestServices` (not shipped in the gateway binary). Build to confirm the gateway has no reference to it.

- [ ] **Step 2: Write the failing end-to-end test** — `WebApplicationFactory<Gateway.Public.Program>` with: mock bearer scheme overriding `"authenticated"`, a stub upstream that echoes received headers, and a fake `ITenantDatabaseStrategyResolver` (or a real `customer` test server). Assertions:

```csharp
// tests/integration/Gateway.Public.IntegrationTests/GatewayFlowTests.cs (representative cases)
[Fact]
public async Task AuthenticatedRequest_ForwardsTenantAndDbStrategyAndExchangedBearer()
{
    var client = _factory.WithMockUser(organizations: "tenant-a").CreateClient();
    var response = await client.GetAsync("/orders/123");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var echoed = await response.Content.ReadFromJsonAsync<EchoedHeaders>();
    Assert.Equal("tenant-a", echoed!.TenantId);
    Assert.False(string.IsNullOrEmpty(echoed.TenantDbStrategy));
    Assert.StartsWith("Bearer ", echoed.Authorization);
}

[Fact]
public async Task Unauthenticated_Returns401_BeforeTenantLogic()
{
    var response = await _factory.CreateClient().GetAsync("/orders/123");
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
}

[Fact]
public async Task TokenTenantMismatch_Returns403()
{
    var client = _factory.WithMockUser(organizations: "tenant-a").CreateClient();
    var request = new HttpRequestMessage(HttpMethod.Get, "/orders/123");
    request.Headers.Add("X-TenantId", "tenant-b");
    var response = await client.SendAsync(request);
    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
}
```

- [ ] **Step 3: Run → expect FAIL, then make the harness pass** (wire the mock scheme, stub upstream cluster to an in-test server, register the fake resolver via `ConfigureTestServices`).

Run: `nx test --project=Gateway.Public.IntegrationTests`
Expected: PASS.

- [ ] **Step 4: Re-enable the Phase A skipped `order` integration tests** through the gateway mock-auth path (the `// TODO(auth-phase-b)` markers from Phase A Task 4).

- [ ] **Step 5: Full gate + docs + commit**

Update `src/services/gateway/AGENTS.md` to document the implemented public gateway (pipeline, route metadata `EdgeAccess`, fail-closed audience binding). Run `nx affected -t build test lint` → PASS.

```bash
git add tests/integration/Gateway.Public.IntegrationTests src/services/gateway
git commit -m "test(gateway): end-to-end edge flow with mock auth"
```

---

## Self-Review

- **Spec §5 coverage:** §5.1 standard auth pipeline + YARP `authenticated` policy (Task 9); §5.2 ordered step pipeline (Tasks 3–8); §5.3 fail-closed `EdgeAccessPolicy` registry (Task 2); §5.4 header firewall (Task 4); §5.5 FusionCache fail-safe + circuit breaker resolver (Task 6); §5.6 mock auth in test assembly + Production guard (Task 10). §8 error codes used across Tasks 5–7. ✓
- **Placeholder scan:** remaining notes are explicit implementer verifications of third-party member names (YARP `RouteId`, FastEndpoints `MapRemote`/`AddHandlerServer`, SharedKernel signatures) — not deferred design. The `ResolveDbStrategyStep` cluster-id note explicitly directs adding `EdgeContext.ClusterId`; fold that into Task 6/8 when implementing.
- **Type consistency:** `EdgeAccessPolicy`/`EdgeAccessMode`, `IEdgeStep`/`EdgeStepResult`/`EdgeProblem`/`EdgeContext`, `EdgeHeaders.*`, `ITenantDatabaseStrategyResolver`/`TenantDbStrategyResult`, `EdgeTenantOptions` used identically across Tasks 2–10. ✓
- **Cross-phase dependency:** Phase C's `GetTenantDatabaseInfoCommand`/`TenantDatabaseInfoRpcResult` consumed in Tasks 6 & 9; Phase A's protected `order` endpoints are what the gateway fronts. Both must land first.
- **Open implementer decision:** add a `ClusterId` property to `EdgeContext` (set by the middleware from `IReverseProxyFeature.Route.Config.ClusterId`) and pass it to `ResolveDbStrategyStep` instead of `GetEndpoint().DisplayName`.
