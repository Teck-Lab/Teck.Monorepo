# deploy/ — Deployment Dockerfiles

Shared Dockerfile template for all deployable services. Use `Containerfile.template` with per-service build args instead of creating service-specific files.

## WolverineFx Codegen

WolverineFx uses runtime code generation by default. Docker and CI builds must pre-generate the handlers before `dotnet publish` so the compiled image contains the generated code.

### When to run it

- Run it in CI and Docker build pipelines only.
- Do **not** make local development depend on it unless you are validating a release image.

### How to run it

Use the service entrypoint assembly (`.Host.dll`) and write the generated files before the Docker build starts:

```bash
dotnet <Service>.Host.dll codegen write
```

Equivalent build-target form:

```bash
dotnet msbuild <Service>.Host.csproj /t:WolverineCodegenWrite /p:RunWolverineCodegen=true
```

The repository should keep this as a pre-Docker CI step, not as an in-container local-dev default.

## Messaging / message-store schema

Two distinct schemas live in each service database, created by **two different mechanisms**. Do not conflate them.

### 1. The `wolverine` message-store schema (outbox / inbox / durable queues)

- The `wolverine` schema (tables `wolverine_incoming_envelopes`, `wolverine_outgoing_envelopes`,
  `wolverine_dead_letters`, node/control tables, …) is created **at host startup, in every
  environment**, by Wolverine's `AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate`
  (`WolverinePersistenceConfigurator.ConfigureCoreRuntime`).
- It is **NOT an EF Core migration** and is **NOT created by the `--migrate` init container**. The
  running service is the only thing that creates it.
- `CreateOrUpdate` is idempotent and Wolverine guards the DDL with a Postgres **advisory lock**, so
  multiple replicas booting concurrently converge safely rather than racing. Self-healing: if the
  schema is missing or partial, the next boot repairs it. The service's own DB user already performs
  DDL for its EF schema, so no extra privilege is required.
- Regression guard: `MessageStoreSchemaTests` (Inventory.IntegrationTests) boots a wired host on the
  broker-backed runtime and asserts the envelope tables exist via `information_schema.tables`.

### 2. The EF Core entity schema (the service's own tables)

- Applied by `LocalDatabaseMigrationExtensions.ApplyLocalDatabaseMigrations`, gated by
  `ShouldApplyMigrations` to **Development or `ASPIRE_LOCAL=true`** only. So in local/dev/Aspire runs
  the entity schema is auto-migrated on boot; in production it is **not**.
- The `deploy/{service}/base/deployment.yaml` init container (`name: migration`, `args: ["--migrate"]`)
  is intended to apply the EF schema in production, but **no code handles `--migrate` today**. Hosts
  run via `RunTeckServiceAsync` → `RunJasperFxCommands(args)`. JasperFx normalizes `["--migrate"]` to
  `["run", "--migrate"]` (leading `-` ⇒ default `run` command), and `--migrate` is not a flag of the
  `run` command. Because `JasperFxEnvironment.AutoStartHost` is `false` in a real host (only the
  integration-test factories set it `true`), the unknown flag raises `InvalidUsageException`, JasperFx
  prints "Invalid usage / Unknown argument or flag for value --migrate", falls back to the `run` help
  usage, and the process **exits with code 1**. The init container therefore fails today (it does not
  silently no-op and it does not migrate). Wiring a real EF-migration command onto `--migrate` is a
  tracked **follow-up**, out of scope for the transport work; it does not affect the `wolverine`
  message store, which self-creates per (1).

### 3. Production images must ship pre-generated Wolverine codegen

- Unchanged and load-bearing: production runs Wolverine with `TypeLoadMode.Static`, so the image must
  contain the pre-generated handler code from the `WolverineCodegenWrite` build step above. Only the
  message-store-build setting is environment-independent; codegen mode stays environment-gated
  (Dynamic in dev/tests, Static in prod).

## Convention

Base Kubernetes manifests live here, owned by service teams. Environment-specific patches live in Teck.GitOps.

```
deploy/
├── Containerfile.template          ← shared Docker build template
├── _template/base/                 ← new service starter template
├── order/base/                     ← order service base manifests
│   ├── deployment.yaml             ← owned by service team
│   ├── service.yaml                ← owned by service team
│   └── kustomization.yaml          ← owned by service team (base, no env patches)
├── catalog/base/
├── basket/base/
└── ...
```

## Who Owns What

| File | Owner | Changes How |
|------|-------|-------------|
| `deploy/{service}/base/*.yaml` | Service team | Directly — pod spec, probes, ports, volumes |
| `apps/{env}/.../kustomization.yaml` (in GitOps) | Infra team | Patches — image tag, replicas, resources, env overrides |
| Kargo Warehouse/Stage | Infra team | Promotion rules — which images, which stages |

**Golden rule:** Environment-specific values (replica counts, resource limits, feature flags) NEVER go in base manifests. They go in GitOps overlays. Base defines the shape; overlay defines the environment.
```bash
docker build -f deploy/Containerfile.template \
  --build-arg SERVICE_NAME=Catalog \
  --build-arg PROJECT_PATH=src/services/commerce/catalog/Catalog.Host/Catalog.Host.csproj \
  --build-arg VERSION=1.2.3 \
  .
```

```bash
docker build -f deploy/Containerfile.template \
  --build-arg SERVICE_NAME=Location \
  --build-arg PROJECT_PATH=src/services/operations/location/Location.Host/Location.Host.csproj \
  --build-arg VERSION=2.0.0 \
  .
```

```bash
docker build -f deploy/Containerfile.template \
  --build-arg SERVICE_NAME=ImageGenerator \
  --build-arg PROJECT_PATH=src/services/content/image-generator/ImageGenerator.Host/ImageGenerator.Host.csproj \
  --build-arg VERSION=0.9.0 \
  .
```

In CI, prefer `bunx nx run <project>:docker:build` and keep raw `docker build` for local debugging only.

## Deployment Pipeline (End-to-End)

Docker images are built here. Kubernetes manifests and promotion live in other repos:

```
Teck.Monorepo (here)          Teck.GitOps                   Teck.Terraform
─────────────────────         ──────────                    ──────────────
deploy/Containerfile.template  apps/{env}/teck-cloud/        modules/{tool}/
    │                          └─ {service}/kustomization.yaml
    │                          └─ config.yaml
    │                          └─ deployment.yaml
    │                          └─ service.yaml
    │
    ├─→ Docker image           Kargo Warehouse detects        Provisions infra
    │   ghcr.io/teck-lab/       new image tag → promotes       (PG, Redis, MQ)
    │   teck-monorepo/{group}/  Freight through stages:
    │   {service}:{version}     canary → dev → prod
    │                          └─ ArgoCD syncs overlay
    │
    └─ CI: reusable-build-sign-sbom.yml
       (SLSA L3: build → codegen → scan → SBOM → sign → push)
```

**Key rule:** Never create Kubernetes YAML in this repo. Deployment manifests belong in Teck.GitOps. Dockerfiles belong here.

## Image Naming

Images are built by CI and published to `ghcr.io/teck-lab/teck-monorepo/{group}/{service-name}`.
Tags: `{version}` (semver) + `sha-{shortSha}` (immutable reference). Release metadata uses `{group}@{version}`.

## Rules

- **Never use `latest` tag** — all tags must be semver or `sha-{hash}`
- **Multi-arch**: all images build for `linux/amd64` + `linux/arm64`
- **SBOM**: SLSA L3 provenance + signed SBOM attached to every image
- **No K8s manifests here** — Kubernetes YAML belongs in Teck.GitOps, not the monorepo
- **No Helm charts here** — Helm values/templates belong in Teck.Terraform
