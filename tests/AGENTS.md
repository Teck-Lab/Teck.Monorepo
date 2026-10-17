# tests/ — .NET Test Layers and Feature Verification

## Test layers

| Layer | Project examples | Use it for |
|---|---|---|
| Unit | `tests/unit/*` | Isolated decisions, validation, calculations, and handlers. No containers; use fakes for external boundaries. |
| Service integration | `tests/integration/{Basket,Catalog,Customer,Inventory,Notification,Order,Pricing}.IntegrationTests` | Exercise service HTTP/host composition against real PostgreSQL and RabbitMQ when persistence, SQL behavior, message delivery, or cross-component composition is the behavior. |
| Gateway edge | `tests/integration/Gateway.Public.IntegrationTests` | Exercise the real gateway pipeline and YARP transforms with in-process upstream/authentication doubles; no external services required. |
| Routed identity | `tests/integration/Aspire.AppHost.IntegrationTests` | Focused end-to-end proof through real Keycloak → YARP → service for identity, routing, and tenant boundaries. This boots the relevant Aspire resources and containers. |
| Architecture | `tests/architecture/*Architecture.UnitTests` | Enforce dependency and layering rules; these are runnable tests. `Teck.Platform.Arch.Tests` supplies shared rules only and is not a test suite. |

Choose the narrowest layer that proves the observable contract. A feature may need a positive success case and a meaningful negative/authorization case; add a persistence assertion when stored state is part of the contract, and a routed identity case when real authentication or tenant routing is part of it. Reuse existing suites and shared helpers; do not require one test per feature or a coverage percentage.

## Running tests

Use actual Nx project names, not product slugs. For example:

```bash
# One runnable suite, selected by Nx project name
bunx nx run Inventory.IntegrationTests:test

# Every suite affected by the current changes
bunx nx affected -t test

# Every runnable test target in the workspace
bunx nx run-many -t test --all

# Focus a real xUnit v3 class; run the project once first to restore/build
dotnet run --project tests/unit/Teck.LocalIdentity.UnitTests/Teck.LocalIdentity.UnitTests.csproj -- -class '*Realm*' -noColor
```

The repository selects Microsoft.Testing.Platform in `global.json`. The xUnit v3 in-process runner accepts `-class '*Realm*'` as shown above; do not copy VSTest `--filter FullyQualifiedName~...` examples into this workspace. Nx's `--` forwards arguments to the target command.

## Fixture lifecycle and isolation

`Teck.Platform.IntegrationTests.Shared` is a fixture/support library, not a test project and intentionally has no `test` target. Each service test assembly defines its own xUnit collection so xUnit v3 can discover the shared `ICollectionFixture`; the fixture starts one PostgreSQL and one RabbitMQ container per test project. Customer HTTP tests create a migrated database per test host and drop it after disposing the host. Other suites may use a shared database and truncate tables between cases. Keep test data scoped to the scenario and avoid assumptions about execution order. Prefer an isolated database when concurrent hosts or background work could overlap table cleanup; never make tests depend on mutable state left by another case. Aspire's shared collection is serialized because its stateful resources use data volumes.

## CI selection

`.github/workflows/ci.yml` runs `nx affected -t build test lint typecheck` in the main PR-check job, excluding `Aspire.AppHost.IntegrationTests` and `*.Architecture.UnitTests`. Dedicated sibling jobs select affected Aspire tests and architecture suites, respectively; those suites are not omitted by the exclusion. Preserve that split when editing CI, and inspect the project graph before changing exclusions.

Run the focused suite for fast feedback, then affected checks for the feature's dependency impact. Use the relevant architecture or routed Aspire suite when the change crosses those boundaries; full `run-many` is useful for broad validation, not a replacement for selecting behavioral acceptance tests.
