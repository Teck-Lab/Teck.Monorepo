# src/services/gateway/ — Gateway Group

YARP reverse proxies. Single-project per gateway — no Domain, no Application, no database. Versioned together as `gateway@{version}`.

## Implemented and intended web boundaries

| Boundary | Status | Purpose |
|----------|--------|---------|
| `public/` (`Gateway.Public`, deployed as `yarp-gateway`) | Implemented | YARP for selected authenticated Catalog, Order and Pricing routes; currently used by the staff dashboard |
| `web-customer` | Planned, not implemented | YARP BFF for the Next.js customer web app; expose only customer-approved service paths and a customer-safe, versioned OpenAPI/Scalar contract |
| `web-admin` | Planned, not implemented | YARP BFF for the internal-operator Next.js dashboard; expose approved shared/customer and admin operations, with developer/internal OpenAPI/Scalar access |

There is no implemented `admin-gateway` project. These are gateway boundaries, not additional frontend applications. Next.js currently owns the dashboard's browser session; no gateway-owned SPA cookie session is implemented. Both planned gateways must exchange authenticated inbound user tokens for each downstream service audience. Guest routes without a user token require a separate explicit policy; they cannot silently bypass token exchange.

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
│       └── ExchangeTokenStep.cs     ← #4: exchanges an inbound token via IServiceTokenExchangeService; stores the result in HttpContext.Items
└── Containerfile
```

## Dependencies

| Dependency | Purpose |
|-----------|---------|
| SharedKernel.Core | Configuration, extensions |
| SharedKernel.Infrastructure | Auth (JWT validation, token exchange, ITenantTokenContextResolver), health checks |
| SharedKernel.Grpc.Contracts | gRPC tenant metadata contract used by the implemented public gateway; tenant-scoped operator routes need an authoritative strategy too |

## Rules

- **No service references** — gateways proxy via YARP at runtime, never reference service projects
- **No business logic** — pure routing and auth middleware
- **Auth token exchange** — BFF pattern: exchange authenticated user tokens for per-service audience tokens via IServiceTokenExchangeService; an absent inbound token currently makes `ExchangeTokenStep` proceed, so protected/guest routes need an explicit policy before broad exposure

## Endpoint classification and documentation: current state

`src/shared/SharedKernel.Infrastructure/Endpoints/EndpointPermission.cs` declares a protected resource, scope, and `Audience`. `AuthenticatedEndpoint` applies Keycloak protected-resource authorization (or `AllowAnonymous`) and attaches `OpenApiAudienceMetadata`. That audience filters service-host OpenAPI operations; it does **not** select YARP routes or make an endpoint safe for customer exposure. Existing `"public"` audiences include protected Catalog management operations. The gateway's `ReverseProxy:Routes` independently select paths, methods, upstream clusters, edge modes and exchange audiences; they are currently maintained by hand in `public/appsettings.json`.

Only Catalog and Pricing hosts currently register `AddOpenApiInfrastructure`/`UseOpenApiInfrastructure`, serving versioned `/openapi/{documentName}/openapi.json` and `/docs` (Scalar) via shared infrastructure. Customer has HTTP profile endpoints but does not currently register those OpenAPI/Scalar helpers. The public gateway does not register OpenAPI or Scalar. The dashboard's checked-in TypeScript client snapshot is assembled from live Catalog and Pricing **service-host** documents, not generated from a gateway document (`src/packages/api-client/scripts/sync-openapi.ts`). Service documents include `vN` and `vN-public` registrations; Scalar currently lists `vN`. Do not infer that endpoint declarations with `Version(0)` are already exposed at a particular `/api/vN/...` path without checking actual host routes.

Before adding catch-all routes for `web-customer` or `web-admin`, classify and migrate service paths and permissions deliberately; prefix or document audience alone is not an authorization boundary. Keep each gateway's per-service destination explicit, reject unintended future endpoint exposure, and prove its published OpenAPI contains only reachable operations. If paths are changed in services, migrate callers, tests, generated clients and docs together; do not assume YARP automatically discovers `EndpointPermission.Audience`. Tenant IDs come from authenticated claims or an explicit guest policy. The implemented gateway asks Customer for the tenant's **database strategy**, not the caller's identity; tenant-scoped operator requests may still need that lookup, while platform-wide requests without tenant data may not.

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

The implemented public gateway's configured routes declare `Metadata: { EdgeAccess: ... }`. The registry currently defaults omitted metadata to `Authenticated` rather than rejecting it. The middleware
reads `IReverseProxyFeature.Route.Config.RouteId`, looks up the `EdgeAccessPolicy`
from `IEdgeAccessPolicyRegistry`, then runs the ordered steps:

| Step | Class | Result on failure |
|------|-------|-------------------|
| 1. HeaderFirewall | `HeaderFirewallStep` | Saves `X-TenantId` into `EdgeContext.ClientRequestedTenantId`, then strips `X-TenantId` and `X-Tenant-DbStrategy` so clients cannot inject trusted headers downstream. |
| 2. ResolveTenant | `ResolveTenantStep` | 403 `tenant.token.missing` if no tenant claim; 403 `tenant.mismatch` if client-requested tenant ≠ token tenant; 400 `tenant.header.missing` for `TenantFromHeader` mode with no header |
| 3. ResolveDbStrategy | `ResolveDbStrategyStep` | 503 `tenant.lookup.unavailable` if gRPC fails; sets `X-Tenant-DbStrategy` header |
| 4. ExchangeToken | `ExchangeTokenStep` | 401/403 on `TokenExchangeException`; stores an exchanged token in `HttpContext.Items` for the YARP transform; currently proceeds if no inbound token is present |

After all steps proceed, YARP transforms (`AddEdgeGatewayTransforms`) copy
`X-TenantId`, `X-Tenant-DbStrategy`, and the exchanged `Authorization: Bearer …`
to the proxied request.

### Route metadata

The implemented public gateway's routes declare `EdgeAccess` in their `Metadata` block:

```json
"Metadata": { "EdgeAccess": "Authenticated" }
```

Valid values: `Authenticated`, `TenantFromHeader`, `Anonymous`. The current registry defaults an omitted `EdgeAccess` to `Authenticated`; it is not an explicit-metadata guard.
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
