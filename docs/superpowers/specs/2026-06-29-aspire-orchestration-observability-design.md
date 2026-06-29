# Aspire Orchestration + ServiceDefaults / Observability — Design

**Date:** 2026-06-29
**Status:** Approved (pending spec review)
**Scope:** Wire up .NET Aspire (newest, 13.4.x) for the Teck platform: an AppHost that
orchestrates infra + all .NET services + the Next.js web app, and a `Teck.ServiceDefaults`
project that composes the existing rich observability and adds the Aspire-only glue
(service discovery + standard HTTP resilience). Maximize traceability/logging without
downgrading the existing Serilog + OpenTelemetry setup.

## Background / current state

- **Observability is already mature and Aspire-ready** (`SharedKernel.Infrastructure.Observability`):
  - OpenTelemetry traces + metrics with ~12 instrumentations (ASP.NET Core, HttpClient,
    Npgsql, EF Core, Runtime, FusionCache, Wolverine, YARP, Keycloak, RabbitMQ, Redis,
    gRPC client). OTLP exporters are gated on `OTEL_EXPORTER_OTLP_ENDPOINT`.
  - Serilog logging → OTLP sink **with TraceId/SpanId correlation** (`Serilog.Sinks.OpenTelemetry`),
    plus console and optional Grafana Loki sinks, with exception/correlation/environment enrichers.
  - Entry point: `builder.AddTeckCloudObservability()` (calls `ConfigureTeckCloudOpenTelemetry()`
    + `ConfigureTeckCloudSerilog()`), invoked per host in `Program.cs`.
- **Aspire is referenced but not set up**: `global.json` pins `Aspire.AppHost.Sdk`; central
  versions exist in `Directory.Packages.props`; client integrations are used
  (`EnrichNpgsqlDbContext`, `AddRedisDistributedCache("redis")`); devcontainer forwards the
  Aspire dashboard port (18888). There is **no AppHost** and **no ServiceDefaults** project.
- **Gateway already uses logical service names**: YARP destinations `http://order`,
  `http://customer` and `Services:CustomerApi:Url = http://customer` — directly compatible
  with Aspire service discovery.
- **Hosts:** `order`, `customer`, `catalog` (WolverineFx), `gateway/public` (YARP, no Wolverine).
- Connection-string names services expect: `OrderWrite`/`OrderRead`/`Default`,
  `CustomerWrite`/`CustomerRead`, `CatalogWrite`/`CatalogRead`, `redis`, and RabbitMQ.

## Goals

1. One `aspire run` boots the whole platform: infra + 4 services + web frontend, with the
   Aspire dashboard showing traces, metrics, and logs.
2. Keep the existing richer Serilog + OpenTelemetry setup as the single source of truth — no
   downgrade to the stock Aspire basic OTEL.
3. Add the genuinely-missing Aspire capabilities: **service discovery** and **standard HTTP
   resilience**.
4. No changes to the EF model / migrations. Minimal changes to service `Program.cs` files.
5. Newest stable Aspire (**13.4.6**; CommunityToolkit Bun **13.4.0**).

## Non-goals

- Orchestrating the **mobile (Expo)** app under Aspire — the Metro/Expo dev server does not fit
  the Aspire web-app model; it stays on its current workflow.
- Replacing the existing Testcontainers integration tests (they do not use Aspire and remain).
- Production/K8s deployment wiring for Aspire (the GitOps/Terraform repos own deploy). Aspire is
  for local/dev orchestration and the dashboard.
- Changing the observability instrumentation set or sinks.
- **Reproducing the production Keycloak realm or its authorization configuration.** In production
  the realm is provisioned by the **Keycloak operator**, and the per-client authorization config
  (clients, scopes, protected-resource permissions) is created manually. Aspire uses only a
  **minimal dev realm** sufficient for services to boot against an issuer; fine-grained authz is
  out of scope here.

## Design

### 1. `Teck.ServiceDefaults` project

A new class library (Aspire convention) referenced by all 4 hosts. Depends on
`SharedKernel.Infrastructure` (to reuse observability) and adds the Aspire packages
(`Microsoft.Extensions.ServiceDiscovery`, `Microsoft.Extensions.Http.Resilience`).

`public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)`:
- Calls the existing `builder.AddTeckCloudObservability()` (rich Serilog + OTEL) — **instead of**
  the stock template's basic `ConfigureOpenTelemetry()`. No double-wiring, Serilog preserved.
- `builder.Services.AddServiceDiscovery()`.
- `builder.Services.ConfigureHttpClientDefaults(http => { http.AddStandardResilienceHandler();
  http.AddServiceDiscovery(); })` — service discovery + standard resilience for all HttpClients.
- Adds the Aspire liveness convention `/alive` (tagged `live`) alongside the existing
  `/health` + `/ready` from `AddTeckService` (no duplication — `/alive` is liveness-only).

Each host's `Program.cs` replaces its standalone `builder.AddTeckCloudObservability()` call with
`builder.AddServiceDefaults()`. `AddTeckService(...)` (FastEndpoints, health, CORS, resilience)
remains as-is.

Rationale: OTEL + Serilog stay in `SharedKernel.Infrastructure.Observability`; ServiceDefaults is
a thin composition layer that adds Aspire glue. This is the "replace stock OTEL with ours, keep
Aspire's service-discovery/resilience" decision.

### 2. `Teck.AppHost` project (`aspire/Teck.AppHost`)

Uses the `Aspire.AppHost.Sdk` (13.4.6). `Program.cs` builds a `DistributedApplication`:

**Infrastructure resources:**
- `var postgres = builder.AddPostgres("postgres").WithDataVolume()` (persistent) with databases
  `AddDatabase("order")`, `AddDatabase("customer")`, `AddDatabase("catalog")`.
- `var rabbitmq = builder.AddRabbitMQ("rabbitmq").WithManagementPlugin()`.
- `var redis = builder.AddRedis("redis")`.
- `var keycloak = builder.AddKeycloak("keycloak").WithRealmImport(...)` via `Aspire.Hosting.Keycloak`.
  The realm import is a **minimal dev realm** — just enough for services and the gateway to boot
  against a valid issuer/JWKS in local dev. It deliberately does **not** reproduce the
  operator-managed production realm or the manual per-client authz config (scopes, permissions);
  see Non-goals. (`Keycloak.AuthServices.Aspire.Hosting` may be used for client-wiring helpers if
  it simplifies injecting the issuer URL into services.)

**Service projects** (each `.WaitFor(...)` its dependencies):
- `order.Host`  → references `postgres/order`, `rabbitmq`, `redis`, `keycloak`.
- `customer.Host` → references `postgres/customer`, `rabbitmq`, `redis`, `keycloak`.
- `catalog.Host` → references `postgres/catalog`, `rabbitmq`, `redis`, `keycloak`.
- `gateway.public` → references the three services (for discovery) + `keycloak` + `redis`.

**Connection-string name mapping** (so services need no config changes): the AppHost injects the
names services already read, e.g. for order:
`.WithEnvironment("ConnectionStrings__OrderWrite", orderDb)` and `__OrderRead` to the same DB
resource; `redis` and the RabbitMQ connection name are injected via `.WithReference(...)` which
already matches (`redis`) or via explicit environment for the RabbitMQ name the messaging layer
reads. Exact RabbitMQ connection-string key to be confirmed against
`SharedKernel.Infrastructure.Messaging` during implementation and mapped accordingly.

**Frontend — DEFERRED.** `@teck/web` is currently an empty scaffold (no `next` dependency, no
`dev` script), so there is nothing runnable to orchestrate. The web app is added to the AppHost
via `AddBunApp` in the same change that gives it a runnable `dev` script — a one-line addition
later. Not part of this plan.

**Redis + RabbitMQ — stood up but not yet consumed.** `AddCachingInfrastructure` (Redis) and
`WolverinePersistenceConfigurator` (RabbitMQ + Postgres Wolverine durability) exist in
`SharedKernel.Infrastructure` but are **not called by any host yet**. The AppHost runs `redis`
and `rabbitmq` and injects their connection strings into the services via `.WithReference(...)`
(connection names `redis` and the RabbitMQ name the messaging layer will read), so they are ready
the moment a consumer is wired. No service behavior depends on them in this plan, so the smoke
test does not assert on them.

**Observability:** the AppHost automatically injects `OTEL_EXPORTER_OTLP_ENDPOINT` into every
service, so the existing OTLP exporters light up the dashboard with no code change.

### 3. Gateway service discovery

`AddServiceDefaults()` already registers service discovery for HttpClients. Additionally wire YARP
to use service discovery so `http://order` destinations resolve via Aspire
(`AddServiceDiscoveryDestinationResolver()` on the reverse proxy), and ensure the FastEndpoints
remote gRPC client to `customer` uses the discovery-enabled HttpClient. Outside Aspire the same
logical names resolve from configuration/DNS — no behavioral change to prod.

### 4. Testing

- **Aspire smoke test** (new `tests/integration/Aspire.AppHost.IntegrationTests`, using
  `Aspire.Hosting.Testing` + `DistributedApplicationTestingBuilder`): boot the AppHost, wait for
  the services to reach `Healthy`, and assert `/health` (and a representative route) responds
  through the gateway. Exactly one meaningful smoke test — these are resource-heavy.
- **ServiceDefaults unit tests** (`tests/unit/...`): assert `AddServiceDefaults` registers service
  discovery + the standard resilience handler and composes observability (no exceptions, expected
  services present).
- Existing Testcontainers integration tests remain unchanged.

### 5. Housekeeping

- Bump all Aspire package versions to 13.4.6 (Bun 13.4.0; Keycloak hosting to latest compatible)
  in `Directory.Packages.props`, and `Aspire.AppHost.Sdk` to 13.4.6 in `global.json`.
- Add `Teck.AppHost`, `Teck.ServiceDefaults`, and the test project(s) to `Teck.Platform.slnx`.
- Short note in root `AGENTS.md` / `CLAUDE.md`: `aspire run` for local orchestration; dashboard on
  18888 (already forwarded by the devcontainer).
- Ensure the AppHost participates in the WolverineFx codegen story only as needed (the AppHost has
  no Wolverine handlers; the orchestrated services already handle `codegen` correctly).

## Error handling / edge cases

- **Resource startup ordering:** `.WaitFor(...)` gates services on Postgres/RabbitMQ/Redis/Keycloak
  readiness so services do not crash-loop on missing dependencies.
- **Dev migrations/seeding:** services migrate (and Customer seeds the dev tenant) on startup in
  Development as they already do; the AppHost runs them in Development, so no separate migrate step.
- **Codegen path:** unaffected — `codegen write` runs per service in the container build, not via
  the AppHost.
- **Non-Aspire runtime (prod/K8s):** services read connection strings and logical service URLs from
  configuration exactly as today; service discovery degrades to config/DNS resolution.

## Affected areas

- New: `aspire/Teck.AppHost/`, `aspire/Teck.ServiceDefaults/` (or `src/aspire/...`),
  `tests/integration/Aspire.AppHost.IntegrationTests/`, ServiceDefaults unit tests.
- Modified: 4 host `Program.cs` (swap observability call for `AddServiceDefaults`; gateway YARP
  discovery), `Directory.Packages.props`, `global.json`, `Teck.Platform.slnx`, root docs.
- Unchanged: EF model/migrations, observability internals, existing integration tests.

## Open items to confirm during implementation

- Exact RabbitMQ connection-string key read by the messaging layer (map it in the AppHost).
- Exact env var the `web` app reads for the gateway/API base URL.
- Minimal dev-realm import contents (issuer/realm name + the bare client(s) needed for services to
  validate tokens locally). Not the operator realm; not the manual authz config.
- Final project directory convention (`aspire/` vs `src/aspire/`) per repo layout norms.
