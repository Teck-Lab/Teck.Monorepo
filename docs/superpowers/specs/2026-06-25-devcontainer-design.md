# Dev Container for the Teck monorepo — Design

**Date:** 2026-06-25
**Status:** Approved design, pending implementation plan
**Topic:** Add a `.devcontainer/` so the repo can be opened as a full, reproducible dev environment with Claude Code running isolated inside it.

## Goal

A "clone and Reopen in Container" experience that reproduces the **entire** toolchain so a new engineer (or Claude) can build, test, and run both stacks without installing anything on the host beyond Docker and an editor that speaks the Dev Containers spec.

Out of the box the container must be able to:

- Build & test the .NET 10 stack (`dotnet build`/`test`, SDK pinned by `global.json` = `10.0.300`).
- Build & test the TypeScript stack via Bun + Nx (`bun install`, `nx affected -t build test lint typecheck`).
- Run the **Testcontainers** integration tests (Postgres, RabbitMQ, Redis, Keycloak) — requires a working Docker daemon inside the environment.
- Launch the **Aspire** AppHost locally (also needs a container runtime + dashboard).
- Run **Claude Code** isolated inside the container with few/no permission prompts.

## Decisions (locked with the user)

| Dimension | Choice |
|---|---|
| Scope | **Full dev environment** |
| Docker access | **Docker-in-Docker** (nested daemon, `--privileged`, Codespaces-friendly) |
| Security posture | **Convenience-first** — no egress firewall; `claude --dangerously-skip-permissions` |
| Image assembly | **Dotnet base image + Dev Container Features** (declarative, minimal/no Dockerfile) |

## Architecture

A single Docker dev container built declaratively from `devcontainer.json`. The host repository is bind-mounted as the workspace; the editor (VS Code / Codespaces / JetBrains / Cursor) connects to the container, and all terminals, language servers, build tools, and Claude Code run inside it. A nested Docker daemon (Docker-in-Docker) runs inside the container so Testcontainers and Aspire can spawn sibling service containers without touching the host daemon.

```
Host editor ──► Dev container (privileged)
                 ├─ .NET 10 SDK + Bun + Node/Nx
                 ├─ Claude Code (CLI + VS Code extension)
                 └─ Docker-in-Docker daemon
                      └─ Testcontainers / Aspire service containers
                         (postgres, rabbitmq, redis, keycloak)
   host repo  ◄── bind-mounted as /workspace ──►  edits appear live on host
```

## Components / files

All new files live under `.devcontainer/`.

### 1. `.devcontainer/devcontainer.json`

The complete configuration. No custom Dockerfile in the initial version (added later only if a missing system package forces it).

- **Base image:** `mcr.microsoft.com/devcontainers/dotnet:10.0`
  - Provides the .NET SDK and a non-root `vscode` user.
  - **Verification required during implementation:** confirm the SDK bundled in this image satisfies `global.json` (`10.0.300`, `rollForward: latestMinor`). If the bundled SDK is older than `10.0.300`, layer `ghcr.io/devcontainers/features/dotnet` pinned to `10.0.300` to install the exact SDK. Do not assume — check.
- **Features:**
  - `ghcr.io/devcontainers/features/docker-in-docker` — nested daemon for Testcontainers + Aspire. Implies `--privileged`.
  - `ghcr.io/devcontainers-extra/features/bun` pinned to **`1.2.0`** — matches `packageManager: bun@1.2.0`.
  - `ghcr.io/devcontainers/features/node` (LTS) — runtime Nx needs; Nx itself is installed by `bun install` (it is a `devDependency`).
  - `ghcr.io/anthropics/devcontainer-features/claude-code:1.0` — installs the latest Claude Code CLI and adds the VS Code extension.
- **`remoteUser`: `vscode`** — non-root, which `--dangerously-skip-permissions` requires (the CLI refuses to run that flag as root).
- **`mounts`:**
  - `source=claude-code-config-${devcontainerId},target=/home/vscode/.claude,type=volume` — persists Claude auth, settings, and session history across rebuilds, isolated per project via `${devcontainerId}`.
  - A named volume for the Docker-in-Docker image store (`target=/var/lib/docker`) so pulled Testcontainers images (Postgres/RabbitMQ/Redis/Keycloak) survive rebuilds. Without this, every rebuild re-pulls all service images; the nested daemon does **not** share the host image cache. *(Exact wiring confirmed against the docker-in-docker feature's options during implementation — the feature may expose this directly rather than via a raw mount.)*
- **`containerEnv`:** `DOTNET_CLI_TELEMETRY_OPTOUT=1`, `DOTNET_NOLOGO=1`, `NUGET_XMLDOC_MODE=skip`. (No Claude telemetry opt-out or autoupdater disable — convenience-first.)
- **`forwardPorts`:** `18888` (Aspire dashboard), `3000` (Next.js dev), `8080` (service host) — each labeled via `portsAttributes`.
- **`postCreateCommand`:** `bash .devcontainer/postCreate.sh`.
- **`customizations.vscode.extensions`:** `ms-dotnettools.csdevkit`, `biomejs.biome`, `nrwl.angular-console` (Nx Console), `editorconfig.editorconfig`. (The Claude Code VS Code extension is added automatically by its feature.)

### 2. `.devcontainer/postCreate.sh`

One-time setup after the container is built:

- `dotnet restore` (warm the NuGet cache / restore the solution).
- `bun install --frozen-lockfile` (restore JS workspace deps + Nx).
- `dotnet dev-certs https` (so Aspire / Kestrel HTTPS works locally).
- Append a `claude` shell alias to the `vscode` user's shell rc (`~/.bashrc`) that adds `--dangerously-skip-permissions`, giving the convenience-first, low-prompt experience by default while still allowing the bare CLI when wanted.

The script is idempotent and tolerant of a partially-building baseline (it should not hard-fail the container build if `dotnet restore` reports project errors — known issue per project memory).

### 3. `.devcontainer/README.md`

Short operator doc:

- Prerequisites: Docker Desktop / engine + the VS Code Dev Containers extension (or Codespaces).
- How to open: "Reopen in Container".
- How to sign into Claude Code (`claude`, browser auth; paste-code fallback).
- The convenience-first security trade-off, stated plainly: the container is `--privileged` (Docker-in-Docker) and Claude runs with `--dangerously-skip-permissions`, so isolation is weak — **only use with trusted code**, and Claude can modify any file in the bind-mounted workspace (which is your real host repo).
- First-run note: the initial integration-test run pulls service images into the DinD daemon and is slow; subsequent runs are fast thanks to the `/var/lib/docker` volume.

## Data flow

1. Host editor issues "Reopen in Container" → Docker builds the image from the base + features.
2. `postCreateCommand` runs `postCreate.sh` → deps restored, dev-cert created, `claude` alias installed.
3. Engineer/Claude works in the integrated terminal: `nx affected -t ...`, `dotnet test`, `aspire run`, etc.
4. Integration tests / Aspire ask the nested Docker daemon to start service containers as siblings inside the dev container; images come from the persistent `/var/lib/docker` volume after first pull.
5. File edits write through the bind mount and appear immediately in the host repo.

## Error handling / edge cases

- **SDK mismatch:** mitigated by the explicit `global.json`-vs-image verification step; fall back to the pinned `dotnet` feature.
- **DinD first-run slowness:** mitigated by the docker image-store volume; documented in the README.
- **Root + skip-permissions conflict:** prevented by fixing `remoteUser: vscode`.
- **Auth lost on rebuild:** prevented by the `~/.claude` named volume.
- **Partially-building baseline:** `postCreate.sh` must not fail the build on known restore/build errors (see project memory: baseline does not fully compile yet).

## Testing / verification

This is configuration, not application code, so verification is manual and behavioral:

1. **Container builds** via Dev Containers CLI or "Reopen in Container".
2. **Toolchain present:** `dotnet --version` satisfies `global.json`; `bun --version` = `1.2.0`; `node --version` present; `nx --version` works; `claude --version` works.
3. **Docker works inside:** `docker run --rm hello-world` succeeds in the container.
4. **Integration tests run:** the Order integration tests (Testcontainers) start their service containers and pass (or fail only for app reasons, not Docker availability).
5. **Aspire launches:** AppHost starts and the dashboard is reachable on forwarded `18888`.
6. **Claude isolated:** `claude` signs in, auth persists across a rebuild (volume), and the `--dangerously-skip-permissions` alias works as a non-root user.

## Out of scope (YAGNI)

- Egress firewall / `init-firewall.sh` and `NET_ADMIN`/`NET_RAW` capabilities (explicitly dropped — convenience-first, and undercut by `--privileged` anyway).
- Codespaces-specific secret wiring for headless auth.
- Managed org-policy settings (`/etc/claude-code/managed-settings.json`).
- A custom Dockerfile (added later only if a system package is missing).
- Multi-architecture build tuning.

## Open items to resolve during implementation

1. Confirm `mcr.microsoft.com/devcontainers/dotnet:10.0` ships an SDK ≥ `10.0.300`; otherwise add the pinned `dotnet` feature.
2. Confirm the exact, current option/coordinates for the Bun feature (`devcontainers-extra/features/bun`) and that the `1.2.0` pin is valid.
3. Confirm whether the docker-in-docker feature persists `/var/lib/docker` via a built-in option or needs the explicit named-volume mount.
