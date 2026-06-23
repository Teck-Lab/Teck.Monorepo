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
├── Program.cs                       ← YARP proxy configuration
├── {Gateway}.csproj
└── Containerfile
```

## Dependencies

| Dependency | Purpose |
|-----------|---------|
| SharedKernel.Core | Configuration, extensions |
| SharedKernel.Infrastructure | Auth (JWT validation, token exchange), health checks |
| SharedKernel.Grpc.Contracts | gRPC client contracts (public gateway only) |
| Teck.Cloud.ServiceDefaults | OpenTelemetry, resilience |

## Rules

- **No service references** — gateways proxy via YARP at runtime, never reference service projects
- **No business logic** — pure routing and auth middleware
- **Auth token exchange** — BFF pattern: exchange user token for per-service audience token
