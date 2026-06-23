# src/shared/ — SharedKernel Building Blocks

Cross-cutting code shared by ALL services. Services never depend on each other directly — all shared code flows through here.

## Building Blocks (4)

SharedKernel.Persistence has been merged into SharedKernel.Infrastructure — all multi-provider persistence helpers now live there.

| Project | Contents | Dependencies |
|---------|----------|-------------|
| `SharedKernel.Core` | Caching, CQRS, Domain primitives, Exceptions, Pagination, TenantConnection, TenantProvider | None |
| `SharedKernel.Events` | Integration event contracts (tenant, device, billing, license, product) | SharedKernel.Core |
| `SharedKernel.Grpc.Contracts` | gRPC proto contracts | Standalone |
| `SharedKernel.Infrastructure` | Auth, Behaviors, Caching, Database, Endpoints, HealthChecks, Messaging (WolverineFx mediator + transport), Middlewares, MultiTenant, OpenApi, Persistence (multi-provider helpers) | SharedKernel.Core + SharedKernel.Events + SharedKernel.Grpc.Contracts |
## Rules

- **Add sparingly** — every new type in SharedKernel forces a rebuild of all 10 services
- **No service-specific code** — SharedKernel is generic platform infrastructure, not business logic
- **Breaking changes require coordination** — a change to SharedKernel.Core impacts every service and their consumers
- **Version bumps are automatic** — any change triggers rebuild of all dependents via `nx affected`
