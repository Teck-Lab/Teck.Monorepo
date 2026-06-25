# Dev Container Setup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `.devcontainer/` so the Teck monorepo opens as a full, reproducible dev environment (.NET 10 + Bun/Nx + Docker-in-Docker for Testcontainers/Aspire) with Claude Code running isolated inside it.

**Architecture:** A single declarative `devcontainer.json` builds from the official .NET 10 devcontainer image and layers Dev Container Features (pinned dotnet SDK, docker-in-docker, bun, node, claude-code). A `postCreate.sh` restores dependencies and wires a low-prompt `claude` alias. A `README.md` documents usage and the convenience-first security trade-off. No custom Dockerfile in v1.

**Tech Stack:** Dev Containers spec, Docker-in-Docker, .NET 10 SDK `10.0.300`, Bun `1.2.0`, Node LTS, Nx, Claude Code Dev Container Feature.

## Global Constraints

- **Base image:** `mcr.microsoft.com/devcontainers/dotnet:2.1-10.0-noble` (.NET 10 is noble-only; multi-arch amd64+arm64; ships non-root user `vscode`, home `/home/vscode`).
- **SDK pin required:** base image SDK is `10.0.2xx`, below `global.json`'s `10.0.300` → must install `ghcr.io/devcontainers/features/dotnet:2` with `"version": "10.0.300"`.
- **Bun pin:** `ghcr.io/devcontainers-extra/features/bun:1` with `"version": "1.2.0"` (matches `packageManager: bun@1.2.0`).
- **DinD:** `ghcr.io/devcontainers/features/docker-in-docker:4` — declares `privileged: true` and auto-persists `/var/lib/docker` per `${devcontainerId}`. Do **not** add `--privileged` to `runArgs`; do **not** add a manual `/var/lib/docker` mount.
- **remoteUser:** `vscode` (non-root — required so `--dangerously-skip-permissions` is allowed).
- **Claude config persistence:** named volume `claude-code-config-${devcontainerId}` → `/home/vscode/.claude`.
- **devcontainer.json is strict JSON** (no comments) so it can be validated with a JSON parser.
- **Security posture:** convenience-first — no egress firewall; `claude` aliased to `--dangerously-skip-permissions`.
- **`postCreate.sh` must not hard-fail the build** on known baseline restore/build errors (project memory: baseline does not fully compile yet).
- Spec reference: `docs/superpowers/specs/2026-06-25-devcontainer-design.md`.

---

### Task 1: `devcontainer.json` — full container configuration

**Files:**
- Create: `.devcontainer/devcontainer.json`

**Interfaces:**
- Produces: a dev container that mounts the repo as the workspace, installs the toolchain, runs `postCreateCommand: bash .devcontainer/postCreate.sh` (Task 2 supplies that script), forwards ports 18888/3000/8080, and persists `/home/vscode/.claude`.
- Consumes: nothing from other tasks (Task 2's script path is referenced by name only).

- [ ] **Step 1: Create the file**

Create `.devcontainer/devcontainer.json` with exactly this content:

```json
{
  "name": "Teck Platform",
  "image": "mcr.microsoft.com/devcontainers/dotnet:2.1-10.0-noble",
  "features": {
    "ghcr.io/devcontainers/features/dotnet:2": {
      "version": "10.0.300"
    },
    "ghcr.io/devcontainers/features/docker-in-docker:4": {},
    "ghcr.io/devcontainers-extra/features/bun:1": {
      "version": "1.2.0"
    },
    "ghcr.io/devcontainers/features/node:1": {
      "version": "lts"
    },
    "ghcr.io/anthropics/devcontainer-features/claude-code:1.0": {}
  },
  "remoteUser": "vscode",
  "containerEnv": {
    "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
    "DOTNET_NOLOGO": "1",
    "NUGET_XMLDOC_MODE": "skip"
  },
  "mounts": [
    "source=claude-code-config-${devcontainerId},target=/home/vscode/.claude,type=volume"
  ],
  "forwardPorts": [18888, 3000, 8080],
  "portsAttributes": {
    "18888": { "label": "Aspire dashboard" },
    "3000": { "label": "Next.js dev" },
    "8080": { "label": "Service host" }
  },
  "postCreateCommand": "bash .devcontainer/postCreate.sh",
  "customizations": {
    "vscode": {
      "extensions": [
        "ms-dotnettools.csdevkit",
        "biomejs.biome",
        "nrwl.angular-console",
        "editorconfig.editorconfig"
      ]
    }
  }
}
```

- [ ] **Step 2: Validate it is well-formed JSON**

Run: `python3 -m json.tool .devcontainer/devcontainer.json > /dev/null && echo OK`
Expected: prints `OK` (no parse error).

- [ ] **Step 3: Assert the required invariants are present**

Run:
```bash
python3 - <<'PY'
import json
c = json.load(open(".devcontainer/devcontainer.json"))
assert c["image"] == "mcr.microsoft.com/devcontainers/dotnet:2.1-10.0-noble", c["image"]
assert c["remoteUser"] == "vscode"
f = c["features"]
assert f["ghcr.io/devcontainers/features/dotnet:2"]["version"] == "10.0.300"
assert f["ghcr.io/devcontainers-extra/features/bun:1"]["version"] == "1.2.0"
assert "ghcr.io/devcontainers/features/docker-in-docker:4" in f
assert "ghcr.io/anthropics/devcontainer-features/claude-code:1.0" in f
assert c["postCreateCommand"] == "bash .devcontainer/postCreate.sh"
assert "--privileged" not in json.dumps(c), "DinD feature handles privileged; do not add runArgs"
assert "/var/lib/docker" not in json.dumps(c), "DinD feature auto-persists; no manual mount"
m = "\n".join(c["mounts"])
assert "claude-code-config-${devcontainerId}" in m and "/home/vscode/.claude" in m
print("invariants OK")
PY
```
Expected: prints `invariants OK`.

- [ ] **Step 4: Commit**

```bash
git add .devcontainer/devcontainer.json
git commit -m "feat(devcontainer): add devcontainer.json with full toolchain"
```

---

### Task 2: `postCreate.sh` — dependency restore + claude alias

**Files:**
- Create: `.devcontainer/postCreate.sh`

**Interfaces:**
- Consumes: invoked by Task 1's `postCreateCommand` as `bash .devcontainer/postCreate.sh`, running as `vscode` with the workspace as CWD.
- Produces: restored NuGet + Bun deps, an HTTPS dev cert, and a `claude` alias in `~/.bashrc` that adds `--dangerously-skip-permissions`.

- [ ] **Step 1: Create the script**

Create `.devcontainer/postCreate.sh` with exactly this content:

```bash
#!/usr/bin/env bash
# One-time setup after the dev container is built. Runs as the `vscode` user
# with the repository as the working directory.
#
# NOTE: this script is intentionally tolerant of a partially-building baseline
# (the monorepo does not fully compile yet). Dependency-restore failures are
# reported but do NOT fail the container build, so the environment still comes
# up and an engineer can fix things from inside it.
set -uo pipefail

echo "==> Restoring .NET dependencies (dotnet restore)"
dotnet restore || echo "WARN: dotnet restore reported errors (continuing; baseline may not fully build yet)"

echo "==> Installing JS workspace dependencies (bun install)"
bun install --frozen-lockfile || echo "WARN: bun install reported errors (continuing)"

echo "==> Trusting a local HTTPS development certificate"
dotnet dev-certs https || echo "WARN: could not create HTTPS dev cert (continuing)"

echo "==> Installing low-prompt 'claude' alias in ~/.bashrc"
ALIAS_LINE="alias claude='claude --dangerously-skip-permissions'"
if ! grep -qxF "$ALIAS_LINE" "$HOME/.bashrc" 2>/dev/null; then
  printf '\n# Convenience-first: run Claude Code without permission prompts inside the isolated container\n%s\n' "$ALIAS_LINE" >> "$HOME/.bashrc"
fi

echo "==> postCreate complete"
```

- [ ] **Step 2: Make it executable**

Run: `chmod +x .devcontainer/postCreate.sh && echo done`
Expected: prints `done`.

- [ ] **Step 3: Lint the script for syntax errors**

Run: `bash -n .devcontainer/postCreate.sh && echo "syntax OK"`
Expected: prints `syntax OK`.

- [ ] **Step 4: Smoke-test the alias-install logic is idempotent**

Run:
```bash
HOME=$(mktemp -d) bash -c '
  set -e
  ALIAS_LINE="alias claude='"'"'claude --dangerously-skip-permissions'"'"'"
  # simulate the script body twice
  for i in 1 2; do
    if ! grep -qxF "$ALIAS_LINE" "$HOME/.bashrc" 2>/dev/null; then
      printf "\n%s\n" "$ALIAS_LINE" >> "$HOME/.bashrc"
    fi
  done
  n=$(grep -cF "alias claude=" "$HOME/.bashrc")
  test "$n" -eq 1 && echo "idempotent OK ($n alias line)"
'
```
Expected: prints `idempotent OK (1 alias line)` — the alias is added once even when the logic runs twice.

- [ ] **Step 5: Verify executable bit is committed**

Run: `git add .devcontainer/postCreate.sh && git ls-files -s .devcontainer/postCreate.sh`
Expected: mode begins with `100755` (executable) — e.g. `100755 <hash> 0 .devcontainer/postCreate.sh`.

- [ ] **Step 6: Commit**

```bash
git commit -m "feat(devcontainer): add postCreate.sh for deps and claude alias"
```

---

### Task 3: `README.md` — operator documentation

**Files:**
- Create: `.devcontainer/README.md`

**Interfaces:**
- Consumes: documents the behavior produced by Tasks 1 and 2 (forwarded ports, `claude` alias, persisted auth volume, DinD).
- Produces: human-facing docs only; nothing depends on it.

- [ ] **Step 1: Create the file**

Create `.devcontainer/README.md` with exactly this content:

````markdown
# Dev Container

A full, reproducible dev environment for the Teck monorepo. The host repo is bind-mounted as the workspace; the editor connects to the container and all terminals, build tools, language servers, and **Claude Code** run inside it.

## Prerequisites

- Docker (Docker Desktop on macOS/Windows, or Docker Engine on Linux).
- An editor that speaks the Dev Containers spec: VS Code + the [Dev Containers extension](https://marketplace.visualstudio.com/items?itemName=ms-vscode-remote.remote-containers), GitHub Codespaces, a JetBrains IDE, or Cursor.

## Open it

In VS Code: Command Palette → **Dev Containers: Reopen in Container** (first build pulls images and runs `postCreate.sh`, so it takes a few minutes).

## What's inside

| Tool | Version | How |
|---|---|---|
| .NET SDK | `10.0.300` (pinned to `global.json`) | `dotnet` feature on top of the .NET 10 image |
| Bun | `1.2.0` | `bun` feature |
| Node | LTS | `node` feature (Nx runtime) |
| Docker | Docker-in-Docker | `docker-in-docker` feature |
| Claude Code | latest | `claude-code` feature (CLI + VS Code extension) |

`postCreate.sh` runs `dotnet restore`, `bun install --frozen-lockfile`, creates an HTTPS dev cert, and installs a `claude` shell alias (see Security below).

## Running things

- Build/test everything: `bun run build`, `bun run test`, or `nx affected -t build test lint typecheck`.
- Integration tests (Testcontainers) and Aspire work because Docker runs **inside** the container (Docker-in-Docker). The first integration-test run pulls Postgres/RabbitMQ/Redis/Keycloak images into the nested daemon and is slow; later runs are fast because the image store is persisted across rebuilds.
- Forwarded ports: **18888** Aspire dashboard, **3000** Next.js dev, **8080** service host.

## Claude Code

Run `claude` in the integrated terminal and follow the browser sign-in. If the callback doesn't reach the container, copy the code from the browser and paste it at the prompt. Your auth and session history persist across rebuilds via a per-project named volume, `claude-code-config-${devcontainerId}` (find it with `docker volume ls | grep claude-code-config`).

## Security — read this

This container is **convenience-first**, not hardened:

- It runs **privileged** (required by Docker-in-Docker).
- `claude` is aliased to `claude --dangerously-skip-permissions`, so Claude runs tool calls without asking. Run the bare binary path (`$(which claude)`) if you want prompts back.
- Claude can modify any file in the bind-mounted workspace — **which is your real host repository** — and reach anything the container's network allows (there is no egress firewall).

**Only use this with trusted code, and monitor what Claude does.** Avoid mounting host secrets (`~/.ssh`, cloud credential files) into the container.
````

- [ ] **Step 2: Validate it is well-formed Markdown (parses, non-empty)**

Run: `test -s .devcontainer/README.md && grep -q "Security — read this" .devcontainer/README.md && echo "README OK"`
Expected: prints `README OK`.

- [ ] **Step 3: Commit**

```bash
git add .devcontainer/README.md
git commit -m "docs(devcontainer): add README with usage and security notes"
```

---

### Task 4: End-to-end build verification (manual / requires Docker)

**Files:**
- None (verification only).

**Interfaces:**
- Consumes: all three files from Tasks 1–3.
- Produces: a confirmed-working container, or a list of concrete failures to fix.

This task **cannot be fully automated from this session** because it requires Docker and the Dev Containers tooling. Perform it on a machine with Docker (or in CI/Codespaces). If the `@devcontainers/cli` is available, the steps below run headlessly; otherwise use VS Code "Reopen in Container" and run the checks in the integrated terminal.

- [ ] **Step 1: Build the container (headless CLI path, if available)**

Run:
```bash
npx -y @devcontainers/cli up --workspace-folder .
```
Expected: build completes; final output reports the container is running. (If `@devcontainers/cli` is unavailable, use VS Code "Reopen in Container" instead.)

- [ ] **Step 2: Verify the toolchain versions inside the container**

Run (via `... exec --workspace-folder .` or the container terminal):
```bash
dotnet --version          # expect 10.0.300 or a newer 10.0.3xx patch (global.json rollForward: latestMinor)
bun --version             # expect 1.2.0
node --version            # expect an LTS major
nx --version              # expect Nx to report a version
claude --version          # expect a Claude Code version
whoami                    # expect: vscode
```
Expected: `dotnet --version` reports `10.0.300`; `bun --version` reports `1.2.0`; `whoami` is `vscode`; the rest report versions without error.

- [ ] **Step 3: Verify Docker works inside the container (DinD)**

Run: `docker run --rm hello-world`
Expected: the "Hello from Docker!" message prints (nested daemon is functional → Testcontainers/Aspire will work).

- [ ] **Step 4: Verify the `claude` alias is active in an interactive shell**

Run: `bash -lic 'type claude'`
Expected: output shows `claude is aliased to \`claude --dangerously-skip-permissions\``.

- [ ] **Step 5: Spot-check an integration test path (optional, slow)**

Run: `nx test --project=Order.IntegrationTests` (or the equivalent `dotnet test` for the Order integration project).
Expected: Testcontainers starts its service containers without Docker-availability errors. (Application-level test failures, if any, are out of scope for this devcontainer task — only Docker availability is being verified here.)

- [ ] **Step 6: Record the outcome**

If all checks pass, this task is complete — no commit needed (verification only). If anything fails, capture the exact error and loop back to the relevant task (image/SDK → Task 1; restore/alias → Task 2).

---

## Self-Review

**Spec coverage:**
- Full dev environment (.NET + Bun/Nx + DinD + Aspire) → Task 1 (features) + Task 4 (verification). ✓
- Docker-in-Docker for Testcontainers/Aspire → Task 1 DinD feature; Task 4 Step 3/5. ✓
- Convenience-first (no firewall; skip-permissions) → Task 2 alias; Task 3 Security section. ✓
- Dotnet image + Features (pinned SDK 10.0.300, Bun 1.2.0) → Task 1 + Global Constraints. ✓
- Persist Claude auth across rebuilds → Task 1 `mounts`. ✓
- Forwarded ports 18888/3000/8080 → Task 1 `forwardPorts`/`portsAttributes`. ✓
- `postCreate.sh` (restore, dev-certs, alias, tolerant of broken baseline) → Task 2. ✓
- README with security trade-off → Task 3. ✓
- DinD privileged + `/var/lib/docker` auto-persist (no manual mount) → Global Constraints + Task 1 Step 3 assertions. ✓
- Out of scope (firewall, managed settings, Dockerfile, Codespaces secrets) → not present in any task. ✓

**Placeholder scan:** no TBD/TODO; every file's full content is inline. ✓

**Type/identifier consistency:** feature IDs, the image tag, the volume name `claude-code-config-${devcontainerId}`, the alias text, and the `postCreateCommand` string match across Tasks 1–4 and the assertions that check them. ✓
