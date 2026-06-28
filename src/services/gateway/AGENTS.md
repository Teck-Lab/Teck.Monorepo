# src/services/gateway/ — Gateway Group

YARP reverse proxies. Single-project per gateway — no Domain, no Application, no database. Versioned together as `gateway@{version}`.

## Services

| Service | Deployed As | Purpose |
|---------|-------------|---------|
| **public-gateway** | yarp-gateway | Public-facing BFF — routes to downstream services with auth token exchange |
| **admin-gateway** | admin-gateway | Internal admin gateway — routes to admin endpoints |

## Structure

```
{gateway}/
├── Program.cs                       ← YARP proxy configuration + startup guard
├── {Gateway}.csproj
├── Edge/
│   ├── EdgeContext.cs               ← Per-request mutable state shared between steps
│   ├── EdgeAccessMode.cs            ← Authenticated | TenantFromHeader | Anonymous
│   ├── EdgeAccessPolicy.cs          ← Mode + ExchangeAudience per route
│   ├── EdgeAccessPolicyRegistry.cs  ← Fail-closed registry: throws at startup if
│   │                                   a non-anonymous route lacks an audience
│   ├── EdgeEnforcementMiddleware.cs ← Runs inside YARP's inner pipeline; reads
│   │                                   IReverseProxyFeature, executes ordered steps
│   ├── EdgeHeaders.cs               ← Header/Items key constants
│   ├── EdgeProblem.cs               ← Typed error carrier (statusCode, errorCode, detail)
│   ├── EdgeProblemWriter.cs         ← Writes RFC 9457 problem+json responses
│   ├── EdgeServiceCollectionExtensions.cs ← AddEdgePipeline() wires all edge services
│   ├── EdgeStepResult.cs            ← Proceed | Stop(problem)
│   ├── EdgeTenantOptions.cs         ← TenantIdHeaderName, OrganizationClaimName, TenantIdClaimName
│   ├── EdgeTenantOptionsExtensions.cs
│   ├── IEdgeAccessPolicyRegistry.cs
│   ├── IEdgeStep.cs                 ← Ordered step contract
│   ├── ITenantDatabaseStrategyResolver.cs
│   ├── RemoteTenantDatabaseStrategyResolver.cs ← gRPC → customer service; Polly circuit-breaker + FusionCache
│   ├── ReverseProxyTransformExtensions.cs  ← YARP transforms forwarding X-TenantId, X-Tenant-DbStrategy, exchanged token
│   ├── TenantDbStrategyResult.cs
│   └── Steps/
│       ├── HeaderFirewallStep.cs    ← #1: saves ClientRequestedTenantId then strips X-TenantId + X-Tenant-DbStrategy
│       ├── ResolveTenantStep.cs     ← #2: resolves tenant from claims/saved header; mismatch → 403
│       ├── ResolveDbStrategyStep.cs ← #3: calls ITenantDatabaseStrategyResolver; sets X-Tenant-DbStrategy
│       └── ExchangeTokenStep.cs     ← #4: exchanges inbound token via IServiceTokenExchangeService; sets X-Authorization
└── Containerfile
```

## Dependencies

| Dependency | Purpose |
|-----------|---------|
| SharedKernel.Core | Configuration, extensions |
| SharedKernel.Infrastructure | Auth (JWT validation, token exchange, ITenantTokenContextResolver), health checks |
| SharedKernel.Grpc.Contracts | gRPC client contracts (public gateway only) |

## Rules

- **No service references** — gateways proxy via YARP at runtime, never reference service projects
- **No business logic** — pure routing and auth middleware
- **Auth token exchange** — BFF pattern: exchange user token for per-service audience token via IServiceTokenExchangeService

## Public Gateway — Edge Pipeline

### Middleware order (`Program.cs`)

```
UseAuthentication     ← ASP.NET outer pipeline: validates Keycloak JWT (Bearer scheme)
UseRouting            ← YARP endpoint selection; evaluates per-route AuthorizationPolicy
UseAuthorization      ← Enforces "authenticated" policy → 401 before YARP runs
MapReverseProxy(proxy =>
  proxy.UseMiddleware<EdgeEnforcementMiddleware>()
)                     ← Edge middleware runs INSIDE YARP's inner pipeline so that
                        IReverseProxyFeature is already populated
```

**Why `EdgeEnforcementMiddleware` is inside `MapReverseProxy`**: YARP only sets
`IReverseProxyFeature` when its own endpoint's `RequestDelegate` starts executing.
Placing the middleware in the outer ASP.NET pipeline means `IReverseProxyFeature` is
always null, causing all requests to bypass the edge steps. The inner pipeline placement
is mandatory.

### Edge step execution (inside `EdgeEnforcementMiddleware`)

Each YARP-proxied route must have `Metadata: { EdgeAccess: ... }`. The middleware
reads `IReverseProxyFeature.Route.Config.RouteId`, looks up the `EdgeAccessPolicy`
from `IEdgeAccessPolicyRegistry`, then runs the ordered steps:

| Step | Class | Result on failure |
|------|-------|-------------------|
| 1. HeaderFirewall | `HeaderFirewallStep` | Saves `X-TenantId` into `EdgeContext.ClientRequestedTenantId`, then strips `X-TenantId` and `X-Tenant-DbStrategy` so clients cannot inject trusted headers downstream. |
| 2. ResolveTenant | `ResolveTenantStep` | 403 `tenant.token.missing` if no tenant claim; 403 `tenant.mismatch` if client-requested tenant ≠ token tenant; 400 `tenant.header.missing` for `TenantFromHeader` mode with no header |
| 3. ResolveDbStrategy | `ResolveDbStrategyStep` | 503 `tenant.lookup.unavailable` if gRPC fails; sets `X-Tenant-DbStrategy` header |
| 4. ExchangeToken | `ExchangeTokenStep` | 401/403 on `TokenExchangeException`; sets `X-Authorization` item |

After all steps proceed, YARP transforms (`AddEdgeGatewayTransforms`) copy
`X-TenantId`, `X-Tenant-DbStrategy`, and the exchanged `Authorization: Bearer …`
to the proxied request.

### Route metadata

All routes must declare `EdgeAccess` in their `Metadata` block:

```json
"Metadata": { "EdgeAccess": "Authenticated" }
```

Valid values: `Authenticated`, `TenantFromHeader`, `Anonymous`.
Non-anonymous routes without `Clusters:{id}:Destinations:*:AccessTokenClientName`
cause `EdgeAccessPolicyRegistry.Build` to **throw at startup** (fail-closed).

### Tenant mismatch detection

`HeaderFirewallStep` saves the client-supplied `X-TenantId` value into
`EdgeContext.ClientRequestedTenantId` BEFORE stripping the header. `ResolveTenantStep`
reads this saved value (falling back to the raw header when the firewall step was not
in the pipeline, e.g. unit tests) to detect mismatches without depending on the
(now-stripped) request header.

### Mock authentication (test-only)

The real `MockBearerAuthenticationHandler` lives **only** in the test assembly
(`tests/integration/Gateway.Public.IntegrationTests`) and is injected via
`WebApplicationFactory.ConfigureTestServices`. It is never compiled into the gateway binary.

A production guard in `Program.cs` throws `InvalidOperationException` if
`Testing:UseMockAuthentication=true` **and** the environment is Production:

```csharp
bool mockAuth = builder.Configuration.GetValue<bool>("Testing:UseMockAuthentication");
if (mockAuth && builder.Environment.IsProduction())
    throw new InvalidOperationException("Mock authentication must never be enabled in Production.");
```

Integration tests also override `IServiceTokenExchangeService` and
`ITenantDatabaseStrategyResolver` with in-process fakes, and route YARP forwards
through an in-memory `TestServer` echo handler via a custom
`IForwarderHttpClientFactory` replacement.
