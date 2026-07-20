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

echo "==> Ensuring a local HTTPS development certificate exists"
dotnet dev-certs https || echo "WARN: could not create HTTPS dev cert (continuing)"

echo "==> Installing Claude Code plugins into the mounted ~/.claude volume"
# The committed .claude/settings.json declares these as enabled, but enabled
# plugins only auto-install behind an interactive trust prompt. Installing them
# explicitly here is headless and deterministic: `claude plugin install` just
# git-clones the (public) marketplaces into ~/.claude/plugins/, so it needs no
# auth and runs before first sign-in. Tolerant of failure so the container still
# comes up if GitHub is unreachable (the trust-prompt path remains as fallback).
if command -v claude >/dev/null 2>&1; then
  claude plugin marketplace add anthropics/claude-plugins-official || echo "WARN: marketplace add (official) failed (continuing)"
  claude plugin marketplace add jarrodwatts/claude-hud || echo "WARN: marketplace add (claude-hud) failed (continuing)"
  for p in superpowers microsoft-docs typescript-lsp security-guidance playwright frontend-design csharp-lsp github; do
    claude plugin install "$p@claude-plugins-official" || echo "WARN: install $p failed (continuing)"
  done
  claude plugin install claude-hud@claude-hud || echo "WARN: install claude-hud failed (continuing)"

  echo "==> Seeding claude-hud display config"
  mkdir -p "$HOME/.claude/plugins/claude-hud"
  cp .devcontainer/claude-hud-config.json "$HOME/.claude/plugins/claude-hud/config.json" || echo "WARN: could not seed claude-hud config (continuing)"
else
  echo "WARN: 'claude' CLI not on PATH; skipping plugin install (continuing)"
fi

echo "==> Installing low-prompt 'claude' alias in ~/.bashrc"
ALIAS_LINE="alias claude='claude --dangerously-skip-permissions'"
if ! grep -qxF "$ALIAS_LINE" "$HOME/.bashrc" 2>/dev/null; then
  printf '\n# Convenience-first: run Claude Code without permission prompts inside the isolated container\n%s\n' "$ALIAS_LINE" >> "$HOME/.bashrc"
fi

echo "==> Seeding agent CLI configs (Codex + OpenCode) pointed at the LiteLLM gateway"
# The committed templates are the source of truth; re-copied on every rebuild.
# Both authenticate to the gateway via the LITELLM_MASTER_KEY env var (below), so
# neither needs an interactive login.
mkdir -p "$HOME/.codex" "$HOME/.config/opencode"
cp .devcontainer/codex/config.toml "$HOME/.codex/config.toml" || echo "WARN: could not seed codex config (continuing)"
cp .devcontainer/opencode/opencode.json "$HOME/.config/opencode/opencode.json" || echo "WARN: could not seed opencode config (continuing)"
# omo (oh-my-openagent) agent config. The plugin itself is declared in opencode.json's
# `plugin` array and auto-installs via Bun on the first `opencode` launch; this file
# just points its agents at the LiteLLM gateway so no guided-install TUI is needed.
cp .devcontainer/opencode/oh-my-openagent.json "$HOME/.config/opencode/oh-my-openagent.json" || echo "WARN: could not seed omo config (continuing)"
# opencode-mem config: enables auto-capture through the litellm gateway, stores
# memories under the persisted ~/.local/share/opencode volume, and serves the
# memory web UI on :4747. The plugin itself auto-installs via opencode.json.
cp .devcontainer/opencode/opencode-mem.jsonc "$HOME/.config/opencode/opencode-mem.jsonc" || echo "WARN: could not seed opencode-mem config (continuing)"
# Directive appended (via omo prompt_append) to the implementer agents/categories,
# telling them to use the superpowers skills — TDD in particular — through
# OpenCode's `skill` tool. The superpowers plugin itself auto-installs via opencode.json.
cp .devcontainer/opencode/superpowers-skills.md "$HOME/.config/opencode/superpowers-skills.md" || echo "WARN: could not seed superpowers directive (continuing)"

echo "==> Exposing LITELLM_MASTER_KEY to agent CLIs via ~/.bashrc"
# Codex (env_key) and OpenCode ({env:LITELLM_MASTER_KEY}) read the gateway key
# from the shell env. Load it dynamically from the gitignored key file so the
# single source of truth stays .devcontainer/litellm/litellm.env (which may not
# exist yet at postCreate time — the guard handles that per shell).
ENV_FILE_ABS="$(pwd)/.devcontainer/litellm/litellm.env"
KEY_LINE="[ -f \"$ENV_FILE_ABS\" ] && export LITELLM_MASTER_KEY=\"\$(grep -E '^LITELLM_MASTER_KEY=' \"$ENV_FILE_ABS\" | cut -d= -f2-)\""
if ! grep -qF "$ENV_FILE_ABS" "$HOME/.bashrc" 2>/dev/null; then
  printf '\n# Expose the LiteLLM gateway master key to agent CLIs (Codex/OpenCode)\n%s\n' "$KEY_LINE" >> "$HOME/.bashrc"
fi

echo "==> Enabling OpenCode background subagents (required by oh-my-opencode-slim)"
# Slim's default orchestration dispatches specialists as background subagents,
# which OpenCode gates behind this experimental flag. Without it the
# orchestrator silently runs everything inline and no multiplexer panes appear.
BG_LINE='export OPENCODE_EXPERIMENTAL_BACKGROUND_SUBAGENTS=true'
if ! grep -qxF "$BG_LINE" "$HOME/.bashrc" 2>/dev/null; then
  printf '\n# Required by oh-my-opencode-slim background orchestration\n%s\n' "$BG_LINE" >> "$HOME/.bashrc"
fi

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
      --port)   port="${args[i+1]}"; break ;;
    esac
  done
  if [ -z "$port" ]; then
    port="$(python3 -c 'import socket; s=socket.socket(); s.bind(("127.0.0.1",0)); print(s.getsockname()[1]); s.close()')" || return 1
    OPENCODE_PORT="$port" command opencode --port "$port" "$@"
  else
    OPENCODE_PORT="$port" command opencode "$@"
  fi
}
OMOS_EOF
fi

echo "==> postCreate complete"
