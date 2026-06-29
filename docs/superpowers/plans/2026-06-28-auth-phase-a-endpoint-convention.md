# Auth Phase A — Endpoint AuthZ Convention Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every service endpoint declare its access policy declaratively via an evolved `AuthenticatedEndpoint<,>` base, enforced by an architecture test, and migrate the `order` endpoints onto it.

**Architecture:** A new `EndpointPermission` value type describes an endpoint's `(Resource, Scope, Audience)`. The sealed `Configure()` of `AuthenticatedEndpoint<,>` reads each endpoint's `Permission` property and wires Keycloak `RequireProtectedResource` + `OpenApiAudienceMetadata` (or `AllowAnonymous` for explicitly anonymous endpoints). An ArchUnit/reflection rule fails the build if a service endpoint bypasses the base or omits a permission.

**Tech Stack:** .NET 10, FastEndpoints, Keycloak.AuthServices.Authorization, ArchUnitNET, xUnit v3.

## Global Constraints

- Target framework `net10.0`; nullable + implicit usings on (root `Directory.Build.props`).
- `TreatWarningsAsErrors=true`; the root `.editorconfig` is an allowlist — public types/members need XML docs, usings ordered, file-scoped namespaces.
- Reference is the design spec: `docs/superpowers/specs/2026-06-28-platform-auth-architecture-design.md` §4.
- Conventional commits (`type(scope): description`). Never tag or run `nx release`.
- Run `nx affected -t build test lint` before considering a task done.

---

### Task 1: `EndpointPermission` value type + unit-test project

**Files:**
- Create: `src/shared/SharedKernel.Infrastructure/Endpoints/EndpointPermission.cs`
- Create: `tests/unit/SharedKernel.UnitTests/SharedKernel.UnitTests.csproj`
- Create: `tests/unit/SharedKernel.UnitTests/Endpoints/EndpointPermissionTests.cs`
- Modify: `Teck.Platform.slnx` (add the new test project)

**Interfaces:**
- Produces: `public sealed record EndpointPermission(string Resource, string Scope, string Audience)` with `static EndpointPermission Anonymous(string audience)` and `bool IsAnonymous`. Consumed by Task 2 and Task 4.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/unit/SharedKernel.UnitTests/Endpoints/EndpointPermissionTests.cs
using SharedKernel.Infrastructure.Endpoints;
using Xunit;

namespace SharedKernel.UnitTests.Endpoints;

public sealed class EndpointPermissionTests
{
    [Fact]
    public void Anonymous_HasEmptyResourceAndScope_AndIsAnonymous()
    {
        var permission = EndpointPermission.Anonymous("public");

        Assert.True(permission.IsAnonymous);
        Assert.Equal(string.Empty, permission.Resource);
        Assert.Equal(string.Empty, permission.Scope);
        Assert.Equal("public", permission.Audience);
    }

    [Fact]
    public void Protected_IsNotAnonymous_AndCarriesResourceScopeAudience()
    {
        var permission = new EndpointPermission("order", "create", "public");

        Assert.False(permission.IsAnonymous);
        Assert.Equal("order", permission.Resource);
        Assert.Equal("create", permission.Scope);
        Assert.Equal("public", permission.Audience);
    }
}
```

- [ ] **Step 2: Create the test project and register it**

Create `tests/unit/SharedKernel.UnitTests/SharedKernel.UnitTests.csproj` mirroring `tests/unit/Order.UnitTests/Order.UnitTests.csproj` (same `<PackageReference>`s for xUnit v3 + analyzers), with a single project reference:

```xml
<ItemGroup>
  <ProjectReference Include="..\..\..\src\shared\SharedKernel.Infrastructure\SharedKernel.Infrastructure.csproj" />
</ItemGroup>
```

Add the project to `Teck.Platform.slnx` next to `Order.UnitTests`. (Nx auto-discovers `.csproj` via the `@nx/dotnet` plugin — no manual `project.json` needed; confirm with `nx show projects | grep SharedKernel.UnitTests`.)

- [ ] **Step 3: Run test to verify it fails**

Run: `nx test --project=SharedKernel.UnitTests`
Expected: FAIL — `EndpointPermission` does not exist (compile error).

- [ ] **Step 4: Write minimal implementation**

```csharp
// src/shared/SharedKernel.Infrastructure/Endpoints/EndpointPermission.cs
namespace SharedKernel.Infrastructure.Endpoints;

/// <summary>
/// Describes the access policy of an endpoint: the Keycloak protected resource and scope it
/// requires, plus the OpenAPI audience document it belongs to.
/// </summary>
/// <param name="Resource">The Keycloak protected-resource name (empty for anonymous endpoints).</param>
/// <param name="Scope">The Keycloak scope required on the resource (empty for anonymous endpoints).</param>
/// <param name="Audience">The OpenAPI audience document group (e.g. "public", "admin").</param>
public sealed record EndpointPermission(string Resource, string Scope, string Audience)
{
    /// <summary>Creates a permission for an endpoint that requires no authorization.</summary>
    /// <param name="audience">The OpenAPI audience document group.</param>
    /// <returns>An anonymous <see cref="EndpointPermission"/>.</returns>
    public static EndpointPermission Anonymous(string audience) => new(string.Empty, string.Empty, audience);

    /// <summary>Gets a value indicating whether this endpoint requires no authorization.</summary>
    public bool IsAnonymous => Resource.Length == 0;
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `nx test --project=SharedKernel.UnitTests`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add src/shared/SharedKernel.Infrastructure/Endpoints/EndpointPermission.cs \
        tests/unit/SharedKernel.UnitTests Teck.Platform.slnx
git commit -m "feat(shared): add EndpointPermission access-policy value type"
```

---

### Task 2: Evolve `AuthenticatedEndpoint<,>` base to wire the permission

**Files:**
- Modify: `src/shared/SharedKernel.Infrastructure/Endpoints/AuthenticatedEndpoint.cs`
- Create: `tests/unit/SharedKernel.UnitTests/Endpoints/AuthenticatedEndpointTests.cs`

**Interfaces:**
- Consumes: `EndpointPermission` (Task 1).
- Produces: abstract `protected abstract EndpointPermission Permission { get; }` and `protected abstract void ConfigureEndpoint();` on `AuthenticatedEndpoint<TRequest,TResponse>`; sealed `Configure()`. Consumed by Task 3 (reflection) and Task 4 (order endpoints).

- [ ] **Step 1: Write the failing test**

Uses FastEndpoints' `Factory.Create` to build a test endpoint and inspect its `Definition` after `Configure()`.

```csharp
// tests/unit/SharedKernel.UnitTests/Endpoints/AuthenticatedEndpointTests.cs
using FastEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using SharedKernel.Infrastructure.Endpoints;
using SharedKernel.Infrastructure.OpenApi;
using Xunit;

namespace SharedKernel.UnitTests.Endpoints;

public sealed class AuthenticatedEndpointTests
{
    private sealed class ProtectedTestEndpoint : AuthenticatedEndpoint<EmptyRequest, EmptyResponse>
    {
        protected override EndpointPermission Permission => new("order", "create", "public");
        protected override void ConfigureEndpoint() => Post("/test/protected");
        public override Task HandleAsync(EmptyRequest req, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class AnonymousTestEndpoint : AuthenticatedEndpoint<EmptyRequest, EmptyResponse>
    {
        protected override EndpointPermission Permission => EndpointPermission.Anonymous("public");
        protected override void ConfigureEndpoint() => Get("/test/anon");
        public override Task HandleAsync(EmptyRequest req, CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public void ProtectedEndpoint_TagsAudience_AndIsNotAnonymous()
    {
        var ep = Factory.Create<ProtectedTestEndpoint>();
        ep.Definition.Initialize(ep, null);

        Assert.Contains(ep.Definition.EndpointTags ?? [], _ => false); // placeholder, replaced below
        Assert.Contains(ep.Definition.Metadata, m => m is OpenApiAudienceMetadata a && a.Audiences.Contains("public"));
        Assert.False(ep.Definition.AllowAnonymous);
        Assert.Contains(JwtBearerDefaults.AuthenticationScheme, ep.Definition.AuthSchemeNames ?? []);
    }

    [Fact]
    public void AnonymousEndpoint_TagsAudience_AndAllowsAnonymous()
    {
        var ep = Factory.Create<AnonymousTestEndpoint>();
        ep.Definition.Initialize(ep, null);

        Assert.Contains(ep.Definition.Metadata, m => m is OpenApiAudienceMetadata a && a.Audiences.Contains("public"));
        Assert.True(ep.Definition.AllowAnonymous);
    }
}
```

> Note for implementer: FastEndpoints exposes endpoint config through `ep.Definition` after `Configure()` runs. If a specific accessor name (`AllowAnonymous`, `AuthSchemeNames`, `Metadata`) differs in the pinned FastEndpoints version, inspect `EndpointDefinition` in the decompiled package and adjust the assertion to the equivalent member; delete the placeholder `EndpointTags` assertion line.

- [ ] **Step 2: Run test to verify it fails**

Run: `nx test --project=SharedKernel.UnitTests`
Expected: FAIL — `Permission` is not a member; `ConfigureEndpoint` still abstract but `Configure` not wiring audience.

- [ ] **Step 3: Write the implementation**

```csharp
// src/shared/SharedKernel.Infrastructure/Endpoints/AuthenticatedEndpoint.cs
using FastEndpoints;
using Keycloak.AuthServices.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using SharedKernel.Infrastructure.OpenApi;

namespace SharedKernel.Infrastructure.Endpoints;

/// <summary>
/// Base class for service endpoints. Each endpoint declares its <see cref="Permission"/>; the base
/// wires Keycloak protected-resource authorization (or anonymous access) plus the OpenAPI audience
/// document, so authorization can never be silently omitted.
/// </summary>
/// <typeparam name="TRequest">The request DTO type.</typeparam>
/// <typeparam name="TResponse">The response DTO type.</typeparam>
public abstract class AuthenticatedEndpoint<TRequest, TResponse> : Endpoint<TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>Gets the access policy for this endpoint.</summary>
    protected abstract EndpointPermission Permission { get; }

    /// <inheritdoc/>
    public sealed override void Configure()
    {
        ConfigureEndpoint();

        EndpointPermission permission = Permission;

        Options(builder =>
        {
            builder.WithMetadata(new OpenApiAudienceMetadata(permission.Audience));
            if (!permission.IsAnonymous)
            {
                builder.RequireProtectedResource(permission.Resource, permission.Scope);
            }
        });

        if (permission.IsAnonymous)
        {
            AllowAnonymous();
        }
        else
        {
            AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        }
    }

    /// <summary>Configures route, version, and summary for this endpoint.</summary>
    protected abstract void ConfigureEndpoint();
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `nx test --project=SharedKernel.UnitTests`
Expected: PASS.

- [ ] **Step 5: Build the whole affected graph (base change is breaking for existing endpoints)**

Run: `nx affected -t build`
Expected: FAIL in `order-api` — `CreateOrderEndpoint`/`GetOrderEndpoint` do not implement `Permission`. This is expected and fixed in Task 4. Do not proceed to commit until the build is green; if executing tasks in order, commit this task together with Task 4, or temporarily verify only `SharedKernel.Infrastructure` builds:

Run: `nx build SharedKernel.Infrastructure` → Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/shared/SharedKernel.Infrastructure/Endpoints/AuthenticatedEndpoint.cs \
        tests/unit/SharedKernel.UnitTests/Endpoints/AuthenticatedEndpointTests.cs
git commit -m "feat(shared): wire declarative permission into AuthenticatedEndpoint base"
```

---

### Task 3: Architecture rule — endpoints must derive from `AuthenticatedEndpoint<,>`

**Files:**
- Create: `tests/architecture/Teck.Platform.Arch.Tests/Rules/EndpointRules.cs`
- Modify: `tests/architecture/Order.Architecture.UnitTests/OrderArchitectureTests.cs`

**Interfaces:**
- Consumes: FastEndpoints `IEndpoint`, `AuthenticatedEndpoint<,>` (Task 2).
- Produces: `EndpointRules.EndpointsShouldDeriveFromAuthenticatedEndpoint(Assembly hostAssembly)`.

- [ ] **Step 1: Write the rule (reflection-based; matches existing `HandlerReflection` style)**

```csharp
// tests/architecture/Teck.Platform.Arch.Tests/Rules/EndpointRules.cs
using System.Reflection;
using FastEndpoints;
using SharedKernel.Infrastructure.Endpoints;
using Xunit;

namespace Teck.Platform.Arch.Tests.Rules;

/// <summary>Architecture rules for FastEndpoints endpoints in service Host assemblies.</summary>
public static class EndpointRules
{
    /// <summary>
    /// Every concrete FastEndpoints endpoint in a service Host must derive from
    /// <see cref="AuthenticatedEndpoint{TRequest,TResponse}"/>, so authorization wiring is
    /// declared once and cannot be bypassed with a raw <c>Endpoint&lt;,&gt;</c>.
    /// </summary>
    /// <param name="hostAssembly">The service Host assembly.</param>
    public static void EndpointsShouldDeriveFromAuthenticatedEndpoint(Assembly hostAssembly)
    {
        Type[] endpointTypes = hostAssembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false }
                && typeof(IEndpoint).IsAssignableFrom(type))
            .ToArray();

        Assert.All(endpointTypes, type =>
            Assert.True(
                DerivesFromAuthenticatedEndpoint(type),
                $"Endpoint '{type.FullName}' must derive from AuthenticatedEndpoint<,>."));
    }

    private static bool DerivesFromAuthenticatedEndpoint(Type type)
    {
        for (Type? current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType
                && current.GetGenericTypeDefinition() == typeof(AuthenticatedEndpoint<,>))
            {
                return true;
            }
        }

        return false;
    }
}
```

- [ ] **Step 2: Wire the rule into the order architecture tests**

Add to `tests/architecture/Order.Architecture.UnitTests/OrderArchitectureTests.cs` (uses the existing `HostAssembly`):

```csharp
    [Fact]
    public void OrderEndpoints_ShouldDeriveFromAuthenticatedEndpoint() =>
        Teck.Platform.Arch.Tests.Rules.EndpointRules
            .EndpointsShouldDeriveFromAuthenticatedEndpoint(HostAssembly);
```

Add the project reference to `SharedKernel.Infrastructure` in `tests/architecture/Teck.Platform.Arch.Tests/Teck.Platform.Arch.Tests.csproj` if not already present (the rule references `AuthenticatedEndpoint<,>`); also add `FastEndpoints` package reference there.

- [ ] **Step 3: Run to verify it currently fails for the right reason**

Run: `nx test --project=Order.Architecture.UnitTests`
Expected: at this point endpoints DO derive from `AuthenticatedEndpoint` already, so the new test PASSES. (The rule's real value is preventing future raw endpoints.) Confirm PASS, and that the test discovers a non-empty endpoint set — if `endpointTypes` is empty the assertion vacuously passes; add `Assert.NotEmpty(endpointTypes);` before `Assert.All` to guard.

- [ ] **Step 4: Commit**

```bash
git add tests/architecture/Teck.Platform.Arch.Tests/Rules/EndpointRules.cs \
        tests/architecture/Teck.Platform.Arch.Tests/Teck.Platform.Arch.Tests.csproj \
        tests/architecture/Order.Architecture.UnitTests/OrderArchitectureTests.cs
git commit -m "test(architecture): enforce endpoints derive from AuthenticatedEndpoint"
```

---

### Task 4: Migrate `order` endpoints onto declarative permissions

**Files:**
- Modify: `src/services/commerce/order/Order.Host/Endpoints/Orders/CreateOrderEndpoint.cs`
- Modify: `src/services/commerce/order/Order.Host/Endpoints/Orders/GetOrderEndpoint.cs`
- Modify (if present): `tests/integration/Order.IntegrationTests/` order endpoint tests that assumed anonymous access.

**Interfaces:**
- Consumes: `EndpointPermission` (Task 1), evolved base (Task 2).

- [ ] **Step 1: Update `CreateOrderEndpoint`**

Replace the `ConfigureEndpoint` body and add the `Permission` property:

```csharp
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("order", "create", "public");

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Post("/orders");
        Version(0);
    }
```

Add `using SharedKernel.Infrastructure.Endpoints;` (already present). Remove the `AllowAnonymous();` call.

- [ ] **Step 2: Update `GetOrderEndpoint`**

```csharp
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("order", "read", "public");

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Get("/orders/{id}");
        Version(0);
    }
```

Remove the `AllowAnonymous();` call.

- [ ] **Step 3: Build**

Run: `nx affected -t build`
Expected: PASS (the Task 2 breaking change is now resolved).

- [ ] **Step 4: Fix integration tests that assumed anonymous access**

If `Order.IntegrationTests` calls these endpoints without auth and expects 2xx, they will now get 401. Update them to send a bearer token via the test auth handler (the shared integration harness — see `tests/integration/Teck.Platform.IntegrationTests.Shared`). If that harness has no auth helper yet, mark the affected tests `[Fact(Skip = "re-enabled with gateway mock-auth in Phase B")]` and leave a `// TODO(auth-phase-b)` so Phase B re-enables them through the gateway mock-auth path.

Run: `nx test --project=Order.IntegrationTests`
Expected: PASS (passing or explicitly skipped with reason).

- [ ] **Step 5: Run the full affected gate**

Run: `nx affected -t build test lint`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/services/commerce/order/Order.Host/Endpoints/Orders/ tests/integration/Order.IntegrationTests/
git commit -m "feat(order): adopt declarative endpoint permissions for order endpoints"
```

---

## Self-Review

- **Spec §4 coverage:** `EndpointPermission` (Task 1), evolved base wiring `RequireProtectedResource` + `OpenApiAudienceMetadata` + anonymous (Task 2), ArchUnit enforcement (Task 3), order-endpoint migration to `("order","create")`/`("order","read")` (Task 4). ✓
- **Placeholder scan:** the only deliberate marker is the FastEndpoints `Definition` accessor note in Task 2 (version-dependent member names) and the `EndpointTags` placeholder line flagged for deletion — both are explicit implementer instructions, not deferred work.
- **Type consistency:** `EndpointPermission(Resource, Scope, Audience)`, `Anonymous(audience)`, `IsAnonymous`, `Permission` property, `ConfigureEndpoint()` used identically across Tasks 1–4. ✓
- **Open dependency:** `OpenApiAudienceMetadata(params string[] Audiences)` confirmed at `src/shared/SharedKernel.Infrastructure/OpenApi/OpenApiAudienceMetadata.cs`; `RequireProtectedResource` provided by the already-referenced `Keycloak.AuthServices.Authorization`.
