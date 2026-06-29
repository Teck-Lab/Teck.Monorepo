# Container Performance & Fast Startup Design

**Date:** 2026-06-29
**Status:** Approved (design)
**Scope:** `deploy/Containerfile.template`, `deploy/order/base/deployment.yaml`, `deploy/_template/base/deployment.yaml`

## Goal

Make the service container images start fast and become *ready* quickly, with a smaller and more secure runtime footprint — without changing application code or slowing the local development inner loop.

**Success criterion:** pod-scheduled → Kubernetes-`Ready` in **under 15 seconds**, *excluding* the migration init container, with the image node-cached and dependencies up. (Estimated app-internal readiness ~1.5–2.5s; probe cadence is the main remaining contributor.)

## Context & constraints

- **Single shared build file.** This repo owns one `Containerfile.template` parameterized by `SERVICE_NAME` / `PROJECT_PATH` / `VERSION` build args; there are no per-service Dockerfiles. All changes are made to the template.
- **The biggest startup cost is already eliminated.** WolverineFx runs in `TypeLoadMode.Static` in production (`SharedKernel.Infrastructure/Messaging/WolverinePersistenceConfigurator.cs:56`, `Behaviors/BehaviorExtensions.cs:49`) and the Dockerfile pre-generates handlers via `codegen write`, so there is **no runtime codegen on boot**. Remaining gains are container/runtime-level.
- **Native AOT and trimming are ruled out.** The code is reflection-heavy by design — EF Core migrations, `WriteAsJsonAsync`, and multiple `[RequiresDynamicCode]` / `[DynamicallyAccessedMembers]` annotations across `SharedKernel.Infrastructure`. **ReadyToRun (R2R)** is the realistic ceiling for startup improvement.
- **Multi-arch.** CI publishes `linux/amd64` + `linux/arm64`, so any R2R/RID work must key off `$TARGETARCH`.
- **Full ICU required.** This is a multi-tenant commerce platform; currency/locale formatting must work for all tenants. InvariantGlobalization is therefore **not** used.
- **Local dev must not regress.** R2R, chiseled base, and codegen are release-image concerns only; `dotnet run` / Aspire keep fast-JIT inner-loop behavior.

## Changes

### 1. `Containerfile.template` — build stage

- Add `ARG TARGETARCH`; derive a RID: `amd64 → linux-x64`, `arm64 → linux-arm64`.
- Pin the build stage to `FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build` so it runs on the native CI architecture (no QEMU emulation). This is required because the `codegen write` step **executes** the Host DLL and must run on a runnable, host-arch build.
- Fix the hardcoded `dotnet restore -r linux-x64` (a latent multi-arch bug) to use the derived RID.
- Build flow:
  1. `dotnet restore "$PROJECT_PATH"` (RID-aware)
  2. `dotnet build "$PROJECT_PATH" -c Release --no-restore` — portable, host-arch build so the Host DLL is runnable.
  3. `dotnet "${SERVICE_NAME}.Host.dll" codegen write` — emits Wolverine-generated `.cs` into the project tree.
  4. `dotnet publish "$PROJECT_PATH" -c Release -o /app/publish -r "$RID" --self-contained false -p:PublishReadyToRun=true /p:Version="$VERSION"` — cross-compiles R2R for the target arch via crossgen2 (no execution required) and compiles the generated sources into the assembly. Framework-dependent (`--self-contained false`) so the image stays small on the chiseled runtime.
- R2R is enabled via the **publish flag only**, never a `.csproj` / `Directory.Build.props` property, so local builds and Aspire remain JIT.
- After publish (still in the SDK stage, which has a shell), normalize the native apphost to a fixed name for a shell-free entrypoint:
  `cp "/app/publish/${SERVICE_NAME}.Host" /app/publish/apphost`
  The apphost embeds its managed assembly name at publish time, so copying/renaming the executable is safe.

### 2. `Containerfile.template` — runtime stage

- Base image → `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra`.
  - **Chiseled**: distroless, runs as non-root (`app` / UID 1654), minimal size → faster image pull and cold start, smaller attack surface.
  - **`-extra` variant**: bundles ICU + tzdata. Required by the full-ICU decision — plain chiseled ships no ICU and would behave as invariant globalization.
- `COPY --from=build /app/publish .`
- Environment:
  - `ASPNETCORE_HTTP_PORTS=8080`
  - `DOTNET_TieredPGO=1`
  - Keep Server GC + DATAS defaults (correct for containers in .NET 10); **no** GC overrides here — those are resource-shaped and belong in GitOps overlays.
- `EXPOSE 8080`
- `ENTRYPOINT ["./apphost"]` (replaces the shell-dependent `["sh", "-c", "exec dotnet …"]`, which chiseled cannot run).
- Retain the OCI `LABEL`s.

### 3. Kubernetes probes

Apply to `deploy/order/base/deployment.yaml` and `deploy/_template/base/deployment.yaml` so all future services inherit the pattern. Resource limits/replicas remain in GitOps overlays (base defines shape only).

- **Add `startupProbe`** → `GET /alive`, short `periodSeconds` (e.g. 3), generous `failureThreshold` (e.g. 30 ≈ 90s budget). Guarantees a slow cold start is never killed by liveness.
- **Liveness** → `GET /alive`, `initialDelaySeconds: 0` (startupProbe now gates boot), `periodSeconds: 30`.
- **Readiness** → change from `/health` (all checks) to `GET /ready` (dependency/`ready`-tagged checks: DB, message bus). `/ready` is the semantically correct gate for routing traffic.

## Out of scope

- Native AOT, IL trimming, InvariantGlobalization (ruled out above).
- GC / memory tuning by environment (lives in Teck.GitOps overlays).
- New `deploy/{service}/base` manifests for catalog/customer/gateway (only `order` has base manifests today; `_template` is updated so they inherit the probe pattern when created).

## Verification

1. Build per-service:
   ```bash
   docker build -f deploy/Containerfile.template \
     --build-arg SERVICE_NAME=Order \
     --build-arg PROJECT_PATH=src/services/commerce/order/Order.Host/Order.Host.csproj \
     --build-arg VERSION=0.0.0-test .
   ```
2. Run the image; confirm it boots as non-root and that `/alive` and `/ready` return 200.
3. Confirm the Aspire AppHost smoke test (`tests/integration/Aspire.AppHost.IntegrationTests/AppHostSmokeTests.cs`) still passes.
4. (Optional) Capture before/after startup time from container logs to confirm the R2R gain.
5. Sanity-check a multi-arch build path (buildx `--platform linux/amd64,linux/arm64`) to confirm the `TARGETARCH`→RID mapping and cross-compiled R2R publish succeed.
