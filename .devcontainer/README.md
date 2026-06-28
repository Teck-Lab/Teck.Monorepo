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

## Security — read this

This container is **convenience-first**, not hardened:

- It runs **privileged** (required by Docker-in-Docker).
- `claude` is aliased to `claude --dangerously-skip-permissions`, so Claude runs tool calls without asking. Run the bare binary path (`$(which claude)`) if you want prompts back.
- Claude can modify any file in the bind-mounted workspace — **which is your real host repository** — and reach anything the container's network allows (there is no egress firewall).

**Only use this with trusted code, and monitor what Claude does.** Avoid mounting host secrets (`~/.ssh`, cloud credential files) into the container.
