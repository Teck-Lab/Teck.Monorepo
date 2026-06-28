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

`postCreate.sh` runs `dotnet restore`, `bun install --frozen-lockfile`, creates an HTTPS dev cert, installs the Claude Code plugins + HUD (see below), and installs a `claude` shell alias (see Security below).

## Claude Code plugins & HUD statusline

The repo's checked-in `.claude/settings.json` is the team source of truth: it declares the enabled plugins (`superpowers`, `microsoft-docs`, `typescript-lsp`, `security-guidance`, `playwright`, `frontend-design`, `csharp-lsp`, `github` from the official marketplace, plus `claude-hud`), the extra `claude-hud` marketplace, the `dark` theme, and the claude-hud **statusLine**. The statusLine resolves `bun` from `PATH` (falling back to `~/.bun/bin/bun`) so it works for any user.

Because enabled plugins normally only install behind an interactive trust prompt, `postCreate.sh` also installs them **explicitly and headlessly** (`claude plugin marketplace add` + `claude plugin install`) into the mounted `~/.claude` volume, and seeds the claude-hud display config from `.devcontainer/claude-hud-config.json`. Edit that JSON to change which HUD elements show. Everything lands in the persistent volume, so it survives rebuilds.

## Running things

- Build/test everything: `bun run build`, `bun run test`, or `nx affected -t build test lint typecheck`.
- Integration tests (Testcontainers) and Aspire work because Docker runs **inside** the container (Docker-in-Docker). The first integration-test run pulls Postgres/RabbitMQ/Redis/Keycloak images into the nested daemon and is slow; later runs are fast because the image store is persisted across rebuilds.
- Forwarded ports: **18888** Aspire dashboard, **3000** Next.js dev, **8080** service host, **8081** Metro / Expo web, **19000** Expo Go (LAN), **19006** Expo web (legacy).

## Mobile (Expo) — light by default

Expo tooling is light: no Android SDK in the image. Develop via `bunx expo start --web` (port 8081, forwarded) or Expo Go on a device with `bunx expo start --tunnel`; native builds run in the cloud via `bunx eas-cli`. The **Expo Tools** VS Code extension is preinstalled.

### Opt-in: local Android builds (heavy)

Not installed by default (adds gigabytes; needs `/dev/kvm` for emulation; iOS cannot build on Linux). To enable, add to `.devcontainer/devcontainer.json` `features`:

```jsonc
"ghcr.io/devcontainers/features/java:1": { "version": "17" },
"ghcr.io/devcontainers/features/android-sdk:1": {}
```

and install `watchman`. Then `bunx expo run:android` builds locally.

## Claude Code

Run `claude` in the integrated terminal and follow the browser sign-in. If the callback doesn't reach the container, copy the code from the browser and paste it at the prompt. Your auth and session history persist across rebuilds via a per-project named volume, `claude-code-config-${devcontainerId}` (find it with `docker volume ls | grep claude-code-config`).

The container is built from `.devcontainer/Dockerfile` (a thin layer over the .NET 10 base image) for one reason: it pre-creates `~/.claude` owned by the `vscode` user so the named volume mounted there is **vscode-owned and writable**. Without this, Docker creates the fresh volume owned by `root`, Claude Code (running as `vscode`) can't write `~/.claude/.credentials.json`, and the browser sign-in reports success but never persists ("not signed in"). All other tooling is still installed via the `features` block.

## Security — read this

This container is **convenience-first**, not hardened:

- It runs **privileged** (required by Docker-in-Docker).
- `claude` is aliased to `claude --dangerously-skip-permissions`, so Claude runs tool calls without asking. Run the bare binary path (`$(which claude)`) if you want prompts back.
- Claude can modify any file in the bind-mounted workspace — **which is your real host repository** — and reach anything the container's network allows (there is no egress firewall).

**Only use this with trusted code, and monitor what Claude does.** Avoid mounting host secrets (`~/.ssh`, cloud credential files) into the container.
