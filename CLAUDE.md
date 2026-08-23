# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Nx monorepo for the **Teck platform** — a multi-tenant commerce platform combining .NET 10 microservices (clean architecture: Domain → Application → Host) and TypeScript Next.js 16 frontends. Backend and frontend are fully decoupled: apps never reference .NET projects; API types are generated from OpenAPI specs.

**Current state:** early scaffolding. Only the `order` service (+ the four SharedKernel projects) exists as real `.csproj` files; the other service directories under `src/services/` are placeholders documenting the intended structure. When adding a new service, mirror `order` and the conventions in `src/services/AGENTS.md`.

## The AGENTS.md tree is canonical

Detailed, authoritative conventions live in a hierarchy of `AGENTS.md` files — **read the one nearest to the code you're touching before editing.** They are the source of truth, not this file. Key ones:

- `src/services/AGENTS.md` — the most important file: full .NET microservice conventions (CQRS DbContexts, Mapperly, Ardalis specifications, ServiceScan DI, Options pattern, multi-tenancy, WolverineFx messaging, migrations).
- `src/AGENTS.md`, `src/shared/AGENTS.md`, `src/apps/AGENTS.md`, `src/packages/AGENTS.md` — layer rules.
- `src/services/commerce/AGENTS.md` (+ per-service like `order/AGENTS.md`), `operations/`, `content/`, `gateway/`.
- `deploy/AGENTS.md`, `tests/AGENTS.md`, `tools/AGENTS.md`, `specs/AGENTS.md`.

## Orca parent-issue coordination

When Orca starts this session with a Teck.Monorepo GitHub issue URL, read the
root `AGENTS.md` Orca routing section and load `teck-feature-flow` plus Orca's
version-matched full orchestration guide. Act as the parent coordinator; do not
implement, plan, review, or QA in the parent worktree.

The preferred coordinator is Claude Code on `claude-opus-5` with high effort.
Confirm the effective model rather than trusting the requested label. If that
model cannot launch because the CLI/model is unavailable, authentication or
capacity is exhausted, the effective model differs, or startup fails, record a
handoff and relaunch the same parent coordination with Codex
`gpt-5.6-sol`/high. Never run two live parent coordinators for the same issue.

## Commands

Nx orchestrates both stacks. Bun is the package manager (`packageManager: bun@1.2.5`); npm scripts in `package.json` wrap the common targets.

```bash
nx affected -t build test lint typecheck   # what PR checks run — use this before pushing
nx run-many -t build                        # build everything (or: bun run build)
nx test                                      # all tests
nx affected -t test                          # only changed projects
nx test --project=order-api                  # single project (.NET or TS)
nx graph                                      # dependency graph
nx reset                                      # clear Nx cache (bun run clean)
```

Frontend (`src/apps`, `src/packages`) — quality gates use Biome + tsc:
```bash
bun run lint        # Biome lint
bun run format      # Biome format
bun run typecheck   # tsc --noEmit
bun run generate    # regenerate @teck/api-client TS types from specs/  (run before new API integrations)
```

.NET: SDK 10.0.300 (`global.json`), central package versions (`Directory.Packages.props`), `net10.0` / nullable / implicit usings from root `Directory.Build.props`. Solution file is `Teck.Platform.slnx`.

### Local orchestration (Aspire)
`aspire run` (from `src/aspire/Teck.AppHost`) boots Postgres + Keycloak + Redis + RabbitMQ + the four services with the Aspire dashboard on http://localhost:18888 (traces/metrics/logs). RabbitMQ is now consumed: order/inventory/basket/catalog/pricing/customer attach a config-gated WolverineFx→RabbitMQ transport + Postgres outbox (`AddTeckMessaging`), so integration events flow producer → RabbitMQ → consumer; Redis is still not consumed. The web frontend is not yet orchestrated.

**Analyzers are enforced as build errors** (`TreatWarningsAsErrors=true`). The root `.editorconfig` is an **allowlist** — a `severity = none` floor plus explicitly opted-in StyleCop/IDE rules (using/member ordering, layout, file hygiene, file-scoped namespaces, and public-API XML docs via `stylecop.json`). Document public types/members; keep usings ordered and file-scoped namespaces. Full rule list and rationale: `src/services/AGENTS.md` → "Code Style & Analyzer Enforcement". Don't add blanket suppressions — opt rules into the allowlist instead.

## Architecture rules that span multiple files

These are the constraints that aren't obvious from any single file and are enforced by ArchUnitNET tests in `tests/architecture/` (they **fail the build**):

- **Layer direction:** Domain (no deps) ← Application (Domain only) ← Host (outermost). Application never references Host; endpoint/Request/Validator types must sit in the correct layer.
- **No cross-group references.** `services/commerce/*` cannot reference `services/operations/*`, and services never reference each other directly — all sharing flows through `src/shared/` (SharedKernel). Cross-service communication is async via WolverineFx → RabbitMQ integration events.
- **Repository + Unit of Work (no per-entity repositories).** Handlers depend on the SharedKernel generics `IGenericReadRepository<T,TId>` / `IGenericWriteRepository<T,TId>` + `IUnitOfWork` (`SharedKernel.Core.Database`) — never a per-entity repo, never Ardalis `IRepositoryBase<T>`, never a concrete `DbContext`. Query logic lives in `Specification` classes under `Application/{Capability}/ReadModels/`, never as LINQ scattered in handlers. An ArchUnit test fails the build if an Application type depends on a `DbContext` or `IRepositoryBase`.
- **CQRS via a three-context split:** abstract `{Service}DbContextBase` (model defined once, in Application) → `{Service}DbContext` (tracked write leaf, Application, EF migration target) + `{Service}ReadDbContext` (`AsNoTracking`, Host, derives from the base). **`IUnitOfWork.SaveChangesAsync` is the single commit point** — write repositories track but never save; command handlers call it once (load-to-mutate uses `enableTracking: true`). Do **not** re-introduce direct-`DbContext` handlers or the prior "DbContext is the unit of work, no IUnitOfWork" shortcut (now reversed). Rationale: `docs/superpowers/specs/2026-06-26-repository-unitofwork-architecture-design.md`.
- **Mapping:** Mapperly only (compile-time), in `Application/{Capability}/Mapping/`. Never hand-write mapping; never map in endpoints.
- **DI:** ServiceScan.SourceGenerator (compile-time), not Scrutor/runtime scanning. Config via Options pattern — handlers inject `IOptions<T>`, never `IConfiguration`.
- **Multi-tenancy:** every tenant-scoped entity implements `ITenantScoped`; EF global query filter + SaveChanges interceptor enforce isolation; `X-TenantId` propagates across HTTP/gRPC/messages.

## Messaging & migrations (gotchas)

- **WolverineFx** is the sole mediator *and* message bus (no separate Mediator lib). Handlers are static methods with injected deps — no `IRequest`/`IRequestHandler`. Dispatch: `InvokeAsync` (req/resp), `EnqueueAsync` (durable local, Postgres-backed), `PublishAsync` (cross-service via RabbitMQ).
- **WolverineFx uses runtime codegen.** Docker/CI builds MUST pre-generate handlers before `dotnet publish` (`dotnet msbuild <Service>.Host.csproj /t:WolverineCodegenWrite /p:RunWolverineCodegen=true`). Do **not** make local dev depend on this. See `deploy/AGENTS.md`.
- **EF Core migrations, same image two modes:** migrations live in `Host/Database/Migrations/`; `Program.cs` runs them when started with `--migrate` (used as a K8s init container), otherwise `app.Run()`. Migrations must be backward-compatible (rollback = revert image tag).

## Release & deployment boundaries

- **Never create git tags and never run `nx release` from a feature branch** — releases are CI-only. Conventional commits (`type(scope): description`) drive versioning. Release groups (`nx.json`): commerce/operations/content/gateway version *fixed* (`{group}@{version}`), web (`apps/*`, `packages/*`) *independent* (`{projectName}@{version}`).
- **This repo owns Dockerfiles only.** Use `deploy/Containerfile.template` with build args — don't create per-service Dockerfiles. **Never put Kubernetes YAML or Helm charts here:** base K8s manifests live under `deploy/{service}/base/`, but environment overlays go in **Teck.GitOps** and infra/Helm in **Teck.Terraform**. Images publish to `ghcr.io/teck-lab/...`; never use the `latest` tag (semver or `sha-{hash}` only).
- Agents hand off at the PR; CI handles everything post-merge.

## Specs / API types

`specs/*.json` are OpenAPI specs auto-generated from .NET endpoints — never hand-edit. `@teck/api-client` TS types are generated from them via `bun run generate`; never hand-edit `src/packages/api-client/src/generated/`. `nx validate-specs` checks backward compatibility before codegen.
