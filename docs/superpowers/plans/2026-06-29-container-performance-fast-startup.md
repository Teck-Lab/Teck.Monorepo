# Container Performance & Fast Startup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Teck service container images start fast and report Kubernetes-`Ready` in under 15s (excluding the migration init container), with a smaller, non-root runtime image — without changing application code or slowing local dev.

**Architecture:** Rewrite the single shared `deploy/Containerfile.template` to publish ReadyToRun (R2R) for the build's target architecture, run on a chiseled distroless base with full ICU, and use a shell-free entrypoint. Separately tune the Kubernetes probes (add a `startupProbe`, point readiness at `/ready`) in the order base manifest and the `_template` so new services inherit the pattern.

**Tech Stack:** Docker/BuildKit (multi-stage, `# syntax=docker/dockerfile:1.7`), .NET 10 SDK + ASP.NET runtime, crossgen2 (ReadyToRun), WolverineFx static codegen, Kubernetes probes.

## Global Constraints

- **One shared build file.** Modify `deploy/Containerfile.template` only; never create per-service Dockerfiles.
- **R2R is a publish flag, never a csproj/`Directory.Build.props` property** — local `dotnet run` / Aspire must stay JIT.
- **No application code changes.** No Native AOT, no trimming, no InvariantGlobalization.
- **Full ICU required** — runtime base must be the `-extra` chiseled variant (`mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra`).
- **Multi-arch:** CI builds `linux/amd64` + `linux/arm64`; RID is derived from `$TARGETARCH` (`amd64→linux-x64`, `arm64→linux-arm64`).
- **WolverineFx `codegen write` executes the Host DLL** — it must run on a runnable, host-arch (portable) build, before the R2R publish.
- **No Kubernetes resource limits/replicas in base manifests** — those live in Teck.GitOps overlays. Base defines shape (probes/ports) only.
- **Success criterion:** pod-scheduled → K8s-`Ready` under 15s excluding the migrator, image node-cached, deps up.

---

### Task 1: Rewrite `deploy/Containerfile.template` (R2R + chiseled + shell-free entrypoint)

**Files:**
- Modify: `deploy/Containerfile.template` (full rewrite, 47 lines → new content below)

**Interfaces:**
- Consumes (build args, unchanged): `SERVICE_NAME`, `PROJECT_PATH`, `VERSION`; plus BuildKit auto-args `TARGETARCH`, `BUILDPLATFORM`.
- Produces: a runnable image whose entrypoint is `/app/apphost` (a copy of the published `${SERVICE_NAME}.Host` native host), listening on `8080`, running as the chiseled non-root `app` user (UID 1654).

- [ ] **Step 1: Replace the entire file contents**

Write `deploy/Containerfile.template` with exactly:

```dockerfile
# syntax=docker/dockerfile:1.7

ARG SERVICE_NAME
ARG PROJECT_PATH
ARG VERSION=0.0.0

# Build stage runs on the native CI architecture (no QEMU). crossgen2 then
# cross-compiles ReadyToRun for the requested TARGETARCH during publish.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG SERVICE_NAME
ARG PROJECT_PATH
ARG VERSION
ARG TARGETARCH
WORKDIR /src

# Map the Docker TARGETARCH to a .NET runtime identifier for R2R publishing.
RUN case "$TARGETARCH" in \
        amd64) echo "linux-x64"   > /tmp/rid ;; \
        arm64) echo "linux-arm64" > /tmp/rid ;; \
        *) echo "Unsupported TARGETARCH: $TARGETARCH" >&2; exit 1 ;; \
    esac

# Copy the repository and restore (host-arch) so WolverineFx has a runnable
# assembly to execute for code generation.
COPY . .
RUN --mount=type=cache,target=/root/.nuget/packages \
    --mount=type=cache,target=/root/.local/share/NuGet/v3-cache \
    dotnet restore "$PROJECT_PATH"

RUN --mount=type=cache,target=/root/.nuget/packages \
    --mount=type=cache,target=/root/.local/share/NuGet/v3-cache \
    dotnet build "$PROJECT_PATH" -c Release --no-restore /p:Version="$VERSION"

# Emit WolverineFx generated handlers into the project tree (executes the Host).
RUN cd "$(dirname "$PROJECT_PATH")" && \
    dotnet "./bin/Release/net10.0/${SERVICE_NAME}.Host.dll" codegen write

# Publish ReadyToRun for the target architecture. Framework-dependent
# (--self-contained false) to stay small on the chiseled runtime. The publish
# recompiles the generated sources into the assembly; with TypeLoadMode.Static
# Wolverine loads them at runtime with no codegen.
RUN --mount=type=cache,target=/root/.nuget/packages \
    --mount=type=cache,target=/root/.local/share/NuGet/v3-cache \
    RID="$(cat /tmp/rid)" && \
    dotnet publish "$PROJECT_PATH" -c Release -o /app/publish \
        -r "$RID" --self-contained false \
        -p:PublishReadyToRun=true /p:Version="$VERSION"

# Normalise the native apphost to a fixed name so the shell-free runtime image
# can use an exec-form entrypoint. The apphost embeds its managed assembly name
# at publish time, so renaming the executable file is safe.
RUN cp "/app/publish/${SERVICE_NAME}.Host" /app/publish/apphost

# Chiseled distroless runtime: non-root by default, minimal size, faster pull.
# The "-extra" variant bundles ICU + tzdata for full globalization (commerce).
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS runtime
ARG SERVICE_NAME
ARG VERSION=0.0.0
WORKDIR /app

LABEL org.opencontainers.image.title="${SERVICE_NAME}.Host" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.vendor="Teck" \
      org.opencontainers.image.description="Teck Monorepo service host"

COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_TieredPGO=1

EXPOSE 8080
ENTRYPOINT ["./apphost"]
```

- [ ] **Step 2: Build the order image (single-arch, native host)**

Run:
```bash
docker build -f deploy/Containerfile.template \
  --build-arg SERVICE_NAME=Order \
  --build-arg PROJECT_PATH=src/services/commerce/order/Order.Host/Order.Host.csproj \
  --build-arg VERSION=0.0.0-test \
  -t teck/order:perf-test .
```
Expected: build SUCCEEDS through all stages (restore → build → `codegen write` → R2R publish → `cp .../apphost` → runtime copy). Note the final image size for the record.

- [ ] **Step 3: Run the image and confirm liveness + non-root**

Run (no DB needed — `/alive` is dependency-free):
```bash
docker run -d --name order-perf -p 18080:8080 teck/order:perf-test
sleep 5
curl -fsS -o /dev/null -w "alive=%{http_code}\n" http://localhost:18080/alive
docker exec order-perf id -u 2>/dev/null || echo "no shell (expected on chiseled)"
docker logs order-perf | head -40
```
Expected: `alive=200`. The `id -u` exec fails with "no shell" (chiseled has no shell) — that is the expected proof it is distroless; the process itself runs as UID 1654. Logs show the host starting and listening on `8080`.

- [ ] **Step 4: Tear down the test container**

Run:
```bash
docker rm -f order-perf
```
Expected: container removed.

- [ ] **Step 5: (Optional but recommended) verify the multi-arch / RID path**

Run a cross-arch build to confirm the `TARGETARCH`→RID mapping and crossgen2 cross-compile succeed (requires buildx + binfmt; skip if unavailable and note it):
```bash
docker buildx build -f deploy/Containerfile.template \
  --platform linux/arm64 \
  --build-arg SERVICE_NAME=Order \
  --build-arg PROJECT_PATH=src/services/commerce/order/Order.Host/Order.Host.csproj \
  --build-arg VERSION=0.0.0-test .
```
Expected: build SUCCEEDS (the build stage stays on the native host via `--platform=$BUILDPLATFORM`; only the publish targets `linux-arm64`). If buildx/binfmt is unavailable, record that this step was skipped.

- [ ] **Step 6: Commit**

```bash
git add deploy/Containerfile.template
git commit -m "perf(deploy): R2R + chiseled-extra runtime, shell-free entrypoint

ReadyToRun publish (per-arch via TARGETARCH), chiseled non-root distroless
base with full ICU, and an exec-form entrypoint for faster, smaller, more
secure container startup. Fixes the hardcoded linux-x64 restore.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: Tune Kubernetes probes for fast, safe readiness

**Files:**
- Modify: `deploy/order/base/deployment.yaml` (probe block)
- Modify: `deploy/_template/base/deployment.yaml` (probe block)

**Interfaces:**
- Consumes: the service health endpoints `/alive` (liveness, dependency-free) and `/ready` (readiness, dependency/`ready`-tagged checks), both on port `8080`, served by every Teck host via `Teck.ServiceDefaults` + `SharedKernel.Infrastructure` hosting extensions.
- Produces: a `startupProbe` that gates boot, `livenessProbe` with `initialDelaySeconds: 0`, and a `readinessProbe` pointed at `/ready`. New services created from `_template` inherit this shape.

- [ ] **Step 1: Replace the probe block in `deploy/order/base/deployment.yaml`**

Find the existing block (currently `livenessProbe` → `/alive` initialDelay 10, `readinessProbe` → `/health` initialDelay 5 period 10) and replace those two probes with these three, keeping the surrounding `resources:` block and indentation (10 spaces for the probe keys, under `containers[0]`):

```yaml
          startupProbe:
            httpGet:
              path: /alive
              port: 8080
            periodSeconds: 3
            failureThreshold: 30
          livenessProbe:
            httpGet:
              path: /alive
              port: 8080
            initialDelaySeconds: 0
            periodSeconds: 30
          readinessProbe:
            httpGet:
              path: /ready
              port: 8080
            initialDelaySeconds: 3
            periodSeconds: 5
```

(`startupProbe` gives a 3s × 30 = 90s boot budget so a slow cold start is never killed by liveness; readiness polls every 5s starting at 3s, so an app ready at ~2s is observed `Ready` well under the 15s budget.)

- [ ] **Step 2: Apply the identical probe block to `deploy/_template/base/deployment.yaml`**

Replace that file's `livenessProbe`/`readinessProbe` block with the same three-probe block from Step 1 (the `_template` has no `resources:` block — that's expected; just replace the two probes with three).

- [ ] **Step 3: Validate both manifests parse as YAML**

Run:
```bash
python3 -c "import yaml; list(yaml.safe_load_all(open('deploy/order/base/deployment.yaml'))); print('order OK')"
python3 -c "import yaml,io,re; s=open('deploy/_template/base/deployment.yaml').read(); yaml.safe_load_all(io.StringIO(re.sub(r'\{[A-Z_]+\}','PLACEHOLDER',s))); print('template OK')"
```
Expected: `order OK` then `template OK`. (The template substitution replaces `{SERVICE_NAME}`/`{GROUP}` placeholders so the file is valid YAML for the parse check.)

- [ ] **Step 4: Confirm the probe paths/fields are exactly as intended**

Run:
```bash
grep -nE "Probe:|path:|initialDelaySeconds|periodSeconds|failureThreshold" deploy/order/base/deployment.yaml
```
Expected output shows `startupProbe`, `livenessProbe` (path `/alive`, `initialDelaySeconds: 0`), and `readinessProbe` (path `/ready`, `initialDelaySeconds: 3`, `periodSeconds: 5`) — and that `/health` no longer appears in a probe.

- [ ] **Step 5: Commit**

```bash
git add deploy/order/base/deployment.yaml deploy/_template/base/deployment.yaml
git commit -m "perf(deploy): add startupProbe and route readiness to /ready

Adds a startupProbe so slow cold starts are not killed by liveness, drops
liveness initialDelay to 0, and points readiness at /ready (dependency-tagged)
instead of /health. Applied to order base and the service _template.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Verification (whole-plan)

After both tasks:

1. **Image builds and boots** (Task 1 Steps 2–3): order image builds; `/alive` returns 200; container is distrolless/non-root.
2. **Probes are correct** (Task 2 Step 4): `startupProbe` present, readiness on `/ready`, liveness on `/alive`.
3. **Aspire smoke test still passes** (local dev path unaffected by R2R/chiseled — these are release-image only):
   ```bash
   nx test --project=Aspire.AppHost.IntegrationTests
   ```
   Expected: PASS (the AppHost runs services as .NET projects, not from these images, so this confirms no regression in the dev path).
4. **(Optional) startup timing for the record:** run the built image against a local Postgres + Keycloak (or via the Aspire-provided infra), time process-start → first `/ready` 200, and confirm it is within the ~1.5–2.5s estimate and the <15s end-to-end budget.

## Notes / follow-ups (out of scope for this plan)

- **EF Core compiled model** is the next lever if sub-second app-internal readiness is ever needed — it removes the model-build cost. Code/MSBuild change, tracked separately.
- catalog/customer/gateway have no `deploy/{service}/base` manifests yet; they inherit the probe pattern from `_template` when created.
