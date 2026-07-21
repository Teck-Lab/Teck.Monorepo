# oh-my-opencode-slim Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the `oh-my-openagent` (omo) dev-container setup with `oh-my-opencode-slim`, configured from the upstream author's preset, with tmux multiplexer panes and hybrid `openai/` + `litellm/` model routing.

**Architecture:** Slim is declared in `opencode.json`'s `plugin` array and auto-installs via Bun on first launch. Its configuration comes from a **committed repo template** (`.devcontainer/opencode/oh-my-opencode-slim.jsonc`) `cp`'d into `~/.config/opencode/` by `postCreate.sh` — the upstream installer is never run, matching the convention every other agent config here already follows. Reasoning-heavy agents use the native `openai/` provider (GPT-5 Pro subscription via `opencode auth login`); cost-sensitive agents keep using the LiteLLM gateway on `:4000`, which is otherwise untouched.

**Tech Stack:** OpenCode, oh-my-opencode-slim, Bun, tmux, LiteLLM (docker compose), bash, JSONC.

**Spec:** `docs/superpowers/specs/2026-07-20-oh-my-opencode-slim-migration-design.md`

## Global Constraints

- **Committed templates are the source of truth.** Never run `bunx oh-my-opencode-slim@latest install` in `postCreate.sh`. Templates under `.devcontainer/` re-seed on every rebuild; in-container copies are ephemeral.
- **Retain these plugins untouched:** `cc-safety-net`, `opencode-mem`, `superpowers@git+https://github.com/obra/superpowers.git`.
- **Never edit `litellm/config.yaml`'s `model_list` or `router_settings`.** Comment updates only.
- **Every `mcps` array entry must name a server defined in `opencode.json`'s `mcp` block.** Until a phase lands its servers, the arrays stay `[]`. No dangling references at any point.
- **`postCreate.sh` is failure-tolerant by design.** Every new step follows the existing `|| echo "WARN: ... (continuing)"` style and must never fail the container build.
- **`.bashrc` edits must be idempotent**, using the existing `grep -qxF` / `grep -qF` guard pattern.
- **Commit signing is mandatory** (`commit.gpgsign=true`, key `FF4693E3D74495BA`, author `jl@tecklab.dk`). Never bypass it. If signing fails, stop and surface it.
- **Pin every external image and package to an explicit version. Never use `latest`.** This repo pins throughout: LiteLLM is `ghcr.io/berriai/litellm:main-stable`, every devcontainer feature is locked to a sha256 digest in `devcontainer-lock.json`, and `CLAUDE.md` forbids `latest`. Container images take an explicit version tag; `npx`/`bunx` invocations take `pkg@x.y.z`. Discover the current version first, then pin it — do not guess a version number.
- **Work happens on `feat/opencode-slim-migration`**, never on `main`.
- **This container's `python3` is stripped — do not use it to parse JSON or YAML.** Verified on 3.12.3: `import json` and `import yaml` both raise `ModuleNotFoundError`. `import socket` *does* work, which is why the `omos` port-picker in Task 4 is safe as written — but do not extend the plan's reliance on `python3` beyond it.
- **Tooling actually available (each verified individually):** `jq` ✅, `node` ✅, `perl` ✅, `docker compose` ✅ — and **`yq` is NOT installed**. There is **no YAML parser in this container**. Do not write a YAML-parse verification step; validate YAML functionally instead (for compose files, `docker compose config`; for `litellm/config.yaml`, restart the gateway and check `/health/liveliness`).
- **Comments in `.json` files break the pre-commit Biome hook.** `biome.json` sets no `allowComments` override, so real `//` comments in a `.json`-extension file fail with parse errors. Either use `"//": "..."` string keys (the convention in `.devcontainer/opencode/oh-my-openagent.json`) or a `.jsonc` extension (the convention in `.devcontainer/opencode/opencode-mem.jsonc`, which Biome accepts with real comments).
- **Multiplexer layout is `main-horizontal`** (deviation D3), not the author's `main-vertical` — tuned for a right-docked VS Code terminal panel.
- **`companion.enabled` is `false`** (deviation D2) — headless container, no display server.
- **Do not route any agent at `gemini-2.5-flash`** (deviation D1) — free tier is 20 requests/**day**, single-route, no fallback.

---

### Task 1: Resolve O1 — pin native OpenAI model IDs

Everything downstream depends on knowing which model IDs the GPT-5 Pro subscription actually exposes. The author's `gpt-5.6-terra` / `gpt-5.6-sol` / `gpt-5.6-luna` / `gpt-5.3-codex-spark` IDs are from **his** account and must not be copied. omo never pinned IDs (it used built-in OpenAI defaults), so this repo has no prior record of them.

**Files:**
- Create: `docs/superpowers/plans/2026-07-20-model-ids.md` (scratch record, deleted in Task 10)

**Interfaces:**
- Produces: three pinned strings used verbatim by Task 2 — `<ORCH_MODEL>`, `<PRO_MODEL>`, `<ECON_MODEL>`.

- [ ] **Step 1: Confirm OpenCode is present**

Run: `opencode --version`
Expected: a version string. If missing, stop — the devcontainer feature failed.

- [ ] **Step 2: Authenticate (USER-RUN — interactive, do not run this yourself)**

Ask the user to run:

```bash
opencode auth login   # select OpenAI, complete the OAuth/ChatGPT flow
```

Then confirm the credential landed:

```bash
test -f ~/.local/share/opencode/auth.json && echo "auth.json present"
```

Expected: `auth.json present`.

- [ ] **Step 3: List available models**

```bash
opencode models --refresh
opencode models | grep '^openai/'
```

Expected: a list of `openai/<id>` entries.

- [ ] **Step 4: Apply the selection rule and record the result**

Pick exactly three IDs from the Step 3 output:

- `<PRO_MODEL>` — the strongest reasoning-tier model available (prefer an ID containing `pro`; else the highest reasoning tier listed). Backs `oracle`.
- `<ORCH_MODEL>` — a fast general model (prefer an ID containing `fast`; else the mid tier). Backs `orchestrator`.
- `<ECON_MODEL>` — the cheapest/fastest listed model (prefer `mini`, `spark`, or `low`-tier naming). Backs `librarian` and `explorer`.

If fewer than three distinct tiers exist, reuse the closest available ID and note it. If **no** Pro-tier model appears, set `<PRO_MODEL>` to the strongest available and record that O1's documented fallback was taken.

Write `docs/superpowers/plans/2026-07-20-model-ids.md`:

```markdown
# Resolved OpenAI model IDs (O1)

Source: `opencode models | grep '^openai/'` on 2026-07-20.

- ORCH_MODEL: openai/<paste exact id>
- PRO_MODEL:  openai/<paste exact id>
- ECON_MODEL: openai/<paste exact id>

Full listing:
<paste the grep output>

Fallback taken? <yes/no — if yes, explain>
```

- [ ] **Step 5: Commit**

```bash
git add docs/superpowers/plans/2026-07-20-model-ids.md
git commit -m "chore(devcontainer): record resolved OpenAI model IDs for slim migration"
```

---

### Task 2: Add the slim config template and swap the plugin

**Files:**
- Create: `.devcontainer/opencode/oh-my-opencode-slim.jsonc`
- Modify: `.devcontainer/opencode/opencode.json` (the `plugin` array)

**Interfaces:**
- Consumes: `<ORCH_MODEL>`, `<PRO_MODEL>`, `<ECON_MODEL>` from Task 1.
- Produces: preset name `teck`; custom agent `fast-generic`; council preset `default`.

- [ ] **Step 1: Verify the current (pre-change) state**

Run: `grep -n 'oh-my-openagent' .devcontainer/opencode/opencode.json`
Expected: one match on the `plugin` array line.

- [ ] **Step 2: Create the slim template**

Create `.devcontainer/opencode/oh-my-opencode-slim.jsonc`. **Substitute the three Task 1 IDs verbatim** — do not leave the angle-bracket tokens in the committed file.

```jsonc
{
  // `@latest` here is a deliberate exception to the pinning constraint: this URL
  // is an editor/IDE validation hint, never fetched or executed at runtime, and
  // it must track the plugin's actual installed version — which auto-updates.
  // Pinning it would eventually validate this file against the WRONG schema.
  "$schema": "https://unpkg.com/oh-my-opencode-slim@latest/oh-my-opencode-slim.schema.json",

  // Active preset. Mirrors the upstream author's preset structure
  // (docs/authors-preset.md) with models substituted for what this
  // environment actually has. See the migration spec for deviations D1-D3.
  "preset": "teck",
  "showStartupToast": false,

  // D2: the companion is a desktop GUI app; this container is headless.
  "companion": { "enabled": false },

  // D3: main-horizontal (NOT the author's main-vertical) — tuned for a
  // right-docked VS Code terminal panel (tall/narrow). If you dock the
  // terminal at the bottom instead (wide/short), switch to "main-vertical".
  "multiplexer": {
    "type": "tmux",
    "layout": "main-horizontal",
    "main_pane_size": 60
  },

  "presets": {
    "teck": {
      // Runs on every turn — latency matters more than depth here.
      "orchestrator": {
        "model": "openai/<ORCH_MODEL>",
        "skills": ["*"],
        "mcps": []
      },
      // The deep-reasoning seat. This is where the Pro subscription pays off.
      "oracle": {
        "model": "openai/<PRO_MODEL>",
        "variant": "high",
        "skills": ["deepwork", "verification-planning", "reflect", "simplify"],
        "mcps": []
      },
      "librarian": {
        "model": "openai/<ECON_MODEL>",
        "variant": "low",
        "skills": [],
        "mcps": []
      },
      "explorer": {
        "model": "openai/<ECON_MODEL>",
        "variant": "low",
        "skills": ["codemap"],
        "mcps": []
      },
      // D1: author uses Gemini here. Our only Gemini route is capped at
      // 20 requests/DAY, so we use flat-rate OpenCode Go pool models instead.
      "designer": {
        "model": "litellm/glm-5.2",
        "skills": [],
        "mcps": []
      },
      "fixer": {
        "model": "litellm/kimi-k2.7-code",
        "variant": "low",
        "skills": ["codemap", "simplify"],
        "mcps": []
      }
    }
  },

  "agents": {
    // Verbatim from the upstream author's preset — the prompt encodes a
    // useful safety boundary (no code edits, no destructive git history).
    "fast-generic": {
      "model": "litellm/deepseek-v4-flash",
      "prompt": "You are a fast generic execution agent for routine mechanical command work. Run requested shell commands, inspect results, and report concise outcomes. For git commits or pushes, inspect git status, git diff, and recent log first; stage only intended files; avoid secrets; preserve repository commit-message style; never amend, rebase, reset --hard, clean, force-push, delete branches, or perform destructive history operations unless the user explicitly requested that exact operation. Do not edit code or make architecture/design decisions.",
      "orchestratorPrompt": "Delegate to @fast-generic for routine mechanical command work: git status/diff/log reconnaissance, normal commit preparation, creating commits, pushing commits, and no-edit command validation such as lint, typecheck, static verification, tests, builds, or package-manager equivalents. Ask it to inspect diffs before committing, stage only intended files, avoid secrets, preserve repository commit-message style, and report final commit hashes or push results. Do not use it for code edits, design work, architecture, debugging strategy, docs research, or destructive git history operations such as amend, rebase, reset --hard, clean, force-push, or deleting branches unless the user explicitly requested that exact operation.",
      "skills": [],
      "mcps": []
    }
  },

  // Cross-vendor consensus: one frontier model plus two independent
  // gateway-backed families, so councillors don't share a failure mode.
  "council": {
    "default_preset": "default",
    "presets": {
      "default": {
        "pro": { "model": "openai/<PRO_MODEL>", "variant": "high" },
        "glm": { "model": "litellm/glm-5.2" },
        "deepseek": { "model": "litellm/deepseek-v4-pro" }
      }
    }
  },

  // Slim owns CROSS-MODEL failover (this block). LiteLLM balances routes
  // WITHIN one model pool only. That split replaces omo's per-agent
  // `fallback_models` — keep the two layers distinct.
  "fallback": {
    "enabled": true,
    "timeoutMs": 15000,
    "maxRetries": 3,
    "retry_on_empty": true
  }
}
```

- [ ] **Step 3: Verify the template is valid JSONC and has no unsubstituted tokens**

```bash
grep -c '<ORCH_MODEL>\|<PRO_MODEL>\|<ECON_MODEL>' .devcontainer/opencode/oh-my-opencode-slim.jsonc
```

Expected: `0`. If non-zero, Task 1's IDs were not substituted — fix before continuing.

```bash
node -e "const s=require('fs').readFileSync('.devcontainer/opencode/oh-my-opencode-slim.jsonc','utf8'); JSON.parse(s.replace(/^\s*\/\/.*$/gm,'')); console.log('parses OK')"
```

Expected: `parses OK`.

- [ ] **Step 4: Swap the plugin name in `opencode.json`**

Change the `plugin` array from:

```json
  "plugin": [
    "oh-my-openagent",
    "cc-safety-net",
    "opencode-mem",
    "superpowers@git+https://github.com/obra/superpowers.git"
  ],
```

to:

```json
  "plugin": [
    "oh-my-opencode-slim",
    "cc-safety-net",
    "opencode-mem",
    "superpowers@git+https://github.com/obra/superpowers.git"
  ],
```

- [ ] **Step 5: Verify the swap and that retained plugins survived**

```bash
grep -c 'oh-my-openagent' .devcontainer/opencode/opencode.json
grep -c 'oh-my-opencode-slim\|cc-safety-net\|opencode-mem\|superpowers' .devcontainer/opencode/opencode.json
```

Expected: `0`, then `4`.

- [ ] **Step 6: Commit**

```bash
git add .devcontainer/opencode/oh-my-opencode-slim.jsonc .devcontainer/opencode/opencode.json
git commit -m "feat(devcontainer): add oh-my-opencode-slim config template and swap plugin"
```

---

### Task 3: Migrate the superpowers directive to project-local prompt appends

Slim has no `prompt_append`. Its replacement is `{agent}_append.md` files, which can live **project-local and committed** — no seeding, survives rebuilds, reviewable in PRs.

**Files:**
- Create: `.opencode/oh-my-opencode-slim/orchestrator_append.md`
- Create: `.opencode/oh-my-opencode-slim/fixer_append.md`

**Interfaces:**
- Consumes: content of `.devcontainer/opencode/superpowers-skills.md` (deleted in Task 5).

- [ ] **Step 1: Confirm the project-local directory is not gitignored**

```bash
git check-ignore -v .opencode/oh-my-opencode-slim/ ; echo "exit=$?"
```

Expected: `exit=1` (not ignored). If it IS ignored, add a negation to `.opencode/.gitignore` before continuing — these files must be committed to work.

- [ ] **Step 2: Create the orchestrator append**

Create `.opencode/oh-my-opencode-slim/orchestrator_append.md`:

```markdown
# Use your superpowers skills

You have the **superpowers** skills, available through OpenCode's native `skill`
tool. Lean into them — don't let the orchestration workflow crowd them out.

Follow superpowers' own discipline (its `using-superpowers` skill): **when a skill
applies to what you're about to do, invoke it first** via the `skill` tool,
announce "Using [skill] to [purpose]", and follow it. Process skills set the
approach — let them. This is **not** a rigid mandate: **if a skill turns out wrong
for the situation, you don't have to use it**, and direct/user instructions always
take precedence.

Skills that most often apply at the orchestration level:

- `brainstorming` — before building something new, to explore intent and design
  before writing code.
- `writing-plans` — when a spec exists and multi-step work needs sequencing.
- `verification-before-completion` — before claiming work is done: run the checks
  and show the evidence.
- `security-review` (**project skill**, `.opencode/skills/`) — before declaring
  implementation work complete or pushing, run `./tools/security-scan.sh` to
  execute the same scans as CI (Semgrep SAST, Gitleaks secrets, Trivy SCA) and
  **triage** the findings — confirm each against the real code rather than
  dumping scanner output. Especially after touching auth, crypto, input handling,
  shell execution, SQL, or dependency manifests.

When delegating implementation to @fixer, expect it to follow
`test-driven-development` — don't ask it to skip tests to save a round trip.

Use the `skill` tool to list the rest. Priority when names collide: project >
personal > superpowers skills.
```

- [ ] **Step 3: Create the fixer append**

Create `.opencode/oh-my-opencode-slim/fixer_append.md`:

```markdown
# Use your superpowers skills

You have the **superpowers** skills, available through OpenCode's native `skill`
tool. Lean into them.

Follow superpowers' own discipline (its `using-superpowers` skill): **when a skill
applies to what you're about to do, invoke it first** via the `skill` tool,
announce "Using [skill] to [purpose]", and follow it. This is **not** a rigid
mandate: **if a skill turns out wrong for the situation, you don't have to use
it**, and direct/user instructions always take precedence.

As the implementation specialist, these apply to you most:

- `test-driven-development` — when implementing a feature or bugfix: write the
  test first, watch it fail, then minimal code to pass. It's the *default* way to
  work on real implementation; the skill's own exception is throwaway prototypes
  (ask first). Use it because it fits, not because you're forced to.
- `systematic-debugging` — on a bug, test failure, or unexpected behavior, before
  proposing a fix.
- `verification-before-completion` — before claiming work is done: run the checks
  and show the evidence.
- `security-review` (**project skill**, `.opencode/skills/`) — before declaring
  implementation work complete or pushing, run `./tools/security-scan.sh` and
  **triage** the findings against the real code rather than dumping scanner
  output. Especially after touching auth, crypto, input handling, shell
  execution, SQL, or dependency manifests.

Repository conventions live in the `AGENTS.md` tree — read the one nearest the
code you're touching before editing. `CLAUDE.md` at the repo root summarizes the
architecture rules that ArchUnitNET tests enforce as build failures.

Use the `skill` tool to list the rest. Priority when names collide: project >
personal > superpowers skills.
```

- [ ] **Step 4: Verify both files are tracked**

```bash
git add .opencode/oh-my-opencode-slim/
git status --short .opencode/oh-my-opencode-slim/
```

Expected: two `A` (added) lines.

- [ ] **Step 5: Commit**

```bash
git commit -m "feat(devcontainer): migrate superpowers directive to slim prompt appends"
```

---

### Task 4: Wire tmux multiplexer support into the shell

Panes attach via `opencode attach`, which needs a real TCP listener; OpenCode's default `--port 0` does not create one. Upstream ships a **zsh** helper; this container runs **bash**.

**Files:**
- Modify: `.devcontainer/postCreate.sh` (append two new blocks before the final `echo "==> postCreate complete"`)

**Interfaces:**
- Produces: shell function `omos`; env var `OPENCODE_EXPERIMENTAL_BACKGROUND_SUBAGENTS`.

- [ ] **Step 1: Verify neither is present yet**

```bash
grep -c 'BACKGROUND_SUBAGENTS\|omos()' .devcontainer/postCreate.sh
```

Expected: `0`.

- [ ] **Step 2: Add the background-subagents export block**

Insert into `.devcontainer/postCreate.sh`, immediately before the final `echo "==> postCreate complete"`:

```bash
echo "==> Enabling OpenCode background subagents (required by oh-my-opencode-slim)"
# Slim's default orchestration dispatches specialists as background subagents,
# which OpenCode gates behind this experimental flag. Without it the
# orchestrator silently runs everything inline and no multiplexer panes appear.
BG_LINE='export OPENCODE_EXPERIMENTAL_BACKGROUND_SUBAGENTS=true'
if ! grep -qxF "$BG_LINE" "$HOME/.bashrc" 2>/dev/null; then
  printf '\n# Required by oh-my-opencode-slim background orchestration\n%s\n' "$BG_LINE" >> "$HOME/.bashrc"
fi
```

- [ ] **Step 3: Add the `omos` port-wrapper block**

Immediately after the Step 2 block:

```bash
echo "==> Installing the 'omos' OpenCode launcher (explicit --port, for tmux panes)"
# Multiplexer panes attach with `opencode attach`, which needs a real TCP
# listener. OpenCode's default (`--port 0`) doesn't create one, so subagent
# panes never appear. Upstream ships a zsh helper; this is the bash equivalent.
# Honours an explicit --port if you pass one; otherwise picks a free loopback
# port and passes it through. Plain `opencode` remains available, unwrapped.
if ! grep -qF 'omos()' "$HOME/.bashrc" 2>/dev/null; then
  cat >> "$HOME/.bashrc" <<'OMOS_EOF'

# Launch OpenCode with an explicit port so oh-my-opencode-slim can open
# subagent panes in tmux. Usage: `tmux` then `omos`.
omos() {
  local port=""
  local -a args=("$@")
  local i
  for (( i=0; i<${#args[@]}; i++ )); do
    case "${args[i]}" in
      --port=*) port="${args[i]#--port=}"; break ;;
      --port)
        if (( i + 1 < ${#args[@]} )); then
          port="${args[i+1]:-}"
        else
          # Trailing --port with no value: treat it as "no port specified"
          # and drop the dangling flag so it never reaches opencode next to
          # an auto-picked --port.
          unset 'args[i]'
        fi
        break
        ;;
    esac
  done
  if [ -z "$port" ]; then
    port="$(python3 -c 'import socket; s=socket.socket(); s.bind(("127.0.0.1",0)); print(s.getsockname()[1]); s.close()')" || return 1
    OPENCODE_PORT="$port" command opencode --port "$port" "${args[@]}"
  else
    OPENCODE_PORT="$port" command opencode "${args[@]}"
  fi
}
OMOS_EOF
fi
```

- [ ] **Step 4: Verify the script is syntactically valid**

```bash
bash -n .devcontainer/postCreate.sh && echo "syntax OK"
```

Expected: `syntax OK`.

- [ ] **Step 5: Test the blocks against a throwaway HOME (do not pollute the real `.bashrc` twice)**

```bash
TMPH=$(mktemp -d) && HOME=$TMPH bash -c '
  touch $HOME/.bashrc
  BG_LINE="export OPENCODE_EXPERIMENTAL_BACKGROUND_SUBAGENTS=true"
  grep -qxF "$BG_LINE" "$HOME/.bashrc" || printf "\n%s\n" "$BG_LINE" >> "$HOME/.bashrc"
  grep -qxF "$BG_LINE" "$HOME/.bashrc" || printf "\n%s\n" "$BG_LINE" >> "$HOME/.bashrc"
  echo "occurrences: $(grep -c BACKGROUND_SUBAGENTS $HOME/.bashrc)"
' && rm -rf $TMPH
```

Expected: `occurrences: 1` — proves the guard is idempotent across rebuilds.

- [ ] **Step 6: Verify the `omos` function parses**

```bash
bash -c 'omos() { local port=""; local -a args=("$@"); local i; for (( i=0; i<${#args[@]}; i++ )); do case "${args[i]}" in --port=*) port="${args[i]#--port=}"; break ;; --port) port="${args[i+1]}"; break ;; esac; done; echo "resolved_port=$port"; }; omos --port 4096; omos --port=5000; omos'
```

Expected three lines: `resolved_port=4096`, `resolved_port=5000`, `resolved_port=` (empty → the real function would auto-pick).

- [ ] **Step 7: Commit**

```bash
git add .devcontainer/postCreate.sh
git commit -m "feat(devcontainer): enable slim background subagents and add omos tmux launcher"
```

---

### Task 5: Remove omo remnants and update seeding

**Files:**
- Delete: `.devcontainer/opencode/oh-my-openagent.json`
- Delete: `.devcontainer/opencode/superpowers-skills.md`
- Modify: `.devcontainer/postCreate.sh` (the seeding block)

- [ ] **Step 1: Update the seeding block in `postCreate.sh`**

Replace this existing block:

```bash
# omo (oh-my-openagent) agent config. The plugin itself is declared in opencode.json's
# `plugin` array and auto-installs via Bun on the first `opencode` launch; this file
# just points its agents at the LiteLLM gateway so no guided-install TUI is needed.
cp .devcontainer/opencode/oh-my-openagent.json "$HOME/.config/opencode/oh-my-openagent.json" || echo "WARN: could not seed omo config (continuing)"
```

with:

```bash
# oh-my-opencode-slim agent config. The plugin itself is declared in opencode.json's
# `plugin` array and auto-installs via Bun on the first `opencode` launch; this file
# pins each agent's model so the upstream install TUI is never needed. The committed
# template is the source of truth — do NOT run `bunx oh-my-opencode-slim install`,
# which is interactive and refuses to overwrite an existing config anyway.
cp .devcontainer/opencode/oh-my-opencode-slim.jsonc "$HOME/.config/opencode/oh-my-opencode-slim.jsonc" || echo "WARN: could not seed slim config (continuing)"
```

- [ ] **Step 2: Remove the now-dead superpowers-skills seeding block**

Delete these lines from `postCreate.sh`:

```bash
# Directive appended (via omo prompt_append) to the implementer agents/categories,
# telling them to use the superpowers skills — TDD in particular — through
# OpenCode's `skill` tool. The superpowers plugin itself auto-installs via opencode.json.
cp .devcontainer/opencode/superpowers-skills.md "$HOME/.config/opencode/superpowers-skills.md" || echo "WARN: could not seed superpowers directive (continuing)"
```

The directive now lives in committed project-local prompt appends (Task 3) and needs no seeding.

- [ ] **Step 3: Delete the omo files**

```bash
git rm .devcontainer/opencode/oh-my-openagent.json .devcontainer/opencode/superpowers-skills.md
```

- [ ] **Step 4: Verify no references remain**

```bash
bash -n .devcontainer/postCreate.sh && echo "syntax OK"
grep -rn 'oh-my-openagent\|superpowers-skills' .devcontainer/ \
  --exclude=README.md --exclude=Dockerfile --exclude=config.yaml ; echo "exit=$?"
```

Expected: `syntax OK`, then `exit=1` (grep exit 1 = no matches).

Three files are **excluded on purpose** — every one still carries an omo mention that Task 6 rewrites: `README.md` (the plugin section), `Dockerfile` (the tmux rationale comment), and `litellm/config.yaml` (the fallback-ownership comments). Do not "fix" them here; that work belongs to the next task, and splitting it would make both commits incoherent.

To confirm the exclusions are the *only* remaining mentions, run:

```bash
grep -rln 'oh-my-openagent' .devcontainer/
```

Expected: exactly `README.md`, `Dockerfile`, and `litellm/config.yaml` — nothing else. Anything further is a genuine miss.

- [ ] **Step 5: Commit**

```bash
git add -A .devcontainer/
git commit -m "refactor(devcontainer): remove omo config and seeding in favor of slim"
```

---

### Task 6: Update documentation

**Files:**
- Modify: `.devcontainer/Dockerfile` (tmux comment, lines 17-19)
- Modify: `.devcontainer/litellm/config.yaml` (comments naming omo — lines ~5, ~14-16)
- Modify: `.devcontainer/README.md` (lines 25, 45, 61, 118-184)

- [ ] **Step 1: Update the Dockerfile tmux rationale**

Replace:

```dockerfile
# tmux — required by oh-my-openagent (omo): its `interactive_bash` tool and the
# Team Mode / background-subagent pane visualization shell out to tmux, so it must
# be installed and on PATH. (System package → installed here, not via a feature.)
```

with:

```dockerfile
# tmux — required by oh-my-opencode-slim's multiplexer integration: background
# subagents are spawned into live tmux panes, so tmux must be installed and on
# PATH. Launch OpenCode via the `omos` helper (see postCreate.sh) so it binds an
# explicit port — pane attachment needs a real TCP listener, which OpenCode's
# default `--port 0` does not provide. (System package → installed here, not via
# a feature.)
```

- [ ] **Step 2: Update the LiteLLM config comments**

In `.devcontainer/litellm/config.yaml`, replace the header line:

```yaml
# PER-MODEL POOLS (for oh-my-openagent / omo, which pins each agent to a specific
```

with:

```yaml
# PER-MODEL POOLS (for oh-my-opencode-slim, which pins each agent to a specific
```

And replace this block:

```yaml
# Cross-MODEL fallback (e.g. agent's model down -> try another model) is handled by
# omo's per-agent `fallback_models`, NOT here — LiteLLM only balances routes WITHIN
# a model. Keep this in sync with .devcontainer/opencode/opencode.json's `models`.
```

with:

```yaml
# Cross-MODEL fallback (e.g. agent's model down -> try another model) is handled by
# oh-my-opencode-slim's top-level `fallback` block, NOT here — LiteLLM only balances
# routes WITHIN a model. Keep this in sync with .devcontainer/opencode/opencode.json's
# `models`.
```

And in `router_settings`, replace:

```yaml
  # No cross-model `fallbacks` — omo owns cross-model fallback via per-agent
  # `fallback_models`; LiteLLM only balances routes within one model.
```

with:

```yaml
  # No cross-model `fallbacks` — oh-my-opencode-slim owns cross-model fallback via
  # its top-level `fallback` block; LiteLLM only balances routes within one model.
```

- [ ] **Step 3: Verify the YAML still parses**

There is no YAML parser in this container (no `yq`, no python `yaml`), so validate this
functionally rather than syntactically — which is the stronger check anyway, since it proves
the gateway still *loads* the config rather than merely that it parses.

First confirm the edit touched comments only:

```bash
git diff -U0 .devcontainer/litellm/config.yaml | grep -E '^[+-]' | grep -v '^[+-][+-]' | grep -vE '^[+-]\s*#' ; echo "non-comment changes exit=$?"
```

Expected: `non-comment changes exit=1` (no output — every changed line is a comment). Any
output here means a functional line was altered, violating the "comment updates only"
constraint.

Then prove the gateway still loads it. **The gateway is not currently running** (its container
exited earlier), so this both validates your edit and brings it back up:

```bash
docker compose -f .devcontainer/litellm/compose.yaml up -d --force-recreate
for _ in $(seq 1 20); do curl -fsS http://localhost:4000/health/liveliness >/dev/null 2>&1 && { echo "gateway reloaded config OK"; break; }; sleep 2; done
```

Expected: `gateway reloaded config OK`.

**If it does NOT come up, do not assume your edit caused it.** This config carries live
provider credentials that may have expired independently. Establish whether the failure
pre-dates your change before reporting it as one:

```bash
git stash && docker compose -f .devcontainer/litellm/compose.yaml up -d --force-recreate
sleep 10 && curl -fsS http://localhost:4000/health/liveliness >/dev/null && echo "PRE-EXISTING failure — not caused by this task" || echo "still down on the unmodified config too"
git stash pop
```

Report which case you observed. A pre-existing failure is out of scope for this task —
note it and move on; do not attempt to fix provider credentials.

- [ ] **Step 4: Rewrite the README omo section**

In `.devcontainer/README.md`:

- Line 25: change `| tmux | apt | Dockerfile (\`apt-get install tmux\`) — required by omo |` to `| tmux | apt | Dockerfile (\`apt-get install tmux\`) — required by oh-my-opencode-slim panes |`
- Line 45: change the `oh-my-openagent` link to `[oh-my-opencode-slim](https://github.com/alvinunreal/oh-my-opencode-slim)`
- Line 61: change `owned by omo's per-agent` to `owned by slim's top-level \`fallback\` block`
- Line 121: change `- \`oh-my-openagent\` — the multi-agent system (details below).` to `- \`oh-my-opencode-slim\` — the multi-agent system (details below).`

Replace the whole block at lines 134-184 with:

```markdown
**oh-my-opencode-slim is auto-installed & pre-wired — zero manual steps except auth:**
`opencode.json` lists `oh-my-opencode-slim` in its `plugin` array, so OpenCode
installs it via Bun on the first `opencode` launch (the upstream `bunx
oh-my-opencode-slim install` TUI is **never** run — it's interactive and would
fight the committed template). `postCreate.sh` seeds
`~/.config/opencode/oh-my-opencode-slim.jsonc` from
`.devcontainer/opencode/oh-my-opencode-slim.jsonc`.

- **Seven agents:** orchestrator, explorer, oracle, council, librarian, designer,
  fixer — plus a custom `fast-generic` for mechanical git/lint/test work.
- **Hybrid routing.** Reasoning-heavy agents run on the **native OpenAI provider**
  via your ChatGPT/GPT-5 Pro subscription (`opencode auth login` → OpenAI, one
  time), *not* the gateway: `oracle` gets the Pro-tier model, `orchestrator` a
  fast tier, `librarian`/`explorer` an economy tier. Cost-sensitive agents stay on
  the **LiteLLM gateway** pools: `designer` → `glm-5.2`, `fixer` →
  `kimi-k2.7-code`, `fast-generic` → `deepseek-v4-flash`.
- **No agent uses `gemini-2.5-flash`.** Its free tier is 20 requests per **day**,
  single-route, with no cross-model fallback — it would brick an agent by
  mid-morning. (The upstream author's preset uses Gemini here; we deliberately
  don't.)
- **Council** runs cross-vendor consensus (Pro + `glm-5.2` + `deepseek-v4-pro`) so
  councillors don't share a failure mode.
- **Fallback layering:** slim's top-level `fallback` block owns *cross-model*
  failover; LiteLLM balances routes *within* one model pool. Two distinct layers —
  keep them that way.
- **Superpowers skills are nudged in** via committed, project-local prompt
  appends: `.opencode/oh-my-opencode-slim/orchestrator_append.md` and
  `fixer_append.md`. Unlike omo's seeded `prompt_append`, these are
  version-controlled, reviewable in PRs, and survive rebuilds with no seeding
  step. Edit them to tune the directive.
- **The desktop companion is disabled** — it's a GUI app and this container is
  headless.

**tmux panes: launch with `omos`, not `opencode`.**
Slim spawns background subagents into live tmux panes. Two requirements:
`OPENCODE_EXPERIMENTAL_BACKGROUND_SUBAGENTS=true` (exported into `~/.bashrc` by
`postCreate.sh`) and an **explicit port** — pane attachment uses `opencode
attach`, which needs a real TCP listener, and OpenCode's default `--port 0`
doesn't create one. `postCreate.sh` installs an `omos` bash function that picks a
free port and passes it through:

```bash
tmux      # start a session first
omos      # instead of `opencode`
```

Outside tmux `omos` is a harmless no-op wrapper. Layout is `main-horizontal`
(main pane on top, subagents below), tuned for a **tall/narrow** terminal — the
VS Code panel docked right. Docking at the bottom instead (wide/short)? Switch
`multiplexer.layout` to `main-vertical` in the template.

Edit the committed templates under `.devcontainer/{opencode,codex}/` to change
models/agents — they re-seed on every rebuild (the in-container copies are
ephemeral). First `opencode` launch needs network to fetch the plugin; the
OpenAI-backed agents need the one-time `opencode auth login`.
```

- [ ] **Step 5: Verify no stale omo references survive**

```bash
grep -rn 'oh-my-openagent' .devcontainer/ ; echo "exit=$?"
```

Expected: `exit=1` (no matches) — the plugin name must be fully gone.

Do **not** additionally grep for a bare `omo`. The rewritten README intentionally
retains two comparative mentions ("Unlike omo's seeded `prompt_append`…") that
explain *why* the current arrangement differs from what it replaced. That context
is worth keeping; a zero-`omo` grep would flag it as a failure and invite deleting
useful history.

- [ ] **Step 6: Commit**

```bash
git add .devcontainer/
git commit -m "docs(devcontainer): document slim migration, tmux panes, and hybrid routing"
```

---

### Task 7: Phase 1 verification gate

Nothing beyond this point runs until every check here passes with observed output. Do not mark a step done from expectation — paste the actual result.

- [ ] **Step 1: Rebuild the container**

Rebuild the devcontainer (VS Code: *Dev Containers: Rebuild Container*), then inspect the `postCreate` output.

Expected: no `WARN:` lines mentioning `slim`, `omos`, or `BACKGROUND_SUBAGENTS`.

- [ ] **Step 2: Verify the seeded config matches the template**

```bash
diff .devcontainer/opencode/oh-my-opencode-slim.jsonc ~/.config/opencode/oh-my-opencode-slim.jsonc && echo "template matches"
```

Expected: `template matches`.

- [ ] **Step 3: Verify the shell environment**

```bash
echo "$OPENCODE_EXPERIMENTAL_BACKGROUND_SUBAGENTS"
type omos | head -1
```

Expected: `true`, then `omos is a function`.

- [ ] **Step 4: Verify the old omo config is gone**

```bash
test ! -f ~/.config/opencode/oh-my-openagent.json && echo "omo config absent"
```

Expected: `omo config absent`. (On a rebuilt container it will be; if you skipped the rebuild, delete it manually — a stale file could still be read.)

- [ ] **Step 5: Launch in tmux and ping all agents**

```bash
tmux
omos
```

Then in OpenCode: `ping all agents`

Expected: **all seven agents respond** (orchestrator, explorer, oracle, council, librarian, designer, fixer). Record any that fail with their error.

- [ ] **Step 6: Verify panes appear**

Delegate real work, e.g.: `explore the src/services/order service structure`

Expected: a subagent pane opens; layout is `main-horizontal` with the main pane ~60%.

- [ ] **Step 7: Verify the prompt append resolved**

Ask fixer to implement a trivial change.

Expected: it announces a superpowers skill (`test-driven-development`) — proving `.opencode/oh-my-opencode-slim/fixer_append.md` was found and appended.

- [ ] **Step 8: Verify retained plugins still work**

```bash
curl -fsS http://localhost:4747 >/dev/null && echo "opencode-mem UI up"
curl -fsS http://localhost:4000/health/liveliness >/dev/null && echo "litellm up"
```

Expected: both lines. Also confirm `cc-safety-net` still blocks a destructive command and the `skill` tool still lists superpowers skills.

- [ ] **Step 9: Record results and commit**

Append observed output to `docs/superpowers/plans/2026-07-20-model-ids.md` under a `## Phase 1 verification` heading, then:

```bash
git add docs/superpowers/plans/2026-07-20-model-ids.md
git commit -m "chore(devcontainer): record phase 1 slim verification results"
```

**STOP.** If any check failed, fix it before Task 8. Phase 1 must be green.

---

### Task 8: Phase 2 — context7 and github MCPs

**Files:**
- Modify: `.devcontainer/opencode/opencode.json` (add `mcp` block)
- Modify: `.devcontainer/opencode/oh-my-opencode-slim.jsonc` (populate `mcps` arrays)

- [ ] **Step 1: Add the `mcp` block to `opencode.json`**

Insert as a top-level key, after `"plugin"`:

```json
  "mcp": {
    "context7": {
      "type": "remote",
      "url": "https://mcp.context7.com/mcp",
      "enabled": true
    },
    "github": {
      "type": "local",
      "command": ["gh", "mcp", "server"],
      "enabled": true
    }
  },
```

- [ ] **Step 2: Verify `gh` can actually serve MCP before relying on it**

```bash
gh --version && gh auth status
gh mcp server --help >/dev/null 2>&1 && echo "gh mcp available" || echo "gh mcp NOT available"
```

If `gh mcp NOT available`, this `gh` build predates the built-in MCP server. Replace the `github` entry with the standalone server:

```json
    "github": {
      "type": "local",
      "command": ["npx", "-y", "@modelcontextprotocol/server-github@<VERSION>"],
      "environment": { "GITHUB_PERSONAL_ACCESS_TOKEN": "{env:GITHUB_TOKEN}" },
      "enabled": true
    }
```

Resolve `<VERSION>` first — per the global pinning constraint, do not ship a bare package name:

```bash
npm view @modelcontextprotocol/server-github version
```

and export `GITHUB_TOKEN` in `~/.bashrc` via `postCreate.sh` using `gh auth token`. Record which variant you used.

- [ ] **Step 3: Populate the `mcps` arrays in the slim template**

Per the spec's routing intent — research servers go to the librarian, not the orchestrator:

- `orchestrator.mcps`: `["*", "!context7", "!github"]`
- `librarian.mcps`: `["context7", "github"]`
- All others: leave `[]`

- [ ] **Step 4: Verify JSON validity and that every referenced server exists**

```bash
node -e "
const fs=require('fs');
const strip=s=>s.replace(/^\s*\/\/.*$/gm,'');
const oc=JSON.parse(strip(fs.readFileSync('.devcontainer/opencode/opencode.json','utf8')));
const sl=JSON.parse(strip(fs.readFileSync('.devcontainer/opencode/oh-my-opencode-slim.jsonc','utf8')));
const defined=new Set(Object.keys(oc.mcp||{}));
const refs=new Set();
const walk=o=>{for(const k in o){if(k==='mcps')o[k].forEach(m=>{const n=m.replace(/^!/,'');if(n!=='*')refs.add(n)});else if(typeof o[k]==='object'&&o[k])walk(o[k])}};
walk(sl);
const dangling=[...refs].filter(r=>!defined.has(r));
console.log('defined:',[...defined].join(',')||'(none)');
console.log('referenced:',[...refs].join(',')||'(none)');
console.log(dangling.length?'DANGLING: '+dangling.join(','):'no dangling references');
"
```

Expected: `no dangling references`. **This is the guard for the global constraint — run it after every phase that touches `mcps`.**

- [ ] **Step 5: Restart and verify**

Rebuild or restart OpenCode, then ask the librarian a docs question, e.g.:
`ask the librarian to look up the WolverineFx message-handler API via context7`

Expected: it returns docs sourced via context7. Then a repo query exercising `github`.

- [ ] **Step 6: Commit**

```bash
git add .devcontainer/opencode/
git commit -m "feat(devcontainer): wire context7 and github MCPs into slim librarian"
```

---

### Task 9: Phase 3 — self-hosted searxng and crawl4ai

Heaviest phase. crawl4ai pulls a Chromium-bearing image (~2 GB+) into the nested Docker daemon, running alongside LiteLLM. Mirrors the established LiteLLM pattern exactly.

**Files:**
- Create: `.devcontainer/mcp/compose.yaml`
- Create: `.devcontainer/start-mcp.sh`
- Modify: `.devcontainer/devcontainer.json` (`postStartCommand`, `forwardPorts`, `portsAttributes`)
- Modify: `.devcontainer/opencode/opencode.json` (`mcp` block)
- Modify: `.devcontainer/opencode/oh-my-opencode-slim.jsonc` (`mcps` arrays)

- [ ] **Step 1: Check available disk before pulling images**

```bash
df -h /var/lib/docker 2>/dev/null || df -h /
```

Expected: at least 5 GB free. If not, stop and reclaim space — a failed mid-pull leaves a broken layer cache.

- [ ] **Step 2: Discover current image versions to pin**

Per the global pinning constraint, resolve real version tags before writing the compose file — do not guess:

```bash
skopeo list-tags docker://docker.io/searxng/searxng 2>/dev/null | tail -20 \
  || curl -fsS "https://hub.docker.com/v2/repositories/searxng/searxng/tags?page_size=20" | jq -r '.results[].name'
curl -fsS "https://hub.docker.com/v2/repositories/unclecode/crawl4ai/tags?page_size=20" | jq -r '.results[].name'
```

Pick the newest **non-`latest`, non-prerelease** tag from each (searxng publishes date-stamped tags like `2026.7.1-abc1234`; crawl4ai publishes semver like `0.7.4`). Record both — they are substituted into Step 3.

- [ ] **Step 3: Create the compose file**

Create `.devcontainer/mcp/compose.yaml`, substituting the two tags from Step 2 for `<SEARXNG_TAG>` and `<CRAWL4AI_TAG>`:

```yaml
# Self-hosted MCP backends for oh-my-opencode-slim's research agents.
# Mirrors .devcontainer/litellm/compose.yaml: compose owns the service
# definitions; start-mcp.sh adds health-waiting and failure tolerance.
#
# To apply edits on an already-running stack:
#   docker compose -f .devcontainer/mcp/compose.yaml up -d --force-recreate
services:
  searxng:
    # Pinned per the global constraint — never `latest`. Bump deliberately.
    image: searxng/searxng:<SEARXNG_TAG>
    container_name: teck-searxng
    ports:
      - "8888:8080"
    environment:
      - SEARXNG_BASE_URL=http://localhost:8888/
      # JSON output is REQUIRED for MCP consumption and is off by default.
      - SEARXNG_SEARCH_FORMATS=html,json
    volumes:
      - searxng-config:/etc/searxng
    restart: unless-stopped
    healthcheck:
      test: ["CMD", "wget", "-qO-", "http://localhost:8080/healthz"]
      interval: 10s
      timeout: 5s
      retries: 6

  crawl4ai:
    # Pinned per the global constraint — never `latest`. Bump deliberately.
    image: unclecode/crawl4ai:<CRAWL4AI_TAG>
    container_name: teck-crawl4ai
    ports:
      - "11235:11235"
    # Chromium needs more than Docker's 64MB default /dev/shm or it crashes
    # on non-trivial pages.
    shm_size: 1gb
    restart: unless-stopped
    healthcheck:
      test: ["CMD", "curl", "-fsS", "http://localhost:11235/health"]
      interval: 10s
      timeout: 5s
      retries: 12

volumes:
  searxng-config:
```

- [ ] **Step 4: Create the start script**

Create `.devcontainer/start-mcp.sh` (mode `755`):

```bash
#!/usr/bin/env bash
# Bring up the self-hosted MCP backends (searxng, crawl4ai) via docker compose.
# Invoked by the devcontainer `postStartCommand`, so it runs on EVERY container
# start (through docker-in-docker).
#
# Deliberately failure-tolerant, mirroring start-litellm.sh: these are optional
# research backends and must never block the container from coming up.
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE_FILE="$SCRIPT_DIR/mcp/compose.yaml"

if ! docker compose version >/dev/null 2>&1; then
  echo "WARN: MCP: 'docker compose' not available; skipping (continuing)."
  exit 0
fi

echo "==> MCP backends: docker compose up -d"
docker compose -f "$COMPOSE_FILE" up -d \
  || { echo "WARN: MCP: 'docker compose up' failed (continuing)."; exit 0; }

echo "==> MCP backends: waiting for health ..."
for _ in $(seq 1 60); do
  if curl -fsS "http://localhost:8888/healthz" >/dev/null 2>&1 \
     && curl -fsS "http://localhost:11235/health" >/dev/null 2>&1; then
    echo "==> MCP backends: searxng :8888 and crawl4ai :11235 healthy"
    exit 0
  fi
  sleep 2
done

echo "WARN: MCP backends did not report healthy within 120s. Recent logs:"
docker compose -f "$COMPOSE_FILE" logs --tail 20 2>&1 || true
echo "     (continuing; images may still be pulling on first start)"
exit 0
```

- [ ] **Step 5: Chain the start script into `devcontainer.json`**

Change:

```json
  "postStartCommand": "bash .devcontainer/start-litellm.sh",
```

to:

```json
  "postStartCommand": "bash .devcontainer/start-litellm.sh && bash .devcontainer/start-mcp.sh",
```

Add `8888` and `11235` to `forwardPorts`, and to `portsAttributes`:

```json
    "8888": { "label": "SearXNG" },
    "11235": { "label": "crawl4ai" },
```

- [ ] **Step 6: Verify script syntax and compose validity**

```bash
bash -n .devcontainer/start-mcp.sh && echo "syntax OK"
chmod 755 .devcontainer/start-mcp.sh
docker compose -f .devcontainer/mcp/compose.yaml config >/dev/null && echo "compose OK"
node -e "JSON.parse(require('fs').readFileSync('.devcontainer/devcontainer.json','utf8').replace(/^\s*\/\/.*$/gm,'')); console.log('devcontainer.json OK')"
grep -c '<SEARXNG_TAG>\|<CRAWL4AI_TAG>\|<VERSION>\|:latest' .devcontainer/mcp/compose.yaml .devcontainer/opencode/opencode.json
```

Expected: `syntax OK`, `compose OK`, `devcontainer.json OK`, and `0` for both files on the last check — unsubstituted tokens or a `latest` tag violate the global pinning constraint.

- [ ] **Step 7: Bring the stack up and confirm health**

```bash
bash .devcontainer/start-mcp.sh
docker compose -f .devcontainer/mcp/compose.yaml ps
```

Expected: both containers `running (healthy)`.

- [ ] **Step 8: Verify searxng actually returns JSON**

```bash
curl -fsS "http://localhost:8888/search?q=wolverinefx&format=json" | head -c 200
```

Expected: JSON, not an error. A `403`/HTML response means `SEARXNG_SEARCH_FORMATS` didn't apply — fix before wiring the MCP.

- [ ] **Step 9: Verify LiteLLM survived the added load**

```bash
curl -fsS http://localhost:4000/health/liveliness >/dev/null && echo "litellm still healthy"
docker stats --no-stream --format '{{.Name}}\t{{.MemUsage}}' | head -10
```

Expected: `litellm still healthy`, and memory headroom remaining. This is the resource-contention check from the spec.

- [ ] **Step 10: Wire the MCP entries**

Add to `opencode.json`'s `mcp` block:

```json
    "searxng": {
      "type": "local",
      "command": ["npx", "-y", "mcp-searxng@<VERSION>"],
      "environment": { "SEARXNG_URL": "http://localhost:8888" },
      "enabled": true
    },
    "crawl4ai": {
      "type": "remote",
      "url": "http://localhost:11235/mcp/sse",
      "enabled": true
    }
```

Update the slim template's `mcps` arrays:

- `oracle.mcps`: `["searxng", "crawl4ai"]`
- `librarian.mcps`: `["context7", "github", "searxng", "crawl4ai"]`
- `fixer.mcps`: `["searxng", "crawl4ai"]`
- `orchestrator.mcps`: unchanged — `["*", "!context7", "!github"]`

- [ ] **Step 11: Re-run the dangling-reference guard from Task 8 Step 4**

Expected: `no dangling references`.

- [ ] **Step 12: Verify end-to-end and commit**

Restart OpenCode, then: `ask the oracle to research current WolverineFx outbox patterns`

Expected: it performs a real web search and returns cited results.

```bash
git add .devcontainer/
git commit -m "feat(devcontainer): add self-hosted searxng and crawl4ai MCP backends"
```

---

### Task 10: Phase 4 — codegraph (CONDITIONAL)

**This task may end in deliberate removal.** `codegraph`'s value depends on C# support, which is unverified. Do not force it in.

**Files:**
- Modify: `.devcontainer/opencode/opencode.json`, `.devcontainer/opencode/oh-my-opencode-slim.jsonc`
- Delete: `docs/superpowers/plans/2026-07-20-model-ids.md`

- [ ] **Step 1: Check language support BEFORE any wiring**

Research the codegraph MCP server's supported languages (README/docs). Determine explicitly: **does it index C#?**

- [ ] **Step 2: Decide, with the rule stated up front**

- **If C# is supported:** continue to Step 3.
- **If C# is NOT supported:** abandon the phase. `codegraph` would index only `src/apps` and `src/packages` while the .NET services — the bulk of the repo — stay invisible, giving explorer a misleading partial graph. Record the finding in the spec's phase-4 section, ensure no `mcps` array mentions `codegraph`, re-run the dangling-reference guard, and skip to Step 6.

- [ ] **Step 3: Wire it (only if C# is supported)**

Add to `opencode.json`'s `mcp` block, then set:

- `explorer.mcps`: `["codegraph"]`
- `designer.mcps`: `["codegraph"]`
- `fixer.mcps`: `["searxng", "crawl4ai", "codegraph"]`
- `oracle.mcps`: `["searxng", "crawl4ai", "codegraph"]`

- [ ] **Step 4: Re-run the dangling-reference guard from Task 8 Step 4**

Expected: `no dangling references`.

- [ ] **Step 5: Verify on BOTH languages**

Ask the explorer to locate a C# symbol (e.g. `OrderDbContext`) and a TypeScript symbol (e.g. an export from `src/packages/api-client`).

Expected: correct results for **both**. If C# fails in practice despite claimed support, revert per Step 2's removal path — claimed support isn't evidence.

- [ ] **Step 6: Clean up the scratch record**

```bash
git rm docs/superpowers/plans/2026-07-20-model-ids.md
```

The resolved model IDs are now committed in the slim template itself; the scratch file was a Task 1→2 handoff only. Before deleting, copy the `## Phase 1 verification` results into the spec if you want them retained long-term.

- [ ] **Step 7: Final full-stack verification**

```bash
curl -fsS http://localhost:4000/health/liveliness >/dev/null && echo "litellm OK"
curl -fsS http://localhost:4747 >/dev/null && echo "opencode-mem OK"
docker compose -f .devcontainer/mcp/compose.yaml ps
grep -rn 'oh-my-openagent' .devcontainer/ ; echo "omo refs exit=$?"
```

Expected: both OK lines, containers healthy, `omo refs exit=1`.

Then in OpenCode: `ping all agents` — all seven respond.

- [ ] **Step 8: Commit**

```bash
git add -A .devcontainer/ docs/superpowers/
git commit -m "feat(devcontainer): finalize slim MCP wiring and remove migration scratch notes"
```

---

## Deviations from the upstream author's preset

Recorded here because each is a deliberate departure a reviewer might otherwise read as a mistake:

- **D1 — Gemini substituted** (`designer`, `fixer` → `glm-5.2`, `kimi-k2.7-code`). The only Gemini route available is capped at 20 requests/**day**, single-route, no fallback.
- **D2 — companion disabled.** It's a desktop GUI app; this container is headless.
- **D3 — `main-horizontal`, not `main-vertical`.** Tuned for a right-docked VS Code terminal panel, preserving the tuning already documented in `.devcontainer/README.md` for omo.
- **D4 — MCP exclusion list rewritten.** The author's `["*", "!context7", "!gh_app", "!websearch"]` names his private GitHub App server and a `websearch` server we don't adopt. Ours is `["*", "!context7", "!github"]`.
- **D5 — prompt appends are project-local, not global.** Committed under `.opencode/oh-my-opencode-slim/` rather than seeded into `~/.config/opencode/`, so they're version-controlled and need no seeding step.
