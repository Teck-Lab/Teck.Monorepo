# Platform Auth Architecture — Design

**Date:** 2026-06-28
**Status:** Approved (design), pending implementation plan
**Scope:** End-to-end authentication, authorization, and multi-tenant enforcement across the Teck platform.

## 1. Summary

Auth is split along a single trust boundary:

- **Edge (public gateway):** authentication, tenant enforcement, and token exchange. The only internet-facing component.
- **Services (`order`, `customer`, …):** authorization via Keycloak protected-resource checks (`RequireProtectedResource`) on each endpoint, plus re-validation of the exchanged, audience-scoped token. Defense-in-depth — services never trust the network alone.

This spec covers three connected deliverables, implemented in phases but designed together:

- **Component A — Endpoint authZ convention:** evolve `AuthenticatedEndpoint<TReq,TResp>` so every endpoint declares its permission declaratively; enforce it with an architecture test.
- **Component B — Public edge gateway:** a YARP BFF doing authN + tenant enforcement + token exchange, redesigned (see §5) to be more idiomatic, fail-closed, and resilient than the reference.
- **Component C — `customer` tenant-authority service:** a minimal real service plus a new gRPC/remote contract that answers per-tenant database-strategy lookups for the gateway.

Reference implementation that inspired this (but which we improve on): `Teck-Lab/Teck.Cloud` → `src/gateways/Web.Public.Gateway`.

## 2. Goals / Non-goals

**Goals**
- Authentication performed at the edge; authorization performed per-service via Keycloak `RequireProtectedResource`.
- Multi-tenant isolation enforced from token claims, propagated as trusted internal headers.
- A consistent, hard-to-misuse endpoint convention shared by all services.
- Everything created ships with tests; any new database ships with EF Core migrations.

**Non-goals (this iteration)**
- `admin-gateway` (documented as a near-identical sibling for later; the public gateway already guards `/admin` paths via route security).
- A full-featured `customer` service — only the tenant-authority slice is built now.
- Per-tenant secret/connection provisioning changes — reuse the existing `TenantDbConnectionResolver` / OpenBao infra.

## 3. Architecture & request flow

```
Client ──JWT──▶ Public Gateway (YARP)
                 │ 0. Header firewall: strip client-supplied internal headers
                 │ 1. AuthN: ASP.NET auth pipeline + YARP per-route AuthorizationPolicy
                 │    (anonymous routes skip; unauthenticated rejected here, fail-closed)
                 │ 2. Tenant: read tenant(s) from token claims; if caller sent
                 │    X-TenantId it must be allowed by the token (else 403)
                 │ 3. DB-strategy: gRPC ▶ customer service ─▶ X-Tenant-DbStrategy
                 │    (FusionCache fail-safe; edge survives customer outage)
                 │ 4. Token exchange: user token ▶ per-service audience token
                 │ 5. YARP transform: forward X-TenantId + X-Tenant-DbStrategy
                 │    + exchanged Bearer
                 ▼
              Order service
                 │ 6. Validate exchanged JWT (audience = order)
                 │ 7. RequireProtectedResource("order","create") ▶ Keycloak authZ
                 │ 8. Tenant isolation (Finbuckle + EF global filter) using X-TenantId
                 ▼
              Handler runs
```

## 4. Component A — Endpoint authZ convention

Evolve `AuthenticatedEndpoint<TRequest,TResponse>` in `SharedKernel.Infrastructure.Endpoints`. Each endpoint declares its access policy as a **property** (enforceable by reflection in an architecture test); the base wires Keycloak protected-resource + audience metadata.

```csharp
// Value type describing an endpoint's access policy.
public sealed record EndpointPermission(string Resource, string Scope, string Audience)
{
    public static EndpointPermission Anonymous(string audience) => new("", "", audience);
    public bool IsAnonymous => Resource.Length == 0;
}

public abstract class AuthenticatedEndpoint<TRequest, TResponse> : Endpoint<TRequest, TResponse>
    where TRequest : notnull
{
    protected abstract EndpointPermission Permission { get; }

    public sealed override void Configure()
    {
        ConfigureEndpoint();                       // subclass: Post(), Version(), Summary()
        var p = Permission;
        Options(b =>
        {
            b.WithMetadata(new OpenApiAudienceMetadata(p.Audience));
            if (p.IsAnonymous) AllowAnonymous();
            else b.RequireProtectedResource(p.Resource, p.Scope);
        });
        if (!p.IsAnonymous) AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
    }

    protected abstract void ConfigureEndpoint();
}
```

Endpoint usage:

```csharp
public sealed class CreateOrderEndpoint(IMessageBus bus)
    : AuthenticatedEndpoint<CreateOrderRequest, OrderDto>
{
    protected override EndpointPermission Permission => new("order", "create", "public");
    protected override void ConfigureEndpoint() { Post("/orders"); Version(0); }
    public override async Task HandleAsync(CreateOrderRequest request, CancellationToken ct) { /* … */ }
}
```

**Properties:** auth can't be silently forgotten; opting out is explicit and greppable (`EndpointPermission.Anonymous(...)`); audience tagging is mandatory so OpenAPI doc-splitting stays correct; one file owns Keycloak wiring for all services.

**Migration of existing endpoints:** the two `order` endpoints currently call `AllowAnonymous()`; they move to real permissions — `CreateOrderEndpoint` → `("order","create","public")`, `GetOrderEndpoint` → `("order","read","public")`.

## 5. Component B — Public edge gateway (`Gateway.Public`)

New project under `src/services/gateway/public/`. Pipeline: `UseAuthentication → UseAuthorization → UseRouting → EdgeEnforcementMiddleware → MapReverseProxy`.

**Reused from SharedKernel (no copy):** `AddKeycloak` (authN), `ServiceTokenExchangeService` + `IServiceTokenExchangeService`, `ITenantTokenContextResolver`, FusionCache.

The six redesign decisions relative to the reference:

### 5.1 Real ASP.NET authN/authZ pipeline + YARP-native per-route authorization
Wire `UseAuthentication()` + `UseAuthorization()` and use YARP's per-route `AuthorizationPolicy` to reject unauthenticated callers **before** tenant logic. No imperative `AuthenticateAsync`/`"Bearer"` fallback inside middleware. Fail-closed by default; standard, testable policies. The tenant middleware only ever sees an authenticated principal or an explicitly anonymous route.

### 5.2 Ordered, single-responsibility edge pipeline
Replace the monolithic `TenantEnforcementMiddleware` with a railway-style sequence; each step returns `Continue` or `ShortCircuit(ProblemDetails)`:

```
HeaderFirewall → ResolveTenant → ResolveDbStrategy → ExchangeToken
```

`EdgeEnforcementMiddleware` just runs the ordered list. Each step is an independently unit-testable unit with one clear purpose.

### 5.3 Strongly-typed, fail-closed route policy resolved once at startup
Bind each route to one explicit policy at startup into a dictionary keyed by route/cluster id (O(1) per-request lookup, no config-tree walking):

```csharp
public sealed record EdgeAccessPolicy(
    EdgeAccessMode Mode,        // Anonymous | TenantFromHeader | Authenticated
    string? ExchangeAudience);  // required when Mode != Anonymous

public enum EdgeAccessMode { Anonymous, TenantFromHeader, Authenticated }
```

Startup **validation refuses to boot** if a non-anonymous route lacks an `ExchangeAudience`. A misconfiguration becomes a boot failure, never a silent raw-user-token forward (closes the audience-confusion risk in the reference's `ResolveExchangeAudience` fallback).

### 5.4 Trusted-header firewall (step 0)
First step unconditionally strips all internal trust headers from the inbound request — `X-TenantId`, `X-Tenant-DbStrategy`, and any inbound `Authorization` rewrite markers — so only the gateway can ever set them. Closes tenant/strategy spoofing.

### 5.5 `customer` dependency is non-fatal to the edge
DB-strategy lookup uses FusionCache **fail-safe** (serve last-known-good on customer error) + a short negative cache + a circuit breaker. Because a tenant's DB strategy changes ~never, serving stale on a `customer` outage is safe and keeps the edge available. Hard failure (no cached value + customer down) still returns a clean 503 with error code `tenant.lookup.unavailable`.

### 5.6 Mock auth out of the production binary
`MockBearerAuthenticationHandler` lives in test infrastructure (`WebApplicationFactory` override), not the gateway binary. As a backstop, enabling mock auth while `Environment == Production` throws at startup.

### 5.7 Gateway internals (new, gateway-local)
- `EdgeEnforcementMiddleware` (orchestrator) + the four `IEdgeStep` implementations.
- `ReverseProxyTransformExtensions.AddEdgeGatewayTransforms` — forwards `X-TenantId`, `X-Tenant-DbStrategy`, and the exchanged `Authorization` upstream.
- `RemoteTenantDatabaseStrategyResolver` (gRPC → customer) behind `ITenantDatabaseStrategyResolver`, wrapped with the §5.5 resilience.
- `EdgeAccessPolicy` registry + startup binder/validator; `EdgeTenantOptions` (header/claim names); `EdgeRouteSecurityOptions` (admin-path + employee-role guard).
- RFC-7807 `application/problem+json` error writer with `traceId` + stable machine error codes.

**Config (`appsettings.json`):** YARP `ReverseProxy` routes/clusters for `order`; route metadata declares the `EdgeAccessPolicy`; cluster destinations carry the exchange audience; `Services:CustomerApi:Url` for the gRPC remote; service discovery resolves destinations. Adding a service is config, not code.

## 6. Component C — `customer` tenant-authority service + gRPC contract

Minimal real service mirroring `order`'s clean-architecture layout (Domain → Application → Host), scoped to the tenant-authority role.

- **Contract (`SharedKernel.Grpc.Contracts`, new `Remote.V1.Tenants`):**
  `GetTenantDatabaseInfoCommand { string TenantId; string ServiceName; }` →
  `TenantDatabaseInfoRpcResult { bool Found; string DatabaseStrategy; string? ErrorDetail; }`,
  transported via `FastEndpoints.Messaging.Remote` (new package reference). Shared wire type both sides bind to.
- **Domain/persistence:** a `Tenant` aggregate (id, identifier, `DatabaseStrategy`, status) using the CQRS three-context split per repo rules (`CustomerDbContextBase` → `CustomerDbContext` write leaf + `CustomerReadDbContext`). Query logic in a `Specification` under `Application/Tenants/ReadModels/`; handlers use `IGenericReadRepository` + `IUnitOfWork`.
- **EF Core migration:** initial migration under `Customer.Host/Database/Migrations/`, backward-compatible, with a seeded development tenant. Applied via `--migrate` init-container mode like `order`.
- **Host:** registers the FastEndpoints messaging-remote **server** handler resolving `GetTenantDatabaseInfoCommand` from the repository.

`commerce/AGENTS.md` is updated to document `customer` as the platform tenant authority (the role `CustomerApiTenantStore`/`CustomerApiTenantOptions` already assume).

## 7. Multi-tenancy contract

- Tenant identity comes from token claims (`organization` / `tenant_id`, configurable via `EdgeTenantOptions`).
- A client-supplied `X-TenantId` must be one the token permits, else 403 (`tenant.mismatch`).
- Services enforce isolation via Finbuckle + the existing EF global query filter + SaveChanges interceptor, keyed off the forwarded `X-TenantId`.
- `X-Tenant-DbStrategy` lets a service select its per-tenant connection via `TenantDbConnectionResolver` / OpenBao.

## 8. Error handling

All edge failures are `application/problem+json` (RFC 7807) with `traceId` and stable machine error codes:

| Code | Status | Cause |
| --- | --- | --- |
| `tenant.header.missing` | 400 | Anonymous route, no tenant resolvable |
| `authorization.required` | 401 | Authenticated route, no/invalid principal |
| `tenant.token.missing` | 403 | Token has no org/tenant claim |
| `tenant.mismatch` | 403 | `X-TenantId` not allowed by token |
| `tenant.lookup.unavailable` | 503 | `customer` down, no cached strategy |
| `tenant.not_found` | 404 | Unknown tenant |
| `authorization.token.expired` | 401 | Token exchange: expired/invalid token |
| `authorization.token_exchange_denied` | 403 | Token exchange refused |

## 9. Testing strategy

Everything created gets tests.

- **Unit:** `EndpointPermission` + base wiring; tenant-claim resolution; each `IEdgeStep` (header firewall, tenant resolve incl. missing/mismatch, db-strategy incl. cache-hit/stale/unavailable, token exchange incl. denied/expired); `EdgeAccessPolicy` startup validation (boots vs throws); YARP transform header propagation; mock-auth-in-Production guard throws.
- **Architecture (ArchUnit, fails build):** every FastEndpoints endpoint in services derives from `AuthenticatedEndpoint<,>` and exposes a `Permission`; no raw `Endpoint<,>` in service endpoint namespaces; no `AllowAnonymous()` outside `EndpointPermission.Anonymous`.
- **Integration:** gateway via `WebApplicationFactory` + `MockBearerAuthenticationHandler` routing to `order` — asserts forwarded headers, exchanged bearer, and 401/403/503 paths; `customer` gRPC handler resolves a seeded tenant and reports not-found correctly.
- **Migrations:** customer initial migration applies cleanly on a fresh database; verified backward-compatible.

## 10. Execution constraints

- All implementation happens in a dedicated **git worktree** (isolated from the main workspace).
- Implementation is executed via **subagents** (subagent-driven development), decomposed so each task is an independently testable unit with its own verification.

## 11. Suggested implementation phasing

1. **Endpoint convention (A):** `EndpointPermission` + evolved base + ArchUnit rule + migrate `order` endpoints. Self-contained, unblocks the pattern.
2. **gRPC contract + `customer` service (C):** contract in SharedKernel, `Tenant` aggregate, migration, remote handler. Provides the gateway's tenant authority.
3. **Public gateway (B):** edge pipeline, route-policy registry, transforms, resilience, wired to `order` + `customer`.
4. **Integration + hardening:** end-to-end tests through the gateway, observability enrichment, docs (`gateway/AGENTS.md`, `commerce/AGENTS.md`).

## 12. Open items / future

- `admin-gateway` sibling when an internal admin surface exists.
- Parallelizing the independent db-strategy and token-exchange lookups if hot-path latency warrants it.
- OpenTelemetry span enrichment (tenant id, route, exchange audience, outcome) — wire via the existing `AddTeckCloudObservability`.
