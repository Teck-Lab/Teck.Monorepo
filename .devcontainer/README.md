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
| tmux | apt | Dockerfile (`apt-get install tmux`) — one observable session per Orca worker |

`postCreate.sh` runs `dotnet restore`, `bun install --frozen-lockfile`, creates an HTTPS dev cert, installs the Claude Code plugins + HUD (see below), and installs a `claude` shell alias (see Security below). The `bun install` also fires the root `prepare` script, which installs the Husky git hooks (`core.hooksPath` → `.husky/_`): pre-commit runs Biome on staged files plus a staged Gitleaks scan, pre-push runs the full local CI mirror (`tools/security-scan.sh`) — so the security gates are active on every fresh container with no manual step.

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

**Per-model pools** (structured for [full OMO](https://omo.dev/docs) and the
isolated OMO Slim evaluation profile, which pin each agent/category to a
specific `provider/model`). Each `model_name` is a REAL
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
fallback (agent's model down → try another) is owned by the active OMO harness,
not LiteLLM — the gateway only balances routes *within* a
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
  `http://localhost:4000/v1` exposing all per-model pools; default model
  `openai/gpt-5.6-terra`. Just run `opencode`.
- **Codex** — `~/.codex/config.toml` (from `.devcontainer/codex/`) points at the
  same gateway (`base_url` + `env_key = LITELLM_MASTER_KEY`, `wire_api = "chat"`),
  default model `deepseek-v4-pro`. Just run `codex`.

**OpenCode plugins auto-install** on first launch — `opencode.json`'s `plugin`
array is fetched via Bun (needs network once):

- `oh-my-openagent@4.19.4` — full OMO, the default worker harness.
- `cc-safety-net` — PreToolUse hook that blocks destructive commands (`rm -rf`,
  `git reset --hard`, …) before an agent runs them. Works out of the box.
- `opencode-mem` — local agent memory (SQLite + on-device vector search, no
  external service). **Config seeded** (`.devcontainer/opencode/opencode-mem.jsonc`):
  auto-capture on via the litellm gateway (`deepseek-v4-flash` — a 5-route pool,
  since capture fires on every prompt; **not** Gemini, whose free tier is 20
  req/**day** and single-route); memories stored
  under the **persisted** `~/.local/share/opencode` volume so they survive
  rebuilds; memory web UI at `http://localhost:4747`.
**Full OMO is auto-installed and is the default Orca worker harness.** The
committed OpenCode config registers the pinned plugin, and `postCreate.sh`
seeds the reproducible user model policy into `~/.omo/omo.jsonc`. The project
contract in `.omo/omo.jsonc` adds worktree, Git, GitHub, and Orca lifecycle
boundaries.

- `planned` and `quick`: GPT-backed Prometheus plans; `/start-work` transfers
  execution to GPT-backed Atlas.
- `autonomous` and `spike`: GPT-5.6 Sol Hephaestus is the primary worker.
- `quick`, Explore, and Librarian route through the LiteLLM
  `deepseek-v4-flash` pool, which spans both OpenCode Go subscriptions and
  configured free/paid routes. OMO runtime fallback and its built-in agent
  chains ultimately reach the GPT-backed OpenCode default if those pools fail.
- `deep`, `ultrabrain`, Oracle, and difficult review retain GPT models.
- Visual Engineering and Multimodal Looker use GPT-5.6 Sol; Gemini access is
  not required.
- OMO permits eight background tasks, capped at five direct OpenAI tasks and
  ten LiteLLM tasks. LiteLLM then balances those requests across OpenCode Go,
  OpenRouter, and configured free routes.
- Hashline editing is enabled for stable, low-conflict edits during parallel
  coding work.
- Nested agents cannot commit, push, merge, create worktrees, mutate GitHub, or
  send Orca lifecycle messages. The primary worker validates, signs, commits,
  and sends `worker_done`.

**Slim is an isolated A/B baseline, not a second active orchestrator.** Its
pinned plugin and previous model policy live under
`~/.config/opencode/profiles/slim`. Select it only through
`tools/orca-feature dispatch-info --harness slim`; full OMO and Slim are never
loaded into the same OpenCode process.

**tmux sessions are created by `teck-omo-worker`.** Every Orca sub-issue gets a
foreground-attached `teck-<parent>-<issue>-<slug>` session, a unique OpenCode
port, and the assigned worktree as its fixed working directory. Full OMO owns
nested panes inside the session; Orca owns lifecycle state. Pane exit or idle
state never means completion.

Edit `.devcontainer/opencode/omo.jsonc` for user-level model routing and
`.omo/omo.jsonc` for repository worker policy. Both are committed and applied
on the next container creation. First `opencode` launch needs network to fetch
the pinned plugin; OpenAI-backed agents need the one-time `opencode auth login`.

**Auth persists across rebuilds.** OpenCode's data dir `~/.local/share/opencode`
(holding `auth.json` and plugin state) is mounted on a per-project named
volume (`opencode-data-${devcontainerId}`, alongside the Claude Code one). So
`opencode auth login` (your ChatGPT subscription) is a **one-time** step that
survives container rebuilds. GitHub MCP authenticates independently as a GitHub
App using the read-only local bundle described below. (The gateway master key isn't stored here; it's
loaded live from `litellm/litellm.env` per shell. Codex authenticates to the
gateway via env, so it has no stored credential to persist.)

## GitHub MCP and feature orchestration

Codex and OpenCode launch the pinned official GitHub MCP server locally over
stdio. It authenticates as a repository-scoped GitHub App from files mounted
read-only at `/run/secrets/teck-github`; see `github-app/README.md`. The exposed
tool allowlist supports issues/sub-issues, PR creation/update, repository reads,
and CI inspection. It excludes remote file commits, branch creation, workflow
dispatch, PR review submission, and merge.

For one-container feature development, use the repo-owned `teck-feature-flow`
skill and `tools/orca-feature`. GitHub sub-issues map to ordinary internal Git
worktrees and Orca Tasks/Dispatches, while all workers stay inside the parent
feature container. The coordinator integrates the signed worker commits into
one parent branch and opens one final PR for human approval.

## What survives a rebuild (auth & state)

| Path | Persisted by | Holds |
|---|---|---|
| `/home/vscode/.claude-config` | volume `claude-code-config-*` | **all** Claude Code state — `.credentials.json`, `.claude.json`, settings, plugins, transcripts |
| `/home/vscode/.local/share/opencode` | volume `opencode-data-*` | OpenCode `auth.json`, `mcp-auth.json`, memory DB |
| `/home/vscode/.codex` | volume `codex-config-*` | Codex `auth.json` if you ever `codex login` |
| `/run/secrets/teck-github` | read-only workspace bind | GitHub App PEM/config and automation signing-key export |
| `/home/vscode/.gnupg` | imported/copied from read-only mounts | Active GPG signing key (see below) |
| `~/.config/opencode` | **not** persisted — by design | re-seeded from `.devcontainer/opencode/` every build |

**`CLAUDE_CONFIG_DIR` is load-bearing.** It's set to `/home/vscode/.claude-config` in
`devcontainer.json` and relocates Claude Code's *entire* state tree onto one volume.
This exists because mounting `~/.claude` alone was **not enough**: Claude Code also keeps
`~/.claude.json` — which holds the OAuth *session* — as a **sibling** of `~/.claude`, not
inside it. That file sat on the container's ephemeral filesystem, so every rebuild wiped
it and forced a fresh sign-in even though the token in `.credentials.json` had persisted
perfectly. One env var + one mount now covers both.

It must be an **absolute path**. `devcontainer.json`'s `containerEnv` is not shell-processed,
so a leading `~` is taken literally and you get a directory named `~` in your workspace.

## Commit signing (GPG)

Agent commits use the dedicated automation key generated on WSL2 by
`scripts/github-automation/init-local-secrets.sh`. Its private export remains
gitignored on WSL2, is mounted **read-only** under `/run/secrets/teck-github`,
and is imported into writable `~/.gnupg` by `postCreate.sh`. Until that bundle
is initialized, the existing read-only host keyring at `~/.gnupg-host` remains
the fallback so the container is still usable.

**Why not VS Code's built-in GPG forwarding:** it worked, until it didn't. Forwarding is an
implicit, undeclared socket bind to the host agent that drops on window reloads, container
restarts, host-agent exit, and WSL2 sleep/resume. Worse, when it drops a **local keyless
agent answers on the same socket path**, so `gpg` reports `No secret key` rather than a
connection error — it looks like your key vanished. There is nothing to disconnect now.

**Why copy instead of using the mount directly:** gpg requires `0700` on its home directory
and writes sockets/`trustdb`/`random_seed` at runtime, so a read-only mount can't serve as
`GNUPGHOME`. Mounting read-**write** would let this container corrupt the host keyring, so
we don't. The writable keyring is ephemeral and refreshed at container setup;
the WSL2 automation bundle remains the source of truth.

`postCreate.sh` verifies the automation key by creating a detached signature,
so a broken setup surfaces at startup instead of halfway through an agent run.
The development-only automation key is generated without a passphrase for
unattended workers; its filesystem mount and GitHub repository scope are the
security boundaries.

> A key mounted here is usable by anything running in the container, agents included. That
> is the accepted trade for unattended signing.

## Running things

- Build/test everything: `bun run build`, `bun run test`, or `nx affected -t build test lint typecheck`.
- Integration tests (Testcontainers) and Aspire work because Docker runs **inside** the container (Docker-in-Docker). The first integration-test run pulls Postgres/RabbitMQ/Redis/Keycloak images into the nested daemon and is slow; later runs are fast because the image store is persisted across rebuilds.
- Forwarded ports: **18888** Aspire dashboard, **4000** LiteLLM gateway, **4747** opencode-mem UI, **3000** Next.js dev, **8080** service host, **8081** Metro / Expo web, **19000** Expo Go (LAN), **19006** Expo web (legacy).

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
