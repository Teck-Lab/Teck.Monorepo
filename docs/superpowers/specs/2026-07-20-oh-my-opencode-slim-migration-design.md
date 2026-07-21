# oh-my-opencode-slim Migration — Design

**Date:** 2026-07-20
**Status:** Approved for planning
**Supersedes (in part):** `2026-07-19-litellm-devcontainer-gateway-design.md` (the gateway stays; only its omo consumer changes)

## Summary

Replace the full `oh-my-openagent` (omo) plugin in the dev container with
[`oh-my-opencode-slim`](https://github.com/alvinunreal/oh-my-opencode-slim), configured from the
project's [author's preset](https://github.com/alvinunreal/oh-my-opencode-slim/blob/master/docs/authors-preset.md),
with [tmux multiplexer integration](https://github.com/alvinunreal/oh-my-opencode-slim/blob/master/docs/multiplexer-integration.md)
so subagents are visible in live panes.

Model routing becomes **hybrid**: a newly-authenticated native `openai/` provider (GPT-5 Pro
subscription) serves the reasoning-heavy agents, while the existing LiteLLM gateway on `:4000`
continues to serve the cost-sensitive ones. The gateway, its pools, and its routing policies are
unchanged.

Delivered in four phases; phase 1 is independently verifiable and the rest add MCP capability.

## Motivation

The current setup pins every omo agent to `litellm/*`. It works, but:

- omo is the heavier plugin; slim's seven-agent model (orchestrator, explorer, oracle, council,
  librarian, designer, fixer) is the actively-developed line and ships background orchestration,
  council consensus, preset switching, and multiplexer panes.
- No agent currently has access to a frontier reasoning model. A GPT-5 Pro subscription is now
  available and should back the `oracle` seat.
- Subagent activity is invisible today. tmux panes make delegated work observable and debuggable.

## Scope

**In scope:** plugin swap, slim config template, model routing, tmux integration, prompt-append
migration, MCP wiring (phases 2–4), dev-container docs.

**Out of scope:** the LiteLLM gateway config (`.devcontainer/litellm/`) except where a new model
route is genuinely needed; the `cc-safety-net`, `opencode-mem`, and `superpowers` plugins, which are
explicitly retained unchanged; anything in `src/`.

## Design

### 1. Plugin swap

| Action | Target |
|---|---|
| Remove | `oh-my-openagent` from the `plugin` array in `.devcontainer/opencode/opencode.json` |
| Remove | `.devcontainer/opencode/oh-my-openagent.json` (8.4 KB agent config) |
| Remove | the `oh-my-openagent.json` and `superpowers-skills.md` `cp` lines in `.devcontainer/postCreate.sh` |
| Add | `oh-my-opencode-slim` to the `plugin` array |
| Add | `.devcontainer/opencode/oh-my-opencode-slim.json` (committed template) |
| Add | `cp` line seeding that template to `~/.config/opencode/oh-my-opencode-slim.json` |

**Retained plugins:** `cc-safety-net`, `opencode-mem`, `superpowers@git+https://github.com/obra/superpowers.git`.
`opencode-mem` keeps its own config (`opencode-mem.jsonc`) and its `:4747` UI port.

**The committed template is the source of truth, not the installer.** Slim ships
`bunx oh-my-opencode-slim@latest install`, which generates a config and edits `opencode.json`. We do
not run it in `postCreate`: it is interactive by default, non-destructive (it refuses to overwrite an
existing config), and would fight the repo template on every rebuild. This matches the existing
convention — every other agent config in `.devcontainer/` is a committed template `cp`'d into place.
The plugin itself auto-installs via Bun on first `opencode` launch, exactly as omo did.

### 2. Model routing

Author's preset structure preserved; models substituted for what this environment actually has.

| Agent | Author's preset | This setup | Rationale |
|---|---|---|---|
| orchestrator | `openai/gpt-5.6-fast` | `openai/` fast tier | Runs on every turn; latency-sensitive |
| oracle | `openai/gpt-5.6-fast` (high) | **`openai/gpt-5-pro`** (high) | The deep-reasoning seat — where Pro pays for itself |
| librarian | `openai/gpt-5.3-codex-spark` (low) | `openai/` economy tier (low) | Research/retrieval, high volume |
| explorer | `openai/gpt-5.3-codex-spark` (low) | `openai/` economy tier (low) | Codebase recon, high volume |
| designer | `omniroute/antigravity/gemini-3-flash-agent` | `litellm/glm-5.2` | See deviation D1 |
| fixer | `gemini-3-flash-agent` (low) | `litellm/kimi-k2.7-code` (low) | See deviation D1 |
| council | — | `openai/gpt-5-pro` + `litellm/glm-5.2` + `litellm/deepseek-v4-pro` | Cross-vendor consensus is the point |
| `fast-generic` (custom) | `gpt-5.3-codex-spark` (low) | `litellm/deepseek-v4-flash` | Mechanical git/lint/test work |

`fast-generic` keeps the author's `prompt` and `orchestratorPrompt` **verbatim** — they encode a
useful safety boundary (no code edits, no destructive git history operations unless explicitly
requested).

#### Open item O1 — native OpenAI model IDs are unverified

There is no `auth.json` in the container and no OpenAI route in `litellm/config.yaml`; the native
`openai/` provider does not exist yet. The author's `gpt-5.6-terra` / `gpt-5.6-sol` / `gpt-5.6-luna` /
`gpt-5.3-codex-spark` IDs are from his own account and **must not be copied blind**.

Phase 1 therefore gates on a manual step:

```bash
opencode auth login        # interactive — the user runs this, not the agent
opencode models --refresh
```

The `openai/` cells above are **role intent, not literal strings**. Real IDs get pinned into the
template after that listing is available. If the subscription turns out not to expose a Pro-tier
model to OpenCode at all, the fallback is to route `oracle` at the strongest available `openai/`
reasoning tier and record the finding — the rest of the design is unaffected.

#### Deviation D1 — Gemini substitution (forced)

The author runs designer and fixer on Gemini. The only Gemini route in this environment is
`gemini-2.5-flash`, which `litellm/config.yaml` documents as capped at **20 requests per _day_**
(`GenerateRequestsPerDayPerProjectPerModel-FreeTier`), single-route, with no cross-model fallback —
"once the daily quota is spent every call 429s hard." Mirroring the author literally would leave two
of seven agents dead by mid-morning. Flat-rate OpenCode Go pool models are substituted instead.

#### Deviation D2 — companion disabled (forced)

The author's preset sets `companion.enabled: true`. The companion is a **desktop GUI application**;
this is a headless container with no display server. Set `companion.enabled: false`.

#### Fallbacks

Slim owns cross-model failover via its own `fallback` block (`fallback.enabled`, `timeoutMs`,
`maxRetries`, `retry_on_empty`). This replaces omo's per-agent `fallback_models`. LiteLLM continues
to balance *within* a model pool only — that division of responsibility is unchanged and should stay
documented in `litellm/config.yaml`, whose comments currently name omo and must be updated.

### 3. tmux multiplexer

Config block (current key — **not** the author's legacy `"tmux": {...}` alias, which the
configuration reference marks as a deprecated alias for exactly these three fields):

```jsonc
"multiplexer": {
  "type": "tmux",
  "layout": "main-vertical",
  "main_pane_size": 60
}
```

Three supporting changes:

1. **tmux stays installed.** It is already in the `Dockerfile`, added for omo's `interactive_bash`
   tool. Only the explanatory comment changes — it currently names omo.
2. **Background subagents export.** Slim's default orchestration requires
   `OPENCODE_EXPERIMENTAL_BACKGROUND_SUBAGENTS=true`. Appended to `~/.bashrc` by `postCreate.sh`,
   using the same idempotent `grep -qxF` guard the existing alias lines use.
3. **Port wrapper.** Panes attach via `opencode attach`, which needs a real TCP listener; OpenCode's
   default `--port 0` does not create one. The upstream doc ships a **zsh** `omos()` helper — this
   container runs **bash**, so a bash equivalent is written to `~/.bashrc`: honour an explicit
   `--port`, otherwise pick a free loopback port and pass it explicitly, exporting a matching
   `OPENCODE_PORT`.

Ports are container-local (multiplexer panes attach from inside), so no `forwardPorts` change is
needed.

### 4. Prompt migration

omo's `prompt_append` mechanism does not exist in slim. Its replacement is per-agent markdown:
`{agent}.md` replaces a prompt entirely, `{agent}_append.md` appends to it.

The current `superpowers-skills.md` directive (TDD via OpenCode's `skill` tool) moves to
**project-local, committed** files:

```
<repo>/.opencode/oh-my-opencode-slim/
  ├── orchestrator_append.md
  └── fixer_append.md
```

This is strictly better than the omo arrangement: the files are version-controlled, reviewable in
PRs, need no `postCreate` seeding, and survive container rebuilds automatically. The repo already
carries `.opencode/skills/`, so the directory is established.

Targeting the two implementer agents is deliberate — under omo the directive was appended broadly.
Explorer and librarian do not write code and do not need TDD instructions in their context window.

> **Trust note.** Project-local slim config and prompt files are loaded automatically when OpenCode
> opens the directory, and can alter agent behaviour and tool access. This is our own repository, so
> the boundary is acceptable — but it is a reason not to extend the pattern to untrusted checkouts.

### 5. MCP wiring (phases 2–4)

Slim's per-agent `mcps` arrays follow the author's routing intent: the orchestrator gets broad access
minus the research servers, which are pushed down to the librarian so research does not consume
orchestrator context.

The author's exclusion list is `["*", "!context7", "!gh_app", "!websearch"]`. Two of those names do
not exist here — `gh_app` is his GitHub App server and `websearch` is a server we are not adopting —
so the list is rewritten against servers this setup actually defines:

```jsonc
"mcps": ["*", "!context7", "!github"]
```

Every `mcps` entry in the template must name a server defined in `opencode.json`'s `mcp` block. Until
a phase lands its servers, the corresponding arrays stay `[]` — no dangling references at any point.

| MCP | Phase | Consumers | Infrastructure |
|---|---|---|---|
| `context7` | 2 | librarian | Remote hosted; none |
| `github` | 2 | librarian | `gh` CLI already installed and authenticated |
| `searxng` | 3 | oracle, librarian, fixer | Self-hosted container |
| `crawl4ai` | 3 | oracle, librarian, fixer | Self-hosted container (Chromium, ~2 GB+) |
| `codegraph` | 4 | explorer, designer, fixer, oracle | Indexing pass over the monorepo |

Phase 3 follows the established LiteLLM pattern exactly: `.devcontainer/mcp/compose.yaml` +
a start script + a `postStartCommand` entry, tolerant of failure so it never blocks container
startup.

**Phase 4 is explicitly conditional.** `codegraph`'s value depends on its language support for a
mixed .NET + TypeScript monorepo, which is unverified. Phase 4 begins with a support check; if C#
coverage is absent or poor, the phase is dropped and the `codegraph` entries are removed from the
`mcps` arrays rather than left dangling.

## Files affected

| File | Change |
|---|---|
| `.devcontainer/opencode/opencode.json` | swap plugin name; add `mcp` block (phases 2–4) |
| `.devcontainer/opencode/oh-my-openagent.json` | **delete** |
| `.devcontainer/opencode/superpowers-skills.md` | **delete** (content moves to `_append.md` files) |
| `.devcontainer/opencode/oh-my-opencode-slim.json` | **new** — preset, models, multiplexer, fallback |
| `.devcontainer/postCreate.sh` | seed lines; `BACKGROUND_SUBAGENTS` export; `omos` bash wrapper |
| `.devcontainer/Dockerfile` | rewrite the tmux rationale comment |
| `.devcontainer/litellm/config.yaml` | update comments naming omo as the fallback owner |
| `.devcontainer/README.md` | rewrite the omo section |
| `.opencode/oh-my-opencode-slim/orchestrator_append.md` | **new** |
| `.opencode/oh-my-opencode-slim/fixer_append.md` | **new** |
| `.devcontainer/mcp/compose.yaml` | **new**, phase 3 |
| `.devcontainer/start-mcp.sh` | **new**, phase 3 |
| `.devcontainer/devcontainer.json` | `postStartCommand` chain, phase 3 |

## Verification

Per phase, evidence required before the phase is called done:

**Phase 1**
1. `opencode auth login` && `opencode models --refresh` list a Pro-tier `openai/` model (resolves O1).
2. Container rebuilds clean; `postCreate.sh` reports no `WARN` lines for the new seed steps.
3. `~/.config/opencode/oh-my-opencode-slim.json` matches the committed template.
4. `tmux` → `opencode` (via `omos`) → `ping all agents` — **all seven agents respond**.
5. Delegating real work opens subagent panes in `main-vertical` at ~60% main pane.
6. A fixer task shows the TDD directive in effect (prompt append resolved).
7. `opencode-mem` UI still reachable on `:4747`; `cc-safety-net` and `superpowers` still load.

**Phase 2** — librarian resolves a library-docs query via `context7` and a repo query via `github`.

**Phase 3** — both containers healthy; `docker compose ps` clean; oracle completes a web-research
task; LiteLLM still healthy on `:4000` alongside them (resource contention check).

**Phase 4** — `codegraph` returns useful results for **both** a C# and a TypeScript symbol, or the
phase is dropped per the conditional above.

## Risks

| Risk | Mitigation |
|---|---|
| Pro subscription exposes no Pro-tier model to OpenCode (O1) | Phase 1 gates on the model listing; documented fallback to strongest available tier |
| Slim's seven agents behave differently from omo's set; muscle memory breaks | Phase 1 is a discrete, revertible commit; omo config recoverable from git history |
| Chromium + searxng + LiteLLM under docker-in-docker exhaust container resources | Phase 3 is last of the infra phases and independently revertible; verification includes a contention check |
| Auto-updating plugin drifts from the committed template | Slim stages changed bundled skills for review rather than overwriting; template remains authoritative for config |
| `--port` wrapper misbehaves in non-interactive shells | Wrapper is a bash *function*, only affects interactive `opencode` invocations; `opencode` itself stays on `PATH` unwrapped |

## Alternatives considered

- **Everything through LiteLLM.** Rejected: a ChatGPT Pro *subscription* cannot be proxied through
  LiteLLM (it needs an API key, which is separately billed). Would have forfeited the Pro seat.
- **Native `openai/` only, drop LiteLLM.** Rejected: abandons the flat-rate and free pools already
  built and tuned, and concentrates all load on one subscription.
- **Run slim's installer in `postCreate`.** Rejected: interactive by default, non-destructive so it
  no-ops on rebuild, and conflicts with the repo-template convention used by every other agent config.
- **Split into two specs (slim swap, then MCP infra).** Considered — the subsystems are genuinely
  independent. Rejected in favour of one spec with phased delivery, to keep the whole target state
  visible in one document.
