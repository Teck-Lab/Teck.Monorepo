# Auth Security Review Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Apply 5 security-review fixes (1 critical + 4 important) to the auth feature branch, ensure all integration tests pass, and produce a final report.

**Architecture:** Each fix is surgical and isolated; they share no code — tasks can be validated independently. The critical fix (Wolverine middleware guard) unblocks the 3 skipped Order integration tests. All 5 commits must leave `nx affected -t build test lint` green.

**Tech Stack:** .NET 10, WolverineFx, YARP, EF Core 10, xUnit v3, Testcontainers, ASP.NET Core WebApplicationFactory, StackExchange.Redis, Keycloak auth.

## Global Constraints

- `net10.0`, nullable/implicit usings enabled throughout.
- `TreatWarningsAsErrors=true` in production code; test projects inherit `CodeAnalysisTreatWarningsAsErrors=false`.
- Allowlist `.editorconfig`: XML docs on all `public` types/members, file-scoped namespaces, ordered usings, StyleCop rules in effect.
- NEVER touch `Order.Host/Database/OrderPersistenceExtensions.cs` or any other production host file to make tests pass — test doubles belong in the test project via `WebApplicationFactory.ConfigureTestServices`.
- Run `bunx nx affected -t build test lint` to verify after each task.
- Each commit body must end with: `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`

---

## File Map

### Modified production files
- `src/shared/SharedKernel.Infrastructure/Behaviors/BehaviorExtensions.cs` — FIX 1: guard `AddMiddleware` calls
- `src/shared/SharedKernel.Infrastructure/Behaviors/LicenseEnforcementMiddleware.cs` — FIX 1: remove unused `errors` list
- `src/services/gateway/public/Edge/Steps/HeaderFirewallStep.cs` — FIX 2: strip `Authorization` on anonymous routes
- `src/services/gateway/public/Edge/EdgeAccessPolicyRegistry.cs` — FIX 3: require `AuthorizationPolicy == "authenticated"` for non-anonymous routes
- `src/services/gateway/public/Edge/Steps/ExchangeTokenStep.cs` — FIX 4: reject blank exchanged token
- `src/services/commerce/customer/Customer.Application/Database/Configurations/TenantConfiguration.cs` — FIX 5: remove `HasData` seed
- `src/services/commerce/customer/Customer.Host/Database/Migrations/20260628172503_InitialCustomer.cs` — FIX 5: remove `InsertData`
- `src/services/commerce/customer/Customer.Host/Database/Migrations/CustomerDbContextModelSnapshot.cs` — FIX 5: remove `HasData` from snapshot
- `src/services/commerce/customer/Customer.Host/Program.cs` — FIX 5: add dev-only idempotent tenant seed

### Modified test files
- `tests/integration/Order.IntegrationTests/CreateOrderTests.cs` — FIX 1: re-enable 3 skipped tests, add mock auth to `OrderWebApplicationFactory`
- `tests/integration/Order.IntegrationTests/SharedTestcontainersCollection.cs` — FIX 1: add (already in git as untracked)
- `tests/integration/Customer.IntegrationTests/GetTenantDatabaseInfoTests.cs` — FIX 5: seed dev tenant explicitly in `InitializeAsync`
- `tests/unit/Gateway.Public.UnitTests/Edge/Steps/HeaderFirewallStepTests.cs` — FIX 2: add 2 new tests
- `tests/unit/Gateway.Public.UnitTests/Edge/EdgeAccessPolicyRegistryTests.cs` — FIX 3: add 1 new test
- `tests/unit/Gateway.Public.UnitTests/Edge/Steps/ExchangeTokenStepTests.cs` — FIX 4: add 1 new test

### Created test files
- `tests/integration/Order.IntegrationTests/MockBearerAuthenticationHandler.cs` — FIX 1: test-only mock auth handler

### Report file
- `/workspaces/Teck.Monorepo/.claude/worktrees/auth-architecture/.superpowers/sdd/task-finalfix-report.md`

---

## Task 1: FIX 1a — Guard Wolverine middleware registration and clean dead code

**Files:**
- Modify: `src/shared/SharedKernel.Infrastructure/Behaviors/BehaviorExtensions.cs`
- Modify: `src/shared/SharedKernel.Infrastructure/Behaviors/LicenseEnforcementMiddleware.cs`

**Interfaces:**
- Consumes: `opts.Services` (IServiceCollection), `IDatabase` (StackExchange.Redis), `ILicenseValidator` (SharedKernel.Core)
- Produces: `AddTeckBehaviors` only calls `AddMiddleware<T>()` when the required dep is registered; `LicenseEnforcementMiddleware.BeforeAsync` has no dead `errors` list

- [ ] **Step 1: Edit BehaviorExtensions.cs**

Replace the two unconditional `AddMiddleware` calls with conditional guards. Full file content:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Core.Licensing;
using SharedKernel.Infrastructure.Messaging.Idempotency;
using StackExchange.Redis;
using Wolverine;

namespace SharedKernel.Infrastructure.Behaviors;

/// <summary>
/// Registers WolverineFx middleware behaviors.
/// Transactional behavior, validation, and logging are handled by WolverineFx built-in
/// features (AutoApplyTransactions, UseFluentValidation, built-in logging).
/// Only custom behaviors like LicenseEnforcementMiddleware are registered here.
/// </summary>
public static class BehaviorExtensions
{
    /// <summary>
    /// Registers Teck custom WolverineFx middleware behaviors (idempotency and license enforcement).
    /// Each middleware is activated only when its required dependency is present in the DI container,
    /// so services that do not register Redis or a license validator remain unaffected.
    /// </summary>
    /// <param name="opts">The WolverineFx options to configure.</param>
    /// <returns>The same <see cref="WolverineOptions"/> instance for fluent chaining.</returns>
    public static WolverineOptions AddTeckBehaviors(this WolverineOptions opts)
    {
        // Factory-delegate registrations are used here intentionally: IdempotencyMiddleware
        // depends on IDatabase (Redis) and LicenseEnforcementMiddleware depends on ILicenseValidator,
        // neither of which is required at DI build time by all service hosts. Using a factory
        // bypasses the ValidateOnBuild singleton-dependency check so handler-only services
        // (e.g. Customer.Host) can start without Redis or a license validator — these middlewares
        // are only invoked when Wolverine processes durable messages, which such services never do.
        opts.Services.AddSingleton<IdempotencyMiddleware>(static sp =>
            new IdempotencyMiddleware(
                sp.GetRequiredService<ILogger<IdempotencyMiddleware>>(),
                sp.GetRequiredService<IDatabase>()));

        if (opts.Services.Any(d => d.ServiceType == typeof(IDatabase)))
        {
            opts.Policies.AddMiddleware<IdempotencyMiddleware>();
        }

        opts.Services.AddSingleton<LicenseEnforcementMiddleware>(static sp =>
            new LicenseEnforcementMiddleware(
                sp.GetRequiredService<ILicenseValidator>()));

        if (opts.Services.Any(d => d.ServiceType == typeof(ILicenseValidator)))
        {
            opts.Policies.AddMiddleware<LicenseEnforcementMiddleware>();
        }

        return opts;
    }
}
```

- [ ] **Step 2: Edit LicenseEnforcementMiddleware.cs — remove unused `errors` list**

Replace only the `BeforeAsync` body (line 42 creates the dead list). The fixed method should be:

```csharp
public async ValueTask BeforeAsync(
    IMessageContext context,
    Func<ValueTask> next,
    CancellationToken cancellationToken)
{
    if (context.Envelope?.Message is ILicenseGatedRequest gatedRequest)
    {
        LicenseValidationResult validation = await licenseValidator.ValidateAsync(
            gatedRequest.TenantId,
            gatedRequest.LocationId,
            cancellationToken).ConfigureAwait(false);

        if (!validation.IsValid)
        {
            throw new InvalidOperationException(validation.ErrorMessage ?? "License validation failed.");
        }
    }

    await next();
}
```

Note: remove the `using ErrorOr;` import if it is only used for the dead `errors` list (check imports in the file). Also remove `using System.Reflection;` if now unused (the `_fromMethod` field still uses it — keep both if `_fromMethod` stays). Actually the `_fromMethod` field uses `System.Reflection` — keep that import but remove `using ErrorOr;` since `Error` type is no longer referenced.

The `_fromMethod` static field references `ErrorOr<object>` so `ErrorOr` is still needed for the field. However, if we remove `errors` then `Error.Forbidden` is no longer called. Check: `_fromMethod` is `typeof(ErrorOr<object>).GetMethod(...)` — this still uses `ErrorOr`. So keep both imports. Only remove the `var errors = ...` line and the `Error.Forbidden(...)` call.

- [ ] **Step 3: Verify build**

```bash
cd /workspaces/Teck.Monorepo/.claude/worktrees/auth-architecture
dotnet build src/shared/SharedKernel.Infrastructure/SharedKernel.Infrastructure.csproj --no-incremental 2>&1 | tail -20
```

Expected: Build succeeded with 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/shared/SharedKernel.Infrastructure/Behaviors/BehaviorExtensions.cs \
        src/shared/SharedKernel.Infrastructure/Behaviors/LicenseEnforcementMiddleware.cs
git commit -m "$(cat <<'EOF'
fix(shared): guard Wolverine middleware activation on dep presence

IdempotencyMiddleware (IDatabase) and LicenseEnforcementMiddleware
(ILicenseValidator) are now activated via Policies.AddMiddleware only when
their required dependency is registered. Services without Redis or a license
validator (order, customer) keep both middlewares dormant and boot normally.
Also remove the dead `errors` list from LicenseEnforcementMiddleware.BeforeAsync.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: FIX 1b — Re-enable and pass the 3 Order integration tests

**Files:**
- Modify: `tests/integration/Order.IntegrationTests/CreateOrderTests.cs`
- Create: `tests/integration/Order.IntegrationTests/MockBearerAuthenticationHandler.cs`
- Verify existing: `tests/integration/Order.IntegrationTests/SharedTestcontainersCollection.cs`

**Interfaces:**
- Consumes: `WebApplicationFactory<Program>` from Order.Host, `SharedTestcontainersFixture` from shared infra
- Produces: 3 tests that POST/GET to `/orders` succeed via mock bearer auth; auth is wired ONLY via ConfigureTestServices

- [ ] **Step 1: Create MockBearerAuthenticationHandler.cs in Order.IntegrationTests**

File: `tests/integration/Order.IntegrationTests/MockBearerAuthenticationHandler.cs`

```csharp
// <copyright file="MockBearerAuthenticationHandler.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Orders.IntegrationTests;

/// <summary>
/// Test-only bearer authentication handler that automatically authenticates every request
/// with a synthetic tenant claim. Used exclusively in integration tests to bypass real JWT
/// validation without modifying Order.Host production code.
/// </summary>
internal sealed class MockBearerAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>The authentication scheme name registered in tests.</summary>
    internal const string SchemeName = "MockBearer";

    /// <summary>Fixed tenant id injected into every authenticated request.</summary>
    internal const string TestTenantId = "00000000-0000-0000-0000-000000000001";

    /// <summary>
    /// Initializes a new instance of the <see cref="MockBearerAuthenticationHandler"/> class.
    /// </summary>
    /// <param name="options">The options monitor.</param>
    /// <param name="logger">The logger factory.</param>
    /// <param name="encoder">The URL encoder.</param>
    public MockBearerAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    /// <inheritdoc/>
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim("tenant_id", TestTenantId),
            new Claim(ClaimTypes.NameIdentifier, "test-user"),
            new Claim(ClaimTypes.Name, "Test User"),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
```

- [ ] **Step 2: Verify SharedTestcontainersCollection.cs exists**

The file at `tests/integration/Order.IntegrationTests/SharedTestcontainersCollection.cs` is already in git (status shows `??`). It should define:
```csharp
namespace Orders.IntegrationTests;
[CollectionDefinition("SharedTestcontainers")]
public class SharedTestcontainersCollection : ICollectionFixture<SharedTestcontainersFixture> { }
```
Read the file to confirm it has correct namespace `Orders.IntegrationTests` and collection name `"SharedTestcontainers"`. If the namespace is wrong, fix it.

- [ ] **Step 3: Update CreateOrderTests.cs**

Remove the 3 `[Fact(Skip=...)]` attributes and their preceding `// TODO(auth-phase-b): ...` comment blocks.

Also update `OrderWebApplicationFactory.ConfigureWebHost` to add mock auth via `ConfigureTestServices`. The `ConfigureWebHost` method in `OrderWebApplicationFactory` already calls `builder.ConfigureAppConfiguration(...)` to inject the connection strings. Add an additional `builder.ConfigureTestServices(services => { ... })` call to wire up `MockBearerAuthenticationHandler`.

The updated `OrderWebApplicationFactory` class:

```csharp
private sealed class OrderWebApplicationFactory(
    SharedTestcontainersFixture fixture,
    string databaseConnectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            configurationBuilder.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:PostgreSql"] = databaseConnectionString,
                    ["ConnectionStrings:RabbitMq"] = fixture.RabbitMqConnectionString,
                    ["ConnectionStrings:OrderWrite"] = databaseConnectionString,
                    ["ConnectionStrings:OrderRead"] = databaseConnectionString,
                    ["ConnectionStrings:Default"] = databaseConnectionString,
                    // Keycloak config must be valid to avoid options-validation errors
                    ["Keycloak:realm"] = "test",
                    ["Keycloak:auth-server-url"] = "http://localhost:8080",
                    ["Keycloak:resource"] = "order-api",
                });
        });

        builder.ConfigureTestServices(services =>
        {
            services
                .AddAuthentication(MockBearerAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, MockBearerAuthenticationHandler>(
                    MockBearerAuthenticationHandler.SchemeName,
                    configureOptions: null);

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = MockBearerAuthenticationHandler.SchemeName;
                options.DefaultChallengeScheme = MockBearerAuthenticationHandler.SchemeName;
                options.DefaultForbidScheme = MockBearerAuthenticationHandler.SchemeName;
            });
        });
    }
}
```

Also add these using statements at the top of CreateOrderTests.cs if not already present:
```csharp
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
```

- [ ] **Step 4: Build Order.IntegrationTests**

```bash
cd /workspaces/Teck.Monorepo/.claude/worktrees/auth-architecture
dotnet build tests/integration/Order.IntegrationTests/Order.IntegrationTests.csproj --no-incremental 2>&1 | tail -30
```

Expected: Build succeeded with 0 errors.

- [ ] **Step 5: Run the 3 order integration tests**

```bash
cd /workspaces/Teck.Monorepo/.claude/worktrees/auth-architecture
dotnet test tests/integration/Order.IntegrationTests/Order.IntegrationTests.csproj --no-build -v normal 2>&1 | tail -40
```

Expected: 3 tests pass (PostOrders_WithValidBody_ReturnsCreatedOrder, PostOrders_WithEmptyLines_ReturnsBadRequest, GetOrders_AfterCreation_ReturnsCreatedOrder).

If tests fail because Order.Host boots with RabbitMQ but the RabbitMQ connection string is the test container URL, check `ConnectionStrings:RabbitMq` is correctly passed. If Wolverine fails to connect to Postgres persistence (it uses a Postgres-backed durable inbox), also inject `ConnectionStrings:PostgreSql` as fallback. If Keycloak JWT options cause the host to throw during build, the mock ConfigureTestServices approach should override it — but the Keycloak options registration may still require non-null values. Inject placeholders.

- [ ] **Step 6: Commit**

```bash
git add tests/integration/Order.IntegrationTests/CreateOrderTests.cs \
        tests/integration/Order.IntegrationTests/MockBearerAuthenticationHandler.cs \
        tests/integration/Order.IntegrationTests/SharedTestcontainersCollection.cs
git commit -m "$(cat <<'EOF'
test(order): re-enable integration tests with test-only mock bearer auth

The 3 CreateOrderTests were blocked by missing ILicenseValidator — now
resolved by the Wolverine middleware guard in the prior commit. Wires a
test-only MockBearerAuthenticationHandler via ConfigureTestServices so
Order.Host boots and processes commands without real Keycloak infrastructure.
All auth infrastructure stays exclusively in the test project.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: FIX 2 — Strip Authorization header on anonymous routes only

**Files:**
- Modify: `src/services/gateway/public/Edge/Steps/HeaderFirewallStep.cs`
- Modify: `tests/unit/Gateway.Public.UnitTests/Edge/Steps/HeaderFirewallStepTests.cs`

**Interfaces:**
- Consumes: `EdgeContext.Policy.Mode` (EdgeAccessMode enum), `HttpContext.Request.Headers.Authorization`
- Produces: `HeaderFirewallStep.ExecuteAsync` strips `Authorization` only on `EdgeAccessMode.Anonymous`; authenticated routes preserve it for `ExchangeTokenStep`

- [ ] **Step 1: Edit HeaderFirewallStep.cs**

Add stripping of `Authorization` when the policy is Anonymous. Full file content:

```csharp
namespace Gateway.Public.Edge.Steps;

/// <summary>Strips client-supplied trusted internal headers so only the gateway can set them.</summary>
/// <param name="tenantOptions">The edge tenant options.</param>
public sealed class HeaderFirewallStep(EdgeTenantOptions tenantOptions) : IEdgeStep
{
    private readonly EdgeTenantOptions tenantOptions = tenantOptions;

    /// <inheritdoc/>
    public Task<EdgeStepResult> ExecuteAsync(EdgeContext context, CancellationToken ct)
    {
        // Save the client-requested tenant id BEFORE stripping so that ResolveTenantStep
        // can perform the mismatch check (client header vs. token claims) even after the
        // header has been removed from the request.
        context.ClientRequestedTenantId = TryGetHeader(
            context.HttpContext, tenantOptions.TenantIdHeaderName);

        context.HttpContext.Request.Headers.Remove(tenantOptions.TenantIdHeaderName);
        context.HttpContext.Request.Headers.Remove(EdgeHeaders.TenantDbStrategy);

        // Strip the inbound Authorization header on anonymous routes so that client bearer
        // tokens are never forwarded to upstream services unauthenticated. Authenticated
        // routes keep the header so ExchangeTokenStep can extract the user token to exchange.
        if (context.Policy.Mode == EdgeAccessMode.Anonymous)
        {
            context.HttpContext.Request.Headers.Remove("Authorization");
        }

        return Task.FromResult(EdgeStepResult.Proceed);
    }

    private static string? TryGetHeader(HttpContext http, string name) =>
        http.Request.Headers.TryGetValue(name, out var values) &&
        !string.IsNullOrWhiteSpace(values.ToString())
            ? values.ToString().Trim()
            : null;
}
```

- [ ] **Step 2: Add 2 tests to HeaderFirewallStepTests.cs**

Add after the existing `RemovesClientSuppliedTrustHeaders` test:

```csharp
/// <summary>On anonymous routes the inbound Authorization header must be stripped so bearer tokens never reach upstream unauthenticated.</summary>
[Fact]
public async Task AnonymousRoute_StripsAuthorizationHeader()
{
    var http = new DefaultHttpContext();
    http.Request.Headers["X-TenantId"] = "spoofed";
    http.Request.Headers["Authorization"] = "Bearer client-token";
    var options = new EdgeTenantOptions("X-TenantId", "organization", "tenant_id");
    var step = new HeaderFirewallStep(options);

    await step.ExecuteAsync(new EdgeContext(http, new EdgeAccessPolicy(EdgeAccessMode.Anonymous, null)), default);

    Assert.False(http.Request.Headers.ContainsKey("Authorization"));
}

/// <summary>On authenticated routes the inbound Authorization header must be preserved so ExchangeTokenStep can read the user token.</summary>
[Fact]
public async Task AuthenticatedRoute_PreservesAuthorizationHeader()
{
    var http = new DefaultHttpContext();
    http.Request.Headers["X-TenantId"] = "spoofed";
    http.Request.Headers["Authorization"] = "Bearer user-token";
    var options = new EdgeTenantOptions("X-TenantId", "organization", "tenant_id");
    var step = new HeaderFirewallStep(options);

    await step.ExecuteAsync(new EdgeContext(http, new EdgeAccessPolicy(EdgeAccessMode.Authenticated, "order")), default);

    Assert.True(http.Request.Headers.ContainsKey("Authorization"));
    Assert.Equal("Bearer user-token", http.Request.Headers.Authorization.ToString());
}
```

- [ ] **Step 3: Build and run unit tests**

```bash
cd /workspaces/Teck.Monorepo/.claire/worktrees/auth-architecture
dotnet test tests/unit/Gateway.Public.UnitTests/Gateway.Public.UnitTests.csproj --no-build -v normal 2>&1 | tail -20
```

Expected: All tests pass including the 2 new ones.

- [ ] **Step 4: Commit**

```bash
git add src/services/gateway/public/Edge/Steps/HeaderFirewallStep.cs \
        tests/unit/Gateway.Public.UnitTests/Edge/Steps/HeaderFirewallStepTests.cs
git commit -m "$(cat <<'EOF'
fix(gateway): strip Authorization header on anonymous routes only

HeaderFirewallStep now removes the client Authorization header when the
route policy is Anonymous, preventing raw bearer tokens from leaking to
upstream services. Authenticated routes keep the header so ExchangeTokenStep
can extract it for the token exchange step.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: FIX 3 — Require AuthorizationPolicy == "authenticated" for non-anonymous routes

**Files:**
- Modify: `src/services/gateway/public/Edge/EdgeAccessPolicyRegistry.cs`
- Modify: `tests/unit/Gateway.Public.UnitTests/Edge/EdgeAccessPolicyRegistryTests.cs`

**Interfaces:**
- Consumes: `IConfiguration` — `ReverseProxy:Routes:{id}:AuthorizationPolicy` key
- Produces: `EdgeAccessPolicyRegistry.Build` throws `InvalidOperationException` naming the route when `AuthorizationPolicy` is missing/blank/not-"authenticated" for non-anonymous routes

- [ ] **Step 1: Edit EdgeAccessPolicyRegistry.cs**

In the `Build` method, after resolving the audience for a non-anonymous route, also check `AuthorizationPolicy`. Full updated `Build` method (replace existing):

```csharp
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

            string? authzPolicy = route["AuthorizationPolicy"];
            if (!string.Equals(authzPolicy, "authenticated", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Route '{routeId}' is '{mode}' but its AuthorizationPolicy is " +
                    $"'{authzPolicy ?? "(null)"}' — set AuthorizationPolicy to 'authenticated'.");
            }
        }

        map[routeId] = new EdgeAccessPolicy(mode, audience);
    }

    return new EdgeAccessPolicyRegistry(map);
}
```

- [ ] **Step 2: Add 1 test to EdgeAccessPolicyRegistryTests.cs**

Add a helper config variant and a test for the missing `AuthorizationPolicy` case:

```csharp
private static IConfiguration ConfigWithAuthzPolicy(string routeMode, bool withAudience, string? authzPolicy)
{
    var dict = new Dictionary<string, string?>
    {
        ["ReverseProxy:Routes:r1:ClusterId"] = "order",
        ["ReverseProxy:Routes:r1:Metadata:EdgeAccess"] = routeMode,
    };
    if (withAudience) dict["ReverseProxy:Clusters:order:Destinations:primary:AccessTokenClientName"] = "order";
    if (authzPolicy is not null) dict["ReverseProxy:Routes:r1:AuthorizationPolicy"] = authzPolicy;
    return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
}

/// <summary>Build should throw when a non-anonymous route is missing AuthorizationPolicy == "authenticated".</summary>
[Fact]
public void Build_Throws_WhenNonAnonymousRouteMissingAuthorizationPolicy()
{
    var ex = Assert.Throws<InvalidOperationException>(() =>
        EdgeAccessPolicyRegistry.Build(ConfigWithAuthzPolicy("Authenticated", withAudience: true, authzPolicy: null)));
    Assert.Contains("r1", ex.Message);
    Assert.Contains("AuthorizationPolicy", ex.Message);
}
```

Also update the existing `Build_BindsAudience_FromClusterDestination` test to supply `AuthorizationPolicy`:
```csharp
[Fact]
public void Build_BindsAudience_FromClusterDestination()
{
    var registry = EdgeAccessPolicyRegistry.Build(ConfigWithAuthzPolicy("Authenticated", withAudience: true, authzPolicy: "authenticated"));
    var policy = registry.ForRoute("r1");
    Assert.NotNull(policy);
    Assert.Equal(EdgeAccessMode.Authenticated, policy!.Mode);
    Assert.Equal("order", policy.ExchangeAudience);
}
```

And `Build_Throws_WhenNonAnonymousRouteHasNoAudience` should also use the new helper:
```csharp
[Fact]
public void Build_Throws_WhenNonAnonymousRouteHasNoAudience()
{
    var ex = Assert.Throws<InvalidOperationException>(() =>
        EdgeAccessPolicyRegistry.Build(ConfigWithAuthzPolicy("Authenticated", withAudience: false, authzPolicy: "authenticated")));
    Assert.Contains("r1", ex.Message);
}
```

- [ ] **Step 3: Build and run unit tests**

```bash
cd /workspaces/Teck.Monorepo/.claude/worktrees/auth-architecture
dotnet test tests/unit/Gateway.Public.UnitTests/Gateway.Public.UnitTests.csproj --no-build -v normal 2>&1 | tail -20
```

Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/services/gateway/public/Edge/EdgeAccessPolicyRegistry.cs \
        tests/unit/Gateway.Public.UnitTests/Edge/EdgeAccessPolicyRegistryTests.cs
git commit -m "$(cat <<'EOF'
fix(gateway): fail-closed when AuthorizationPolicy missing on non-anonymous route

EdgeAccessPolicyRegistry.Build now requires AuthorizationPolicy == 'authenticated'
(case-insensitive) for every non-anonymous route. An InvalidOperationException naming
the route is thrown at startup if the policy is absent or set to a different value,
preventing silent security misconfigurations.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: FIX 4 — Reject blank token from exchange service

**Files:**
- Modify: `src/services/gateway/public/Edge/Steps/ExchangeTokenStep.cs`
- Modify: `tests/unit/Gateway.Public.UnitTests/Edge/Steps/ExchangeTokenStepTests.cs`

**Interfaces:**
- Consumes: `ServiceTokenResult.AccessToken` (string? from exchange service)
- Produces: `ExchangeTokenStep` returns Stop(401) when `result.AccessToken` is null/whitespace

- [ ] **Step 1: Edit ExchangeTokenStep.cs**

After the successful `ExchangeTokenAsync` call, add a null/whitespace check before setting the token. Updated `try` block:

```csharp
try
{
    ServiceTokenResult exchanged = await exchangeService
        .ExchangeTokenAsync(inbound, audience, context.ResolvedTenantId ?? "edge-no-tenant", ct)
        .ConfigureAwait(false);

    if (string.IsNullOrWhiteSpace(exchanged.AccessToken))
    {
        return EdgeStepResult.Stop(new EdgeProblem(
            401,
            "Unauthorized",
            "Token exchange returned an empty access token.",
            "authorization.token_exchange_denied"));
    }

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
```

- [ ] **Step 2: Add 1 test to ExchangeTokenStepTests.cs**

Add after the existing tests:

```csharp
/// <summary>When the exchange service returns a null/blank access token, the step stops with 401 authorization.token_exchange_denied.</summary>
[Fact]
public async Task BlankExchangedToken_Returns401TokenExchangeDenied()
{
    var http = new DefaultHttpContext();
    http.Request.Headers["Authorization"] = "Bearer inbound-token";
    var ctx = new EdgeContext(http, new EdgeAccessPolicy(EdgeAccessMode.Authenticated, "order-api"))
    {
        ResolvedTenantId = "t1",
    };
    var step = new ExchangeTokenStep(
        new FakeExchangeService(new ServiceTokenResult(string.Empty, DateTime.UtcNow.AddHours(1))));

    EdgeStepResult result = await step.ExecuteAsync(ctx, default);

    Assert.False(result.Continue);
    Assert.Equal(401, result.Problem!.StatusCode);
    Assert.Equal("authorization.token_exchange_denied", result.Problem.ErrorCode);
    Assert.Equal("Token exchange returned an empty access token.", result.Problem.Detail);
    Assert.Null(ctx.ExchangedToken);
}
```

- [ ] **Step 3: Build and run unit tests**

```bash
cd /workspaces/Teck.Monorepo/.claude/worktrees/auth-architecture
dotnet test tests/unit/Gateway.Public.UnitTests/Gateway.Public.UnitTests.csproj --no-build -v normal 2>&1 | tail -20
```

Expected: All tests pass including the new one.

- [ ] **Step 4: Commit**

```bash
git add src/services/gateway/public/Edge/Steps/ExchangeTokenStep.cs \
        tests/unit/Gateway.Public.UnitTests/Edge/Steps/ExchangeTokenStepTests.cs
git commit -m "$(cat <<'EOF'
fix(gateway): reject blank exchanged token instead of falling through

ExchangeTokenStep now stops with 401 authorization.token_exchange_denied
when the token exchange service returns a null or whitespace access token,
preventing a potential raw user-token passthrough fallback.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: FIX 5 — Remove dev tenant seed from migration; add dev-only startup seed

**Files:**
- Modify: `src/services/commerce/customer/Customer.Application/Database/Configurations/TenantConfiguration.cs`
- Modify: `src/services/commerce/customer/Customer.Host/Database/Migrations/20260628172503_InitialCustomer.cs`
- Modify: `src/services/commerce/customer/Customer.Host/Database/Migrations/CustomerDbContextModelSnapshot.cs`
- Modify: `src/services/commerce/customer/Customer.Host/Program.cs`
- Modify: `tests/integration/Customer.IntegrationTests/GetTenantDatabaseInfoTests.cs`

**Interfaces:**
- Produces: migration contains no `InsertData`; dev tenant is inserted idempotently in `Customer.Host/Program.cs` only when `IsDevelopment()`; `GetTenantDatabaseInfoTests` seeds the dev tenant itself so `RemoteHandler_ResolvesSeededDevTenant` still passes

- [ ] **Step 1: Remove HasData from TenantConfiguration.cs**

Remove the entire `builder.HasData(new { ... })` call block (lines 21–36). The `Configure` method should end after `builder.HasIndex(tenant => tenant.Identifier).IsUnique();`.

Full updated file:

```csharp
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

- [ ] **Step 2: Edit the migration to remove InsertData**

In `20260628172503_InitialCustomer.cs`, remove the `migrationBuilder.InsertData(...)` call entirely from the `Up` method. The `Up` method should only contain the `CreateTable` and `CreateIndex` calls.

Full updated `Up` method:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.CreateTable(
        name: "tenants",
        columns: table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            Identifier = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
            DatabaseStrategy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
            DatabaseProvider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
            HasReadReplicas = table.Column<bool>(type: "boolean", nullable: false),
            Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
            CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            CreatedBy = table.Column<string>(type: "text", nullable: true),
            UpdatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            UpdatedBy = table.Column<string>(type: "text", nullable: true),
            DeletedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            DeletedBy = table.Column<string>(type: "text", nullable: true),
            IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
        },
        constraints: table =>
        {
            table.PrimaryKey("PK_tenants", x => x.Id);
        });

    migrationBuilder.CreateIndex(
        name: "IX_tenants_Identifier",
        table: "tenants",
        column: "Identifier",
        unique: true);
}
```

- [ ] **Step 3: Edit the model snapshot to remove HasData**

In `CustomerDbContextModelSnapshot.cs`, remove the `b.HasData(new { ... })` block (lines 81–93). The entity block should end with `b.ToTable("tenants", (string)null);`.

- [ ] **Step 4: Update Customer.Host/Program.cs — add dev-only idempotent seed**

After migrations run (the `--migrate` path) and also at normal startup for the dev seed, add the seed. Since the seed must run before `app.Run()`, and `Program.cs` currently does not have a `--migrate` section, add an idempotent dev tenant insert guarded by `app.Environment.IsDevelopment()`.

Read the current `Program.cs` first (already read above — it calls `app.Run()` directly). We need to insert the dev tenant after the app is built but before `app.Run()`.

Updated `Program.cs`:

```csharp
using Customers.Application.Database;
using Customers.Domain.Entities;
using Customers.Host.Database;
using Customers.Host.Grpc.V1;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Grpc.Contracts.Remote.V1.Tenants;
using SharedKernel.Infrastructure;
using SharedKernel.Infrastructure.Behaviors;
using SharedKernel.Infrastructure.Hosting;
using SharedKernel.Infrastructure.Messaging.DeadLetter;
using SharedKernel.Infrastructure.Observability;
using Wolverine;

var builder = WebApplication.CreateBuilder(args);
builder.AddTeckCloudObservability();
builder.Services.AddTeckService(typeof(Program).Assembly, builder.Configuration);
builder.AddCustomerPersistence();
builder.ConfigureInternalServiceTransport();
builder.AddHandlerServer();
builder.Host.UseWolverine(opts =>
{
    opts.AddTeckBehaviors();
    opts.AddTeckDeadLetterPolicy(new DeadLetterOptions());
});

var app = builder.Build();
app.UseTeckService();
app.MapHandlers(registry =>
    registry.Register<GetTenantDatabaseInfoCommand, GetTenantDatabaseInfoCommandHandler, TenantDatabaseInfoRpcResult>());

if (app.Environment.IsDevelopment())
{
    await SeedDevTenantAsync(app);
}

app.Run();

static async Task SeedDevTenantAsync(WebApplication app)
{
    await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();
    await db.Database.MigrateAsync();

    var devTenantId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    bool exists = await db.Set<Tenant>().AnyAsync(t => t.Id == devTenantId);
    if (!exists)
    {
        db.Set<Tenant>().Add(Tenant.Create(
            devTenantId,
            identifier: "dev",
            databaseStrategy: "shared",
            databaseProvider: "postgres",
            hasReadReplicas: false));
        await db.SaveChangesAsync();
    }
}
```

**IMPORTANT**: Check what `Tenant.Create(...)` looks like in the domain. Read `Customer.Domain/Entities/Tenant.cs` before writing this. If it uses a different factory method or the Tenant type requires specific property setters, adjust accordingly.

- [ ] **Step 4b: Read Tenant.cs domain entity to determine correct factory**

```bash
cat /workspaces/Teck.Monorepo/.claude/worktrees/auth-architecture/src/services/commerce/customer/Customer.Domain/Entities/Tenant.cs
```

Use whatever the actual Tenant constructor/factory method is. If it's a direct constructor `new Tenant { Id = ..., Identifier = ... }`, use that. If there's no factory and properties are init-only, use the object initializer. Set `CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)` to match the original seed.

- [ ] **Step 5: Update GetTenantDatabaseInfoTests.cs to seed the dev tenant explicitly**

The test currently relies on the migration seed. After FIX 5, the migration no longer seeds, so the test must insert the dev tenant itself. Update `InitializeAsync`:

```csharp
public async ValueTask InitializeAsync()
{
    // Run EF migrations (no longer seeds the dev tenant — seeded explicitly below).
    await fixture.CreateSharedTestDatabaseAsync(typeof(CustomerDbContext), "Customer.Host");

    // Idempotently insert the dev tenant so the test can resolve it.
    string connectionString = fixture.GetDatabaseConnectionString("testdb_customerdbcontext");
    await SeedDevTenantAsync(connectionString);

    factory = new CustomerWebApplicationFactory(fixture);
    _ = factory.Services;
}
```

And add a `SeedDevTenantAsync` method to the test class (uses raw Npgsql or EF — prefer Npgsql to avoid needing the full EF stack):

```csharp
private static async Task SeedDevTenantAsync(string connectionString)
{
    await using var conn = new Npgsql.NpgsqlConnection(connectionString);
    await conn.OpenAsync();
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = @"
        INSERT INTO tenants
            (""Id"", ""Identifier"", ""DatabaseStrategy"", ""DatabaseProvider"",
             ""HasReadReplicas"", ""Status"", ""CreatedAt"", ""IsDeleted"")
        VALUES
            ('00000000-0000-0000-0000-0000000000a1', 'dev', 'shared', 'postgres',
             false, 'active', '2026-01-01 00:00:00+00', false)
        ON CONFLICT (""Id"") DO NOTHING";
    await cmd.ExecuteNonQueryAsync();
}
```

Also add `using Npgsql;` at the top of the file (Npgsql is already a transitive dependency of the test project).

- [ ] **Step 6: Build customer-related projects**

```bash
cd /workspaces/Teck.Monorepo/.claude/worktrees/auth-architecture
dotnet build src/services/commerce/customer/Customer.Host/Customer.Host.csproj --no-incremental 2>&1 | tail -20
dotnet build tests/integration/Customer.IntegrationTests/Customer.IntegrationTests.csproj --no-incremental 2>&1 | tail -20
```

Expected: Both build with 0 errors.

- [ ] **Step 7: Verify migration has no InsertData**

```bash
grep -n "InsertData\|HasData" \
  /workspaces/Teck.Monorepo/.claude/worktrees/auth-architecture/src/services/commerce/customer/Customer.Host/Database/Migrations/20260628172503_InitialCustomer.cs \
  /workspaces/Teck.Monorepo/.claude/worktrees/auth-architecture/src/services/commerce/customer/Customer.Host/Database/Migrations/CustomerDbContextModelSnapshot.cs \
  /workspaces/Teck.Monorepo/.claude/worktrees/auth-architecture/src/services/commerce/customer/Customer.Application/Database/Configurations/TenantConfiguration.cs
```

Expected: No output (no occurrences of `InsertData` or `HasData`).

- [ ] **Step 8: Run Customer integration tests**

```bash
cd /workspaces/Teck.Monorepo/.claude/worktrees/auth-architecture
dotnet test tests/integration/Customer.IntegrationTests/Customer.IntegrationTests.csproj --no-build -v normal 2>&1 | tail -30
```

Expected: `RemoteHandler_ResolvesSeededDevTenant` passes.

- [ ] **Step 9: Commit**

```bash
git add src/services/commerce/customer/Customer.Application/Database/Configurations/TenantConfiguration.cs \
        src/services/commerce/customer/Customer.Host/Database/Migrations/20260628172503_InitialCustomer.cs \
        src/services/commerce/customer/Customer.Host/Database/Migrations/CustomerDbContextModelSnapshot.cs \
        src/services/commerce/customer/Customer.Host/Program.cs \
        tests/integration/Customer.IntegrationTests/GetTenantDatabaseInfoTests.cs
git commit -m "$(cat <<'EOF'
fix(customer): remove dev tenant from migration; seed only in development

The dev tenant (00000000-...-a1) was inserted by the EF migration, running
in all environments including production. Remove it from TenantConfiguration.HasData,
the migration InsertData, and the model snapshot. Instead, seed the dev tenant
idempotently in Customer.Host/Program.cs guarded by IsDevelopment(). Integration
tests now seed the dev tenant explicitly via raw SQL in InitializeAsync so they
no longer depend on the migration data.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 7: Final verification + report

**Files:**
- Create: `/workspaces/Teck.Monorepo/.claude/worktrees/auth-architecture/.superpowers/sdd/task-finalfix-report.md`

- [ ] **Step 1: Run full affected build/test/lint**

```bash
cd /workspaces/Teck.Monorepo/.claude/worktrees/auth-architecture
bunx nx affected -t build test lint --base=main 2>&1 | tee /tmp/nx-final-run.txt
```

Wait for completion. Expected: all targets green.

- [ ] **Step 2: Self-review checklist**

Verify each item:
1. Order.Host boots: Wolverine no longer tries to activate IdempotencyMiddleware (no IDatabase) or LicenseEnforcementMiddleware (no ILicenseValidator).
2. Anonymous routes strip Authorization header; authenticated routes preserve it.
3. Non-anonymous routes without `AuthorizationPolicy: authenticated` throw at startup.
4. Blank exchanged token stops with 401.
5. Migration contains no `InsertData`; dev seed in `IsDevelopment()` only.
6. No production code was modified to satisfy tests — only test project changes.
7. All 3 order integration tests enabled and passing.
8. Gateway integration tests still green.
9. Customer integration tests still green (dev tenant seeded explicitly).

- [ ] **Step 3: Write the report**

Create the report file at `.superpowers/sdd/task-finalfix-report.md` with sections:
- Each fix (what changed, where, how verified)
- Order tests re-enable approach and result
- Migration change verification output
- `nx affected -t build test lint` result summary
- Files changed (absolute paths)
- Any concerns or deferred items
