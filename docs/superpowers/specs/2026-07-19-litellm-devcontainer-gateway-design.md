# LiteLLM gateway in the dev container — design

**Date:** 2026-07-19
**Status:** approved, implemented on `main`

## Goal

Give the dev container an always-on, OpenAI-compatible LLM gateway that exposes
**per-model pools** and load-balances the routes within each model **by remaining
rate-limit headroom**. Clients inside the container (primarily **OpenCode** +
**oh-my-openagent/omo**, which pin each agent to a specific `provider/model`)
point at a single URL and a single gateway key and call `litellm/<model>`; LiteLLM
selects the route (provider/key) for that model with the most headroom, respects
each provider's declared `rpm`, and cools down any that 429. Real provider keys
never leave the container and are never committed.

Derived from the ai-engineering **Kubernetes** LiteLLM gateway (the Helm
`proxy_config`), adapted for a single local instance **and re-grouped from
capability tiers to per-model pools** (omo routes by concrete model id, not by an
abstract tier).

## Adaptation from the K8s gateway

| Prod (K8s, 2–6 HPA replicas) | Dev container (1 instance) |
|---|---|
| `usage-based-routing-v2` + per-entry `rpm` | **kept** — identical |
| `redis_host` (router counters) + Sentinel `cache_params` | **dropped** — one replica, so in-memory rpm/tpm counters are authoritative |
| CNPG Postgres + `migrationJob` + salt | **dropped** — gateway is stateless |
| capability-tier pools (`fast`/`reason`/…) + `*-net` fallbacks | **re-grouped** → per-model pools; cross-model fallback moves to omo |
| provider keys via `environmentSecrets` | → gitignored `.devcontainer/litellm/litellm.env` |

## Load-balancing design

- **One `model_name` per real model id** (17 total: `deepseek-v4-pro`,
  `deepseek-v4-flash`, `glm-5.2`, `minimax-m3`, `mimo-v2.5`, `kimi-k2.7-code`,
  `kimi-k2.6`, `qwen3.7-max`, `qwen3.7-plus`, `hy3`, `gemini-2.5-flash`,
  `llama-3.3-70b`, `deepseek-v3.2`, `deepseek-r1-distill-qwen-32b`,
  `qwen2.5-coder-32b`, `nemotron-3-ultra`, `north-mini-code`). Clients call
  `litellm/<model_name>`.
- Under each name is a **pool of every route serving that exact model** (flat-rate
  OpenCode Go A/B, free OpenCode Zen, free NVIDIA NIM, paid DeepSeek API,
  OpenRouter…). `routing_strategy: usage-based-routing-v2` routes to the route
  with the most `rpm`/`tpm` **headroom**, so one model never hits a single
  provider's limit.
- **`rpm:` on a route is load-bearing** — it declares that provider's documented
  free limit. Flat-rate OpenCode Go routes carry no `rpm` (dollar-limited) and
  rely on the reactive backstop.
- **Reactive backstop:** `num_retries: 2` retries within the model pool;
  `allowed_fails: 2` + `cooldown_time: 60` bench a 429'd route for 60s.
- **No cross-model `fallbacks` in LiteLLM.** omo owns cross-model fallback via
  each agent's `fallback_models`; the gateway only balances routes *within* a
  model. `.devcontainer/opencode/opencode.json` declares the same 17 model ids
  under the `litellm` provider (kept in sync with `config.yaml`).

## Non-goals (YAGNI)

- No Redis (single instance), no Postgres / virtual keys / budgets / admin UI
  (stateless), no Aspire wiring (dev-container lifecycle, not `aspire run`).
- `not-yet-registered` providers (groq/cerebras/cohere/mistral) stay commented
  in `config.yaml` until their keys + a probe land.

## Lifecycle & run mechanism

- Launched by `.devcontainer/start-litellm.sh` from a **`postStartCommand`** in
  `devcontainer.json`, so it comes up on **every** container start (not just
  first build). `postCreateCommand` remains the one-time `postCreate.sh`.
- The service is defined in `.devcontainer/litellm/compose.yaml` (docker
  **compose**): image `ghcr.io/berriai/litellm:main-stable`, `4000:4000`,
  `restart: unless-stopped`, `env_file: ./litellm.env`, config bind-mounted
  read-only at `/app/config.yaml`, python-based container healthcheck. The
  launcher is a thin wrapper: `docker compose up -d`.
- **Idempotent:** `up -d` reconciles — it re-attaches to the running container and
  only recreates when the compose definition changes (no needless blip on a plain
  restart). To apply a `config.yaml` edit to a running gateway:
  `docker compose ... up -d --force-recreate`.
- The launcher adds the two things compose can't: skip cleanly when the key file
  is absent, and wait (bounded, best-effort) on `GET /health/liveliness`. Neither
  a missing key file nor a failed launch fails container start (mirrors the
  tolerant style of `postCreate.sh`).
- Port `4000` added to `forwardPorts` + `portsAttributes` ("LiteLLM gateway").

## Files

| File | Committed? | Purpose |
|---|---|---|
| `.devcontainer/litellm/compose.yaml` | yes | docker-compose service definition (image, ports, env_file, mount, healthcheck) |
| `.devcontainer/litellm/config.yaml` | yes | four pools + `router_settings` + `litellm_settings` + `general_settings` |
| `.devcontainer/litellm/litellm.env.example` | yes | documents `LITELLM_MASTER_KEY` + all provider keys |
| `.devcontainer/litellm/litellm.env` | **no (gitignored)** | real keys |
| `.devcontainer/start-litellm.sh` | yes | thin compose-up wrapper (key-file guard + health wait) |
| `.devcontainer/codex/config.toml` | yes | Codex config template → gateway (seeded to `~/.codex/`) |
| `.devcontainer/opencode/opencode.json` | yes | OpenCode config template → gateway; registers `oh-my-openagent` in `plugin` (seeded to `~/.config/opencode/`) |
| `.devcontainer/opencode/oh-my-openagent.json` | yes | omo agent→`litellm/<model>` config template (seeded to `~/.config/opencode/`) |
| `.devcontainer/opencode/opencode-mem.jsonc` | yes | opencode-mem config (auto-capture via gateway, persisted storage, :4747 UI) |

Config changes: `devcontainer.json` (postStartCommand, port `4000`, `opencode` +
`codex` features, **`opencode-data` named-volume mount**, `NODE_OPTIONS` heap
bump), `Dockerfile` (**install `tmux`** for omo; pre-create
`~/.local/share/opencode` vscode-owned so that volume is writable), `.gitignore`
(ignore `litellm/litellm.env`), `postCreate.sh` (seed agent configs + export
`LITELLM_MASTER_KEY`), `.devcontainer/README.md`.

## Agent CLIs (OpenCode + Codex)

- Installed via devcontainer **features** (not Dockerfile — the base image has no
  node/bun; those come from features that layer on afterward): `opencode` →
  `ghcr.io/devcontainers-extra/features/opencode:1`; `codex` →
  `ghcr.io/dirien/devcontainer-feature-codex/codex:0` (installs `@openai/codex`).
- **Pre-wired to the gateway.** `postCreate.sh` seeds each CLI's config from the
  committed templates and appends a `~/.bashrc` line that exports
  `LITELLM_MASTER_KEY` (read live from `litellm/litellm.env`) so both authenticate
  without an interactive login. Codex uses `model_provider`/`env_key` +
  `wire_api = "chat"`; OpenCode uses an `@ai-sdk/openai-compatible` provider with
  `apiKey: "{env:LITELLM_MASTER_KEY}"`. Both default to `deepseek-v4-pro`.
- Codex's inner sandbox is disabled (`approval_policy = "never"`,
  `sandbox_mode = "danger-full-access"`) on the same convenience-first premise as
  the `claude --dangerously-skip-permissions` alias — the container is the
  security boundary. Documented in README "Security".

### oh-my-openagent (omo) — auto-installed, zero-touch

- **Plugin auto-installs:** `opencode.json` lists `oh-my-openagent` in its
  `plugin` array; OpenCode installs it via Bun on the first `opencode` launch (no
  interactive `bunx oh-my-openagent install` TUI). First launch needs network.
- **Other OpenCode plugins** in the same `plugin` array (auto-install on launch):
  `cc-safety-net` (npm `cc-safety-net`, a PreToolUse hook blocking destructive
  commands), `opencode-mem` (npm, local SQLite+vector memory; config **seeded** at
  `~/.config/opencode/opencode-mem.jsonc` — auto-capture via the litellm gateway
  `gemini-2.5-flash`, storage under the persisted `~/.local/share/opencode`
  volume, web UI on forwarded port 4747), and
  `superpowers` (obra's skills framework, git-backed:
  `superpowers@git+https://github.com/obra/superpowers.git`, auto-registers its
  skills dir). cc-safety-net's README suggests a CLI install, but it's a proper
  npm `@opencode-ai/plugin` package so the declarative `plugin`-array form is used.
- **Split routing** (aligned to omo's agent-model-matching guide): `postCreate.sh`
  seeds `~/.config/opencode/oh-my-openagent.json`. Communicators (sisyphus,
  prometheus, metis, atlas, sisyphus-junior) → **Kimi** (guide's top
  Claude-alternative; deepseek/minimax/qwen discouraged for these roles); utility
  (explore, librarian) → **Qwen**; vision (multimodal-looker) → `gemini-2.5-flash`.
  All 8 **categories** have a primary + `fallback_models` chain too
  (CategoryConfigSchema supports it): `deep`/`ultrabrain` primary on the GPT sub →
  gateway fallbacks; visual/artistry on Gemini; the rest Kimi/gateway. The GPT-family agents (`hephaestus`, `oracle`, `momus`) are primary on
  OpenCode's **native OpenAI/ChatGPT provider** (the user's ChatGPT subscription
  via `opencode auth login`), satisfying guards like `no-hephaestus-non-gpt`.
- **Per-agent `fallback_models` chains** (from the guide's per-agent fallback
  chains, adapted to available providers) on all 11 agents: GPT agents fall back to
  gateway models (DeepSeek for autonomous coding, Gemini/GLM for advisory); gateway
  agents fall back Kimi → GPT sub → other gateway models. Keeps an agent working
  when its primary is cooled/unavailable.
- **Team mode enabled** (`team_mode.enabled`, 12 `team_*` tools, opt-in per use;
  `tmux_visualization` on; `base_dir` under the persisted opencode volume).
- **Superpowers TDD wired via `prompt_append`.** The superpowers plugin registers
  skills into OpenCode's `skill` tool + injects awareness into every conversation;
  a committed directive (`.devcontainer/opencode/superpowers-skills.md`, seeded to
  `~/.config/opencode/`) is `prompt_append`-ed onto the implementer agents
  (sisyphus, hephaestus, sisyphus-junior) + the `deep` category so they invoke
  `test-driven-development` (failing test first) before implementation. Plain
  plugin install makes skills *available*; the prompt_append makes omo *use* them.
- Chosen: **OpenCode Ultimate edition only** (Codex Light `npx lazycodex-ai
  install` not run — can be added later). GPT agents need a one-time
  `opencode auth login`.
- **tmux** is installed in the image (omo's `interactive_bash` tool + Team
  Mode/background-subagent panes shell out to it). omo's tmux integration is
  enabled in `oh-my-openagent.json` (`"tmux": { "enabled": true }`); it only
  activates when OpenCode runs inside a tmux session, else no-ops harmlessly.
- **Auth persists across rebuilds.** `~/.local/share/opencode` (holding
  `auth.json`) is mounted on the per-project named volume
  `opencode-data-${devcontainerId}` (mirroring the Claude Code volume), with the
  Dockerfile pre-creating it vscode-owned so the volume is writable — else auth
  silently fails to persist (the same root-owned-volume trap the `~/.claude`
  pre-create already guards). So the ChatGPT-subscription login is one-time.

### Provider keys (initial local set)

`OPENCODE_GO_KEY`, `OPENCODE_GO_KEY_2` (flat-rate subs, verified primary of every
pool), `DEEPSEEK_API_KEY` (paid net + co-primary), `NVIDIA_API_KEY` (free 40 RPM),
and free tier `GEMINI_API_KEY` / `SAMBANOVA_API_KEY` /
`CLOUDFLARE_API_KEY` + `CLOUDFLARE_ACCOUNT_ID` / `OPENROUTER_API_KEY`. Plus
`LITELLM_MASTER_KEY` for client→gateway auth.

## Auth model

- Clients call the gateway with `LITELLM_MASTER_KEY` as their API key.
- Real provider keys only exist in the container env (from the gitignored
  `litellm/litellm.env`) and are used solely by LiteLLM for upstream calls.

## Consuming from OpenCode / omo

OpenAI-compatible provider: base URL `http://localhost:4000` (`/v1`), API key =
`LITELLM_MASTER_KEY`, model = `litellm/<model_name>` (any of the 17 per-model ids).
omo pins each agent to a specific model and chains across models via its own
`fallback_models`.

## Verification (performed)

- `docker compose up -d` (via the launcher) starts the container;
  `/health/liveliness` healthy and the compose healthcheck reports `healthy`.
- `GET /v1/models` (with master key) lists exactly the **17 per-model ids** and no
  leftover tier names — YAML anchors resolved, all routes registered under their
  model, no config/parse errors, `usage-based-routing-v2` accepted (an invalid
  strategy would fail startup validation). `opencode.json` declares the same 17.
- Auth enforced: request without the master key → `HTTP 401`.
- Idempotent: a second `up -d` reports the container `Running` (no recreate).
- `devcontainer.json` parses with both agent features + `postStartCommand` + port
  `4000`; both feature OCI artifacts resolve on ghcr.
- `opencode.json` parses as JSON; `codex/config.toml` parses as TOML with the
  enums (`approval_policy`, `sandbox_mode`) correctly at top level, not nested in
  the provider table.
- **Not yet exercised (needs a container rebuild + real keys):** the features
  actually installing, `postCreate.sh` seeding the agent configs, and an
  end-to-end `codex`/`opencode` call through the gateway to a live provider.
- Model ids are ported verbatim from prod and should be re-verified with a probe
  before relying on any single member (ids drift between providers/dates).
