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
| OpenCode | latest | `opencode` feature (devcontainers-extra) |
| Codex CLI | latest | `dirien/codex` feature (installs `@openai/codex`) |
| tmux | apt | Dockerfile (`apt-get install tmux`) — required by omo |

`postCreate.sh` runs `dotnet restore`, `bun install --frozen-lockfile`, creates an HTTPS dev cert, installs the Claude Code plugins + HUD (see below), and installs a `claude` shell alias (see Security below).

## Claude Code plugins & HUD statusline

The repo's checked-in `.claude/settings.json` is the team source of truth: it declares the enabled plugins (`superpowers`, `microsoft-docs`, `typescript-lsp`, `security-guidance`, `playwright`, `frontend-design`, `csharp-lsp`, `github` from the official marketplace, plus `claude-hud`), the extra `claude-hud` marketplace, the `dark` theme, and the claude-hud **statusLine**. The statusLine resolves `bun` from `PATH` (falling back to `~/.bun/bin/bun`) so it works for any user.

Because enabled plugins normally only install behind an interactive trust prompt, `postCreate.sh` also installs them **explicitly and headlessly** (`claude plugin marketplace add` + `claude plugin install`) into the mounted `~/.claude` volume, and seeds the claude-hud display config from `.devcontainer/claude-hud-config.json`. Edit that JSON to change which HUD elements show. Everything lands in the persistent volume, so it survives rebuilds.

## LiteLLM gateway (local LLM load balancer)

An always-on, OpenAI-compatible gateway that pools many LLM providers and
load-balances across them by remaining rate-limit headroom, reachable at
**`http://localhost:4000`** (port 4000, forwarded). Clients — primarily
**OpenCode** — point at one URL and one gateway key; LiteLLM picks a pool member
per request, respects each provider's `rpm`, cools down any that 429, and falls
back to a paid net so a request never hard-fails. It runs stateless (no database)
from `.devcontainer/litellm/config.yaml`.

**Per-model pools** (structured for [oh-my-openagent](https://github.com/code-yeongyu/oh-my-openagent),
which pins each agent to a specific `provider/model`). Each `model_name` is a REAL
model id — clients call it as `litellm/<model_name>` — and under each name sits a
pool of every route that serves that exact model (flat-rate OpenCode Go, free
OpenCode Zen, free NVIDIA NIM, paid DeepSeek API, OpenRouter…). The 17 models:

```
deepseek-v4-pro   deepseek-v4-flash   glm-5.2   minimax-m3   mimo-v2.5
kimi-k2.7-code    kimi-k2.6           qwen3.7-max   qwen3.7-plus   hy3
gemini-2.5-flash  llama-3.3-70b       deepseek-v3.2
deepseek-r1-distill-qwen-32b   qwen2.5-coder-32b   nemotron-3-ultra   north-mini-code
```

`routing_strategy: usage-based-routing-v2` sends each request to the route with
the most `rpm`/`tpm` headroom, so one model never hits a single provider's limit;
a route that 429s is cooled and traffic shifts to the rest. **Cross-model**
fallback (agent's model down → try another) is owned by omo's per-agent
`fallback_models`, not LiteLLM — the gateway only balances routes *within* a
model. `.devcontainer/start-litellm.sh` brings it up with **docker compose**
(service defined in `.devcontainer/litellm/compose.yaml`) via docker-in-docker
from the `postStartCommand`, so it comes up on **every** container start. `up -d`
is idempotent — a restart re-attaches to the running container instead of
recreating it.

**Provide keys (one-time):**

```bash
cp .devcontainer/litellm/litellm.env.example .devcontainer/litellm/litellm.env
# edit it: LITELLM_MASTER_KEY + the provider keys you have
bash .devcontainer/start-litellm.sh   # or just restart the container
```

`.devcontainer/litellm/litellm.env` is **gitignored** — real keys never get
committed. Without the file, startup is skipped with a hint (the container still
comes up). A missing/blank key for one provider just cools that member down; the
gateway still serves from the rest of the pool.

**Point OpenCode (or any OpenAI-compatible client) at it:**

- Base URL: `http://localhost:4000` (uses `/v1` endpoints)
- API key: your `LITELLM_MASTER_KEY`
- Model: any of the 17 model ids above (e.g. `deepseek-v4-pro`, `glm-5.2`, `kimi-k2.7-code`)

**Apply `config.yaml` edits** to an already-running gateway (compose won't detect
a mounted-file change on its own):

```bash
docker compose -f .devcontainer/litellm/compose.yaml up -d --force-recreate
```

**Add a route / model:** add the key to `litellm/litellm.env`, then add a route
under the relevant `model_name` (or a whole new `model_name`) in `config.yaml`
with `rpm:` = its documented free limit (load-bearing — without it the router
won't respect the cap). If you add a new `model_name`, also declare it under the
`litellm` provider's `models` in `.devcontainer/opencode/opencode.json` (keep the
two in sync). Verify the exact model id with a probe first; ids drift between
providers/dates.

## Agent CLIs (OpenCode + Codex) → the gateway

`opencode` and `codex` are installed via devcontainer features and **pre-wired to
the LiteLLM gateway** — `postCreate.sh` seeds their configs and exports
`LITELLM_MASTER_KEY` into the shell (loaded from `litellm/litellm.env`), so no
interactive login is needed once the gateway has keys.

- **OpenCode** — `~/.config/opencode/opencode.json` (from `.devcontainer/opencode/`)
  registers a `litellm` provider (`@ai-sdk/openai-compatible`) at
  `http://localhost:4000/v1` exposing all 17 per-model ids; default model
  `litellm/deepseek-v4-pro`. Just run `opencode`.
- **Codex** — `~/.codex/config.toml` (from `.devcontainer/codex/`) points at the
  same gateway (`base_url` + `env_key = LITELLM_MASTER_KEY`, `wire_api = "chat"`),
  default model `deepseek-v4-pro`. Just run `codex`.

**omo (oh-my-openagent) is auto-installed & pre-wired — zero manual steps:**
`opencode.json` lists `oh-my-openagent` in its `plugin` array, so OpenCode
installs it via Bun on the first `opencode` launch (no `bunx oh-my-openagent
install` TUI). `postCreate.sh` also seeds `~/.config/opencode/oh-my-openagent.json`
(`.devcontainer/opencode/oh-my-openagent.json`), which maps omo's agents to your
`litellm/<model>` pools — the gateway load-balances routes within each model so you
don't hit provider limits.

- 8 non-GPT agents (sisyphus, prometheus, metis, atlas, sisyphus-junior, explore,
  librarian, multimodal-looker) map to your gateway models; `multimodal-looker` →
  `gemini-2.5-flash` (only vision route).
- The GPT-family agents (`hephaestus`, `oracle`, `momus`) are **not** overridden —
  they keep omo's built-in OpenAI defaults and run on OpenCode's **native OpenAI/
  ChatGPT provider**, connected directly (`opencode auth login` → OpenAI, one
  time), **not** the gateway. This also satisfies omo's model-family guards
  (`no-hephaestus-non-gpt`).
- **tmux** is installed (Dockerfile) and omo's tmux integration is enabled
  (`"tmux": { "enabled": true }`), so background subagents spawn into panes. It
  only activates when you launch OpenCode **inside** a tmux session (`tmux`, then
  `opencode`); outside tmux it's a harmless no-op and omo's `interactive_bash`
  tool still works. The layout is tuned for a **tall/narrow terminal** (VS Code
  panel docked right): `main-horizontal` stacks the main pane on top and
  subagents below. Docking the terminal at the bottom (wide/short)? switch
  `layout` back to `main-vertical` and raise `main_pane_min_width` to ~120.

Edit the committed templates under `.devcontainer/{opencode,codex}/` to change
models/agents — they re-seed on every rebuild (the in-container copies are
ephemeral). First `opencode` launch needs network to fetch the plugin; the GPT
agents need the one-time `opencode auth login`.

**Auth persists across rebuilds.** OpenCode's data dir `~/.local/share/opencode`
(holding `auth.json` **and** `mcp-auth.json`) is mounted on a per-project named
volume (`opencode-data-${devcontainerId}`, alongside the Claude Code one). So
`opencode auth login` (your ChatGPT subscription) — and omo's bundled-MCP auth —
are **one-time** steps that survive container rebuilds. (The gateway master key isn't stored here; it's
loaded live from `litellm/litellm.env` per shell. Codex authenticates to the
gateway via env, so it has no stored credential to persist.)

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
- `codex` is seeded with `approval_policy = "never"` and `sandbox_mode = "danger-full-access"` (its inner sandbox off) on the same premise — the container is the boundary. Edit `.devcontainer/codex/config.toml` to tighten it (`workspace-write`, `on-request`).
- Claude/OpenCode/Codex can modify any file in the bind-mounted workspace — **which is your real host repository** — and reach anything the container's network allows (there is no egress firewall).

**Only use this with trusted code, and monitor what Claude does.** Avoid mounting host secrets (`~/.ssh`, cloud credential files) into the container.
