#!/usr/bin/env bash
# One-time setup after the dev container is built. Runs as the `vscode` user
# with the repository as the working directory.
#
# NOTE: this script is intentionally tolerant of a partially-building baseline
# (the monorepo does not fully compile yet). Dependency-restore failures are
# reported but do NOT fail the container build, so the environment still comes
# up and an engineer can fix things from inside it.
set -uo pipefail

SETUP_CACHE_DIR="${TECK_SETUP_CACHE_DIR:-/workspaces/.teck-devcontainer-cache}"
mkdir -p "$SETUP_CACHE_DIR"

fingerprint_files() {
  local file
  for file in "$@"; do
    [ -f "$file" ] && sha256sum "$file"
  done | sha256sum | cut -d' ' -f1
}

dotnet_fingerprint="$(git ls-files -z -- '*.csproj' '*.fsproj' '*.sln' '*.slnx' \
  Directory.Build.props Directory.Packages.props global.json nuget.config \
  | sort -z | xargs -0 -r sha256sum 2>/dev/null | sha256sum | cut -d' ' -f1)"
if [ -f "$SETUP_CACHE_DIR/dotnet-restore" ] \
  && [ "$(<"$SETUP_CACHE_DIR/dotnet-restore")" = "$dotnet_fingerprint" ]; then
  echo "==> .NET dependencies unchanged; reusing restored packages"
else
  echo "==> Restoring .NET dependencies (dotnet restore)"
  if dotnet restore; then
    printf '%s' "$dotnet_fingerprint" > "$SETUP_CACHE_DIR/dotnet-restore"
  else
    echo "WARN: dotnet restore reported errors (continuing; baseline may not fully build yet)"
  fi
fi

js_fingerprint="$(fingerprint_files package.json bun.lock)"
if [ -d node_modules ] && [ -f "$SETUP_CACHE_DIR/bun-install" ] \
  && [ "$(<"$SETUP_CACHE_DIR/bun-install")" = "$js_fingerprint" ]; then
  echo "==> JavaScript dependencies unchanged; reusing node_modules"
else
  echo "==> Installing JS workspace dependencies (bun install)"
  if bun install --frozen-lockfile; then
    printf '%s' "$js_fingerprint" > "$SETUP_CACHE_DIR/bun-install"
  else
    echo "WARN: bun install reported errors (continuing)"
  fi
fi

echo "==> Ensuring a local HTTPS development certificate exists"
dotnet dev-certs https || echo "WARN: could not create HTTPS dev cert (continuing)"

echo "==> Installing Claude Code plugins into the mounted Claude config volume"
# CLAUDE_CONFIG_DIR (set in devcontainer.json) relocates the whole Claude state
# tree onto the mounted volume. Fall back to ~/.claude if it's somehow unset so
# this script still works outside the dev container.
CLAUDE_DIR="${CLAUDE_CONFIG_DIR:-$HOME/.claude}"

# SELF-HEAL: the plugin registries store ABSOLUTE installPath/installLocation
# values, and they live on the persisted volume. If the config dir ever moves
# (as it did when CLAUDE_CONFIG_DIR was introduced), the registries travel with
# the volume still pointing at the OLD path — and every install then fails with
# "Source path does not exist: <old path>" while `claude plugin install` silently
# no-ops because the registry claims the plugin is already installed. Purging the
# two registry files makes them rebuild against the current path; the installs
# below repopulate them. Only triggers when a stale path is actually present.
MKT_REG="$CLAUDE_DIR/plugins/known_marketplaces.json"
PLG_REG="$CLAUDE_DIR/plugins/installed_plugins.json"
stale=0
if command -v jq >/dev/null 2>&1; then
  # Any recorded location that isn't under the CURRENT config dir is stale.
  [ -f "$MKT_REG" ] && [ "$(jq -r --arg d "$CLAUDE_DIR" \
      '[.[]?.installLocation // empty | select(startswith($d) | not)] | length' "$MKT_REG" 2>/dev/null || echo 0)" != "0" ] && stale=1
  [ -f "$PLG_REG" ] && [ "$(jq -r --arg d "$CLAUDE_DIR" \
      '[.plugins[]?[]?.installPath // empty | select(startswith($d) | not)] | length' "$PLG_REG" 2>/dev/null || echo 0)" != "0" ] && stale=1
fi
if [ "$stale" = "1" ]; then
  echo "    stale plugin registry paths detected — purging so they rebuild at $CLAUDE_DIR"
  rm -f "$MKT_REG" "$PLG_REG"
fi
# The committed .claude/settings.json declares these as enabled, but enabled
# plugins only auto-install behind an interactive trust prompt. Installing them
# explicitly here is headless and deterministic: `claude plugin install` just
# git-clones the (public) marketplaces into ~/.claude/plugins/, so it needs no
# auth and runs before first sign-in. Tolerant of failure so the container still
# comes up if GitHub is unreachable (the trust-prompt path remains as fallback).
CLAUDE_PLUGIN_SET='official:superpowers,microsoft-docs,typescript-lsp,security-guidance,playwright,frontend-design,csharp-lsp,github;claude-hud:claude-hud'
CLAUDE_PLUGIN_STAMP="$CLAUDE_DIR/plugins/.teck-plugin-set"
if command -v claude >/dev/null 2>&1 \
  && { [ ! -f "$CLAUDE_PLUGIN_STAMP" ] || [ "$(<"$CLAUDE_PLUGIN_STAMP")" != "$CLAUDE_PLUGIN_SET" ]; }; then
  claude plugin marketplace add anthropics/claude-plugins-official || echo "WARN: marketplace add (official) failed (continuing)"
  claude plugin marketplace add jarrodwatts/claude-hud || echo "WARN: marketplace add (claude-hud) failed (continuing)"
  for p in superpowers microsoft-docs typescript-lsp security-guidance playwright frontend-design csharp-lsp github; do
    claude plugin install "$p@claude-plugins-official" || echo "WARN: install $p failed (continuing)"
  done
  claude plugin install claude-hud@claude-hud || echo "WARN: install claude-hud failed (continuing)"
  mkdir -p "$(dirname "$CLAUDE_PLUGIN_STAMP")"
  printf '%s' "$CLAUDE_PLUGIN_SET" > "$CLAUDE_PLUGIN_STAMP"

  echo "==> Seeding claude-hud display config"
  mkdir -p "$CLAUDE_DIR/plugins/claude-hud"
  cp .devcontainer/claude-hud-config.json "$CLAUDE_DIR/plugins/claude-hud/config.json" || echo "WARN: could not seed claude-hud config (continuing)"
else
  command -v claude >/dev/null 2>&1 \
    && echo "==> Claude plugin set unchanged; reusing installed plugins" \
    || echo "WARN: 'claude' CLI not on PATH; skipping plugin install (continuing)"
fi

echo "==> Installing low-prompt 'claude' alias in ~/.bashrc"
ALIAS_LINE="alias claude='claude --dangerously-skip-permissions'"
if ! grep -qxF "$ALIAS_LINE" "$HOME/.bashrc" 2>/dev/null; then
  printf '\n# Convenience-first: run Claude Code without permission prompts inside the isolated container\n%s\n' "$ALIAS_LINE" >> "$HOME/.bashrc"
fi

echo "==> Seeding direct-provider agent CLI configs (Codex + OpenCode)"
# The committed templates are the source of truth; re-copied on every rebuild.
# OpenCode reads provider keys directly from the workspace environment; Codex
# uses the OpenAI authentication mounted from WSL2.
mkdir -p "$HOME/.codex" "$HOME/.config/opencode" "$HOME/.omo"
# Remove the retired alternate OpenCode profile from persistent config volumes.
# This path was created by earlier versions of this repository and otherwise
# survives container rebuilds indefinitely.
rm -rf "$HOME/.config/opencode/profiles/slim"
cp .devcontainer/codex/config.toml "$HOME/.codex/config.toml" || echo "WARN: could not seed codex config (continuing)"
cp .devcontainer/opencode/opencode.json "$HOME/.config/opencode/opencode.json" || echo "WARN: could not seed opencode config (continuing)"
cp .devcontainer/opencode/tui.json "$HOME/.config/opencode/tui.json" || echo "WARN: could not seed OpenCode TUI config (continuing)"
# Full OMO is the default worker harness. Its unified config is deliberately
# re-seeded from the repository so provider/model policy is reproducible while
# OpenCode auth remains in the persistent data volume.
cp .devcontainer/opencode/omo.jsonc "$HOME/.omo/omo.jsonc" || echo "WARN: could not seed OMO config (continuing)"
# opencode-mem config: enables auto-capture through a direct low-cost provider, stores
# memories under the persisted ~/.local/share/opencode volume, and serves the
# memory web UI on :4747. The plugin itself auto-installs via opencode.json.
cp .devcontainer/opencode/opencode-mem.jsonc "$HOME/.config/opencode/opencode-mem.jsonc" || echo "WARN: could not seed opencode-mem config (continuing)"

echo "==> Configuring Git identity and GitHub CLI transport"
git config --local user.name 'CptPowerTurtle'
git config --local user.email 'jl@tecklab.dk'
git config --local commit.gpgsign false
git config --local credential.https://github.com.helper '!gh auth git-credential'

echo "==> Enabling OpenCode background subagents"
# Full OMO can use visible background panes. teck-omo-worker also exports this
# explicitly per worker.
BG_LINE='export OPENCODE_EXPERIMENTAL_BACKGROUND_SUBAGENTS=true'
if ! grep -qxF "$BG_LINE" "$HOME/.bashrc" 2>/dev/null; then
  printf '\n# Required by OMO background orchestration\n%s\n' "$BG_LINE" >> "$HOME/.bashrc"
fi

SECRET_ENV_MARKER='# Load read-only Teck runtime secrets without printing them.'
if ! grep -qxF "$SECRET_ENV_MARKER" "$HOME/.bashrc" 2>/dev/null; then
  printf '\n%s\n%s\n' "$SECRET_ENV_MARKER" \
    'for teck_env in /run/secrets/teck-ai/providers.env /run/secrets/teck-mcp/mcp.env; do if [ -s "$teck_env" ]; then set -a; source "$teck_env"; set +a; fi; done; unset teck_env' \
    >> "$HOME/.bashrc"
fi

echo "==> Enabling persistent tmux for interactive Orca SSH terminals"
TMUX_MARKER='# Orca interactive SSH terminals resume the workspace tmux session.'
if ! grep -qxF "$TMUX_MARKER" "$HOME/.bashrc" 2>/dev/null; then
  printf '\n%s\n%s\n' "$TMUX_MARKER" \
    'if [ -n "${SSH_TTY:-}" ] && [ -z "${TMUX:-}" ] && command -v tmux >/dev/null 2>&1; then exec tmux new-session -A -s orca; fi' \
    >> "$HOME/.bashrc"
fi

echo "==> postCreate complete"
