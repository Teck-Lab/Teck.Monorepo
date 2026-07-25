# Oh My OpenAgent All-GPT Team Mode Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace oh-my-opencode-slim with normal Oh My OpenAgent, routing all agents through native OpenAI GPT models and enabling visible tmux Team Mode from the first launch.

**Architecture:** The dev-container keeps a committed OpenCode config and a committed normal-OMO JSONC template, both copied into the persisted user config directory by `postCreate.sh`. Normal OMO owns multi-agent routing and GPT-only fallback lists; LiteLLM stays installed only for unrelated consumers. An `omo` shell function starts OpenCode with a real port inside tmux so Team Mode panes can attach.

**Tech Stack:** OpenCode >= 1.4.0, `oh-my-openagent`, OpenAI provider auth, JSONC, Bash, tmux, dev containers.

## Global Constraints

- Register **only** `oh-my-openagent`; slim and normal OMO must never be active together.
- Set the OpenCode root default and every normal-OMO agent/category model to an `openai/*` GPT model.
- Use `openai/gpt-5.6-sol`, `openai/gpt-5.6-terra`, and `openai/gpt-5.6-luna` only after `opencode models --refresh` confirms all three resolve.
- Keep all configured `fallback_models` within `openai/*`; do not introduce cross-provider fallback.
- Enable `team_mode.enabled: true`, `tmux_visualization: true`, `max_parallel_members: 4`, and `max_members: 8`.
- Retain Context7, SearXNG, Crawl4AI, GitHub, LiteLLM, `cc-safety-net`, `opencode-mem`, `opencode-snip`, and Superpowers unchanged unless a documentation comment is being corrected.
- Do not change application code under `src/`, application tests, LiteLLM routes, or secrets.
- Do not commit, push, or tag unless the user explicitly asks.
- Restart OpenCode after any config change; it does not hot-reload configuration.

---

## File Structure

| File | Responsibility |
|---|---|
| `.devcontainer/opencode/opencode.json` | Normal-OMO plugin registration, native GPT default model, retained providers/MCPs/plugins. |
| `.devcontainer/opencode/oh-my-openagent.jsonc` | Source-of-truth Team Mode, all-GPT agent/category routing, and GPT-only fallbacks. |
| `.opencode/agent/fast-generic.md` | Standard OpenCode replacement for slim's custom mechanical-command agent. |
| `.devcontainer/postCreate.sh` | Seeds canonical OMO config and installs the tmux/explicit-port `omo` launcher. |
| `.devcontainer/Dockerfile` | Documents why tmux remains installed. |
| `.devcontainer/README.md` | Documents normal OMO, GPT routing, authentication, Team Mode, and the `omo` launcher. |
| `.devcontainer/litellm/config.yaml` | Corrects fallback-ownership comments without changing gateway behavior. |
| `docs/superpowers/specs/2026-07-20-oh-my-opencode-slim-migration-design.md` | Marks the obsolete slim design as superseded. |
| `docs/superpowers/plans/2026-07-20-oh-my-opencode-slim-migration.md` | Marks the obsolete slim implementation plan as superseded and non-executable. |

### Task 1: Validate the target runtime and preserve a rollback point

**Files:**
- Modify: `~/.config/opencode/opencode.json` (only after creating the backup)
- Modify: `~/.config/opencode/oh-my-opencode-slim.jsonc` (only after creating the backup)
- Create: `~/.config/opencode/backups/omo-to-openagent-<UTC timestamp>/`

**Interfaces:**
- Consumes: the persisted user OpenCode configuration and native OpenAI credentials.
- Produces: a verified set of available GPT IDs and a recoverable copy of the slim configuration.

- [ ] **Step 1: Confirm required executables and OpenCode version**

Run:

```bash
opencode --version
command -v bunx && bunx --version
tmux -V
```

Expected: OpenCode reports `1.4.0` or later, `bunx` is available, and tmux is installed. Stop and upgrade/fix the missing prerequisite rather than editing configuration against an unsupported runtime.

- [ ] **Step 2: Confirm native OpenAI access and the three routing models**

Run the interactive login only if `opencode models --refresh` does not list native OpenAI models:

```bash
opencode auth login
opencode models --refresh
```

Expected: `openai/gpt-5.6-sol`, `openai/gpt-5.6-terra`, and `openai/gpt-5.6-luna` are visible and resolve. If any is unavailable, stop before modifying tracked files; select an available GPT replacement and update the approved design/spec first.

- [ ] **Step 3: Back up active slim configuration with restrictive permissions**

Run:

```bash
backup_dir="$HOME/.config/opencode/backups/omo-to-openagent-$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p "$backup_dir"
cp -a "$HOME/.config/opencode/opencode.json" "$backup_dir/opencode.json"
cp -a "$HOME/.config/opencode/oh-my-opencode-slim.jsonc" "$backup_dir/oh-my-opencode-slim.jsonc"
chmod 700 "$backup_dir"
chmod 600 "$backup_dir"/*.json "$backup_dir"/*.jsonc
```

Expected: both files exist beneath one timestamped directory. Do not copy `auth.json`, API keys, or any secret-bearing file into the repository.

- [ ] **Step 4: Record the exact verified IDs in the implementation notes**

Add the three model IDs and the OpenCode version to the execution handoff/PR description. This makes a future rollback or provider entitlement change diagnosable without recording credentials.

### Task 2: Replace the plugin and add the canonical all-GPT OMO configuration

**Files:**
- Modify: `.devcontainer/opencode/opencode.json:3-14`
- Create: `.devcontainer/opencode/oh-my-openagent.jsonc`
- Delete: `.devcontainer/opencode/oh-my-opencode-slim.jsonc`
- Create: `.opencode/agent/fast-generic.md`

**Interfaces:**
- Consumes: verified OpenAI model IDs from Task 1.
- Produces: a dev-container template that registers normal OMO, supplies all-GPT routing, and preserves the `@fast-generic` delegation contract.

- [ ] **Step 1: Make the root OpenCode config native-GPT-first**

In `.devcontainer/opencode/opencode.json`, make the following structural change while preserving the entire `mcp` and `provider.litellm` objects:

```json
{
  "$schema": "https://opencode.ai/config.json",
  "model": "openai/gpt-5.6-sol",
  "plugin": [
    "oh-my-openagent",
    "cc-safety-net",
    "opencode-mem",
    "opencode-snip@1.6.1",
    "superpowers@git+https://github.com/obra/superpowers.git"
  ]
}
```

Remove the current `agent.explore.disable` and `agent.general.disable` block so it cannot suppress normal OMO's `explore` agent or force non-GPT built-in routing.

- [ ] **Step 2: Write the normal OMO Team Mode template**

Create `.devcontainer/opencode/oh-my-openagent.jsonc` using this schema and routing matrix. `prompt_append` content preserves the intent of the supplied example without introducing a second provider.

```jsonc
{
  "$schema": "https://raw.githubusercontent.com/code-yeongyu/oh-my-openagent/dev/assets/oh-my-opencode.schema.json",
  "agents": {
    "sisyphus": {
      "model": "openai/gpt-5.6-sol",
      "fallback_models": ["openai/gpt-5.6-terra"],
      "prompt_append": "Delegate implementation to hephaestus and parallelize independent exploration."
    },
    "hephaestus": {
      "model": "openai/gpt-5.6-sol",
      "fallback_models": ["openai/gpt-5.6-terra"],
      "prompt_append": "Own implementation tasks end-to-end. Use LSP and AST-aware search when they reduce risk."
    },
    "prometheus": { "model": "openai/gpt-5.6-luna", "fallback_models": ["openai/gpt-5.6-sol"], "prompt_append": "Keep plans concise and name the files and decisions that matter." },
    "atlas": { "model": "openai/gpt-5.6-sol", "fallback_models": ["openai/gpt-5.6-terra"] },
    "oracle": { "model": "openai/gpt-5.6-sol", "fallback_models": ["openai/gpt-5.6-terra"] },
    "librarian": { "model": "openai/gpt-5.6-luna", "fallback_models": ["openai/gpt-5.6-sol"] },
    "explore": { "model": "openai/gpt-5.6-luna", "fallback_models": ["openai/gpt-5.6-sol"] },
    "multimodal-looker": { "model": "openai/gpt-5.6-sol", "fallback_models": ["openai/gpt-5.6-terra"] },
    "metis": { "model": "openai/gpt-5.6-luna", "fallback_models": ["openai/gpt-5.6-sol"] },
    "momus": { "model": "openai/gpt-5.6-sol", "fallback_models": ["openai/gpt-5.6-terra"], "prompt_append": "Focus on code quality, edge cases, and test coverage." },
    "sisyphus-junior": { "model": "openai/gpt-5.6-luna", "fallback_models": ["openai/gpt-5.6-sol"] }
  },
  "categories": {
    "quick": { "model": "openai/gpt-5.6-luna", "fallback_models": ["openai/gpt-5.6-sol"] },
    "unspecified-low": { "model": "openai/gpt-5.6-luna", "fallback_models": ["openai/gpt-5.6-sol"] },
    "unspecified-high": { "model": "openai/gpt-5.6-sol", "fallback_models": ["openai/gpt-5.6-terra"] },
    "visual-engineering": { "model": "openai/gpt-5.6-sol", "fallback_models": ["openai/gpt-5.6-terra"] },
    "deep": { "model": "openai/gpt-5.6-terra", "fallback_models": ["openai/gpt-5.6-sol"] },
    "ultrabrain": { "model": "openai/gpt-5.6-sol", "fallback_models": ["openai/gpt-5.6-terra"] }
  },
  "team_mode": {
    "enabled": true,
    "max_parallel_members": 4,
    "max_members": 8,
    "tmux_visualization": true
  },
  "background_task": {
    "defaultConcurrency": 5,
    "providerConcurrency": { "openai": 5 }
  }
}
```

Before retaining the `multimodal-looker` assignment, verify `gpt-5.6-sol` accepts image input in the refreshed catalog. If it does not, replace both its primary and fallback with verified image-capable `openai/*` GPT IDs and record that change in the implementation notes.

- [ ] **Step 3: Preserve the mechanical-command agent as a standard OpenCode subagent**

Create `.opencode/agent/fast-generic.md`:

```markdown
---
description: Runs routine mechanical commands, validation, and safe conventional-commit preparation without editing code.
mode: subagent
model: openai/gpt-5.6-luna
permission:
  edit: deny
  bash:
    "*": ask
    "git *": allow
    "git reset *": deny
    "git clean *": deny
    "bun *": allow
    "nx *": allow
    "dotnet *": allow
---

Run requested shell commands and report concise outcomes. Before a commit or push, inspect `git status`, `git diff`, and recent history; stage only intended files, avoid secrets, and preserve conventional-commit style. Do not amend, rebase, reset, clean, force-push, delete branches, or edit source files unless the user explicitly requests that exact action.
```

The broad `"*": ask` rule appears before the narrower rules because OpenCode applies the last matching permission rule. Keep the agent's model on native OpenAI so `@fast-generic` also obeys the all-GPT policy.

- [ ] **Step 4: Run a tracked-file consistency check**

Run:

```bash
git diff --check -- .devcontainer/opencode/opencode.json .devcontainer/opencode/oh-my-openagent.jsonc .opencode/agent/fast-generic.md
```

Expected: no whitespace errors. Do not run OpenCode yet; `postCreate.sh` must seed the new template first.

### Task 3: Seed normal OMO and provide a tmux-safe launcher

**Files:**
- Modify: `.devcontainer/postCreate.sh:75-91`
- Modify: `.devcontainer/postCreate.sh:154-202`
- Modify: `.devcontainer/Dockerfile:27-35`

**Interfaces:**
- Consumes: the canonical `.devcontainer/opencode/oh-my-openagent.jsonc` from Task 2 and a tmux-enabled container.
- Produces: an active user config at `~/.config/opencode/oh-my-openagent.jsonc` and an `omo` launcher that supplies the required OpenCode port.

- [ ] **Step 1: Replace the seeded slim template with the normal OMO template**

Replace the slim-specific comment and copy command at `postCreate.sh:82-87` with:

```bash
# Normal OMO is registered by opencode.json and auto-installed by OpenCode. This
# committed template owns Team Mode and model routing, so do not run the installer
# here: it would overwrite the reproducible configuration on every rebuild.
cp .devcontainer/opencode/oh-my-openagent.jsonc "$HOME/.config/opencode/oh-my-openagent.jsonc" \
  || echo "WARN: could not seed normal OMO config (continuing)"
```

Do not run `bunx oh-my-openagent install` from `postCreate.sh`; the committed template plus the OpenCode plugin loader is the repeatable container path.

- [ ] **Step 2: Remove slim's background-subagent export and stale persisted line**

Delete the `OPENCODE_EXPERIMENTAL_BACKGROUND_SUBAGENTS` block at `postCreate.sh:154-161`. Add this cleanup immediately before the new launcher block so existing persisted homes stop exporting a slim-only variable:

```bash
python3 - "$HOME/.bashrc" <<'PY'
import re
from pathlib import Path

path = Path(__import__("sys").argv[1])
if path.exists():
    content = path.read_text().replace(
        "export OPENCODE_EXPERIMENTAL_BACKGROUND_SUBAGENTS=true\n", ""
    )
    content = re.sub(
        r"\n# Launch OpenCode with an explicit port so oh-my-opencode-slim can open\n"
        r"# subagent panes in tmux\. Usage: `tmux` then `omos`\.\nomos\(\) \{.*?\n\}\n",
        "\n",
        content,
        flags=re.DOTALL,
    )
    path.write_text(content)
PY
```

- [ ] **Step 3: Replace `omos()` with a normal-OMO `omo()` launcher**

Replace the `omos()` installation block at `postCreate.sh:163-202` with an idempotent `omo()` function. It must reject non-tmux launches and preserve a user-supplied `--port`; otherwise it allocates a loopback port and forwards every original argument:

```bash
if ! grep -qF 'omo()' "$HOME/.bashrc" 2>/dev/null; then
  cat >> "$HOME/.bashrc" <<'OMO_EOF'

# Run normal OMO Team Mode in tmux with a real OpenCode port for pane attachment.
omo() {
  if [ -z "${TMUX:-}" ]; then
    printf '%s\n' 'Start tmux first: tmux new -s omo, then run omo.' >&2
    return 2
  fi
  local port=""
  local -a args=("$@")
  local i
  for (( i=0; i<${#args[@]}; i++ )); do
    case "${args[i]}" in
      --port=*) port="${args[i]#--port=}"; break ;;
      --port) port="${args[i+1]:-}"; break ;;
    esac
  done
  if [ -z "$port" ]; then
    port="$(python3 -c 'import socket; s=socket.socket(); s.bind(("127.0.0.1", 0)); print(s.getsockname()[1]); s.close()')" || return 1
    OPENCODE_PORT="$port" command opencode --port "$port" "${args[@]}"
  else
    OPENCODE_PORT="$port" command opencode "${args[@]}"
  fi
}
OMO_EOF
fi
```

Retain no slim-specific launcher text or function in the persisted `.bashrc`; the cleanup removes the exact prior `omos()` block before the new function is appended.

- [ ] **Step 4: Correct the tmux package rationale**

Replace Dockerfile lines 27-32 with a comment that identifies tmux as required for normal OMO Team Mode visualization and explains that `omo` starts OpenCode with an explicit port. Keep the package installation command unchanged.

- [ ] **Step 5: Check shell syntax without executing provisioning**

Run:

```bash
bash -n .devcontainer/postCreate.sh
git diff --check -- .devcontainer/postCreate.sh .devcontainer/Dockerfile
```

Expected: both commands exit successfully. Do not run `postCreate.sh` directly; it performs unrelated credential and GPG setup.

### Task 4: Replace slim-specific documentation and historical guidance

**Files:**
- Modify: `.devcontainer/README.md:45-67`
- Modify: `.devcontainer/README.md:118-198`
- Modify: `.devcontainer/litellm/config.yaml:3-18`
- Modify: `.devcontainer/litellm/config.yaml:192-193`
- Modify: `docs/superpowers/specs/2026-07-20-oh-my-opencode-slim-migration-design.md:1-6`
- Modify: `docs/superpowers/plans/2026-07-20-oh-my-opencode-slim-migration.md:1-6`

**Interfaces:**
- Consumes: the final plugin, configuration, and launcher names from Tasks 2-3.
- Produces: operator documentation that instructs users to start normal OMO Team Mode correctly and never directs them to a removed slim configuration.

- [ ] **Step 1: Correct fallback ownership everywhere**

At the README and LiteLLM comment locations, replace claims that slim's top-level `fallback` controls cross-model recovery with this rule:

```text
Normal OMO owns cross-model recovery through each agent/category's
`fallback_models`; LiteLLM only balances routes within a LiteLLM model pool.
```

Keep the LiteLLM routing configuration itself unchanged.

- [ ] **Step 2: Rewrite the plugin and agent section**

Replace the slim section in `.devcontainer/README.md:134-198` with an operator-focused normal OMO section that states:

1. `oh-my-openagent` is auto-installed from the OpenCode plugin list.
2. `.devcontainer/opencode/oh-my-openagent.jsonc` is copied to the persisted user config directory on rebuild.
3. All 11 normal OMO agents and all configured categories use `openai/*` GPT models with GPT-only fallbacks.
4. Users authenticate once with `opencode auth login`; model IDs are verified with `opencode models --refresh`.
5. Team Mode starts enabled, has four concurrent members and eight total members, and tmux visualization is enabled.
6. The exact launch sequence is:

```bash
tmux new -s omo
omo
```

7. `bunx oh-my-openagent doctor --verbose` is the configuration-health command.

Remove every statement about slim presets, seven agents, the desktop companion, the `OPENCODE_EXPERIMENTAL_BACKGROUND_SUBAGENTS` flag, `omos`, hybrid/cross-vendor routing, and slim prompt append files.

- [ ] **Step 3: Mark prior slim documents as historical**

At the top of each 2026-07-20 slim document, add a status note linking to:

```text
docs/superpowers/specs/2026-07-24-oh-my-openagent-gpt-team-mode-design.md
docs/superpowers/plans/2026-07-24-oh-my-openagent-gpt-team-mode.md
```

State that the old documents are historical, target slim, and must not be executed. Retain their contents for rollback context.

- [ ] **Step 4: Search for stale runtime instructions**

Run:

```bash
rg -n 'oh-my-opencode-slim|OPENCODE_EXPERIMENTAL_BACKGROUND_SUBAGENTS|\bomos\b|top-level `fallback`' .devcontainer docs/superpowers
```

Expected: remaining matches exist only in the two historical documents and explicitly labelled rollback notes; no active dev-container file instructs users to configure or launch slim.

### Task 5: Activate, validate, and test Team Mode in the rebuilt container

**Files:**
- Modify: `~/.config/opencode/opencode.json`
- Create: `~/.config/opencode/oh-my-openagent.jsonc`
- Retain as backup: `~/.config/opencode/backups/omo-to-openagent-<UTC timestamp>/`

**Interfaces:**
- Consumes: the rebuilt dev-container templates from Tasks 2-4, native OpenAI auth, and an active tmux session.
- Produces: a running normal OMO session with visual Team Mode and no active slim plugin.

- [ ] **Step 1: Rebuild the dev container and inspect the seeded active files**

Rebuild/reopen the dev container so `postCreate.sh` copies the new templates. Delete the now-inert active slim configuration only after the backup from Task 1 exists, then run:

```bash
rm -f "$HOME/.config/opencode/oh-my-opencode-slim.jsonc"
test -f "$HOME/.config/opencode/oh-my-openagent.jsonc"
test ! -e "$HOME/.config/opencode/oh-my-opencode-slim.jsonc"
rg -n 'oh-my-opencode-slim' "$HOME/.config/opencode/opencode.json" "$HOME/.config/opencode/oh-my-openagent.jsonc"
```

Expected: the canonical normal-OMO config exists, the active plugin registration has no slim entry, and the final `rg` exits with no matches. If an old slim config remains as an inert backup, keep it only under the timestamped backup directory.

- [ ] **Step 2: Run normal OMO diagnosis before opening a task**

Run:

```bash
opencode models --refresh
bunx oh-my-openagent doctor --verbose
```

Expected: every configured primary/fallback model resolves to `openai/*`, the schema validates, and Team Mode is reported enabled. Stop and restore Task 1's backups if doctor reports plugin collision, schema failure, missing native model access, or a non-GPT fallback.

- [ ] **Step 3: Smoke-test regular delegation and visible Team Mode**

Run inside a fresh tmux session:

```bash
tmux new -s omo
omo
```

In OpenCode, first delegate a bounded codebase-navigation task and confirm it selects `openai/gpt-5.6-luna`. Then run a bounded Team Mode task with four independent read-only subtasks and confirm four member panes appear. Confirm logs/tool output never report Claude, Gemini, Kimi, Grok, GitHub Copilot, LiteLLM, or OpenCode-hosted model routing.

- [ ] **Step 4: Run repository and security verification**

Run:

```bash
git diff --check
./tools/security-scan.sh
```

Expected: no whitespace errors; triage each scanner finding against the changed configuration and scripts before declaring the migration complete. Do not report completion based only on scanner execution.

- [ ] **Step 5: Preserve rollback information and report verification evidence**

Keep the timestamped backup directory until normal OMO has survived one container rebuild. Report the OpenCode version, doctor result, model-resolution result, regular-delegation model, Team Mode pane result, and any triaged security findings. Leave changes uncommitted unless the user separately asks for a conventional commit.
