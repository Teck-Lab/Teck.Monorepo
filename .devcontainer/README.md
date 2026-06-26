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
| GitHub CLI | latest | `github-cli` feature (`gh`) |
| Claude Code | latest | `claude-code` feature (CLI + VS Code extension) |

`postCreate.sh` runs `dotnet restore`, `bun install --frozen-lockfile`, creates an HTTPS dev cert, wires `gh` as the git credential helper, configures SSH commit signing, and installs a `claude` shell alias (see Security below).

## Git, GitHub & commit signing

The goal: authenticate once, and have it survive rebuilds **and** be shared by every dev container.

- **Git identity & HTTPS push (VS Code):** VS Code automatically copies your host `~/.gitconfig` (so `user.name`/`user.email` are set) and proxies your host git credentials into the container, so `git push` over HTTPS works with no setup.
- **`gh` CLI:** run **`gh auth login` once**. The token is stored in `~/.config/gh`, which is backed by the fixed-name `shared-gh-config` volume — so the login persists across rebuilds and is reused by any other container that mounts the same volume. `postCreate.sh` also runs `gh auth setup-git`, so git HTTPS operations can fall back to the `gh` token too.
- **Commit signing (SSH) → green "Verified":** if you run a local `ssh-agent` with a key loaded, VS Code forwards the agent into the container. `postCreate.sh` then enables SSH commit signing (`gpg.format=ssh`, `commit.gpgsign=true`) using that key — **no private key is ever copied into the container**, only the agent socket is forwarded. An inherited GPG signing config is left untouched. `~/.ssh` is backed by the `shared-ssh` volume, so `known_hosts` (and anything else you put there) persists and is shared too.

  For commits to show **Verified** on GitHub, your *public* key must be registered as a **Signing Key** (this is separate from any authentication key, even if it's the same key). `postCreate.sh` does this for you automatically via `gh ssh-key add --type signing` — **you never paste anything into github.com**. Authenticate `gh` once and grant the one extra scope it needs to register a signing key:

  ```bash
  gh auth login                                            # once; persists in the shared gh volume
  gh auth refresh -h github.com -s admin:ssh_signing_key   # one time, grants the signing-key scope
  bash .devcontainer/postCreate.sh                          # registers the key now
  ```

  **Won't forget the step:** if registration is still pending (no `gh` auth, or missing scope), `postCreate.sh` writes a hint that **every new terminal prints on startup** until it's done — so the exact commands stay in front of you. It self-clears the moment the key is registered.

  After registration the key is on your GitHub account (and the public side is in the shared `~/.ssh` volume), so every container and future rebuild signs with it and your commits are Verified.

### Persisted & shared across containers

| Path | Volume | Scope |
|---|---|---|
| `~/.claude` | `claude-code-config-${devcontainerId}` | **per project** (the `${devcontainerId}` suffix) |
| `~/.config/gh` | `shared-gh-config` | **shared** by every container mounting this name |
| `~/.ssh` | `shared-ssh` | **shared** by every container mounting this name |

To share the same credentials with **another repo's** dev container, declare the same fixed-name volumes (`shared-gh-config`, `shared-ssh`) in that repo's `devcontainer.json`. To go back to per-project isolation, add a `-${devcontainerId}` suffix to the volume name. (The `~/.claude` volume is per-project on purpose; give it a fixed name too if you want Claude's auth shared.)

> Alternative: instead of named volumes you can bind-mount the host's real `~/.config/gh` and `~/.ssh` (`source=${localEnv:HOME}/.ssh,...,type=bind`) for a single host+container source of truth. That's more convenient but exposes your real host SSH **private keys** to this convenience-first container — see Security.

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
- The persisted `gh` token and SSH state live in shared Docker volumes, and the forwarded SSH agent is reachable from inside the container — so Claude can push and sign commits **as you**, unprompted. That's the cost of convenience-first persistence.

**Only use this with trusted code, and monitor what Claude does.** SSH agent forwarding and named credential volumes keep your private key material on the host (only the agent socket / a container-local volume is exposed). **Do not bind-mount your host's real `~/.ssh` private keys or cloud credential files** into this container unless you accept that trusted-only code can read them.
