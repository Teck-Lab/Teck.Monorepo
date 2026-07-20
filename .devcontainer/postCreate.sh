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
if command -v claude >/dev/null 2>&1; then
  claude plugin marketplace add anthropics/claude-plugins-official || echo "WARN: marketplace add (official) failed (continuing)"
  claude plugin marketplace add jarrodwatts/claude-hud || echo "WARN: marketplace add (claude-hud) failed (continuing)"
  for p in superpowers microsoft-docs typescript-lsp security-guidance playwright frontend-design csharp-lsp github; do
    claude plugin install "$p@claude-plugins-official" || echo "WARN: install $p failed (continuing)"
  done
  claude plugin install claude-hud@claude-hud || echo "WARN: install claude-hud failed (continuing)"

  echo "==> Seeding claude-hud display config"
  mkdir -p "$CLAUDE_DIR/plugins/claude-hud"
  cp .devcontainer/claude-hud-config.json "$CLAUDE_DIR/plugins/claude-hud/config.json" || echo "WARN: could not seed claude-hud config (continuing)"
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
# oh-my-opencode-slim agent config. The plugin itself is declared in opencode.json's
# `plugin` array and auto-installs via Bun on the first `opencode` launch; this file
# pins each agent's model so the upstream install TUI is never needed. The committed
# template is the source of truth — do NOT run `bunx oh-my-opencode-slim install`,
# which is interactive and refuses to overwrite an existing config anyway.
cp .devcontainer/opencode/oh-my-opencode-slim.jsonc "$HOME/.config/opencode/oh-my-opencode-slim.jsonc" || echo "WARN: could not seed slim config (continuing)"
# opencode-mem config: enables auto-capture through the litellm gateway, stores
# memories under the persisted ~/.local/share/opencode volume, and serves the
# memory web UI on :4747. The plugin itself auto-installs via opencode.json.
cp .devcontainer/opencode/opencode-mem.jsonc "$HOME/.config/opencode/opencode-mem.jsonc" || echo "WARN: could not seed opencode-mem config (continuing)"

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

echo "==> Exposing GITHUB_TOKEN (from gh) to OpenCode via ~/.bashrc"
# opencode.json references it as {env:GITHUB_TOKEN} for the github MCP's
# Authorization header. Read live from `gh auth token` rather than copied, so it
# tracks re-auth automatically and no token is written to a file. ~/.config/gh is
# on its own volume, so the login itself survives rebuilds.
GH_LINE='command -v gh >/dev/null 2>&1 && export GITHUB_TOKEN="$(gh auth token 2>/dev/null)"'
if ! grep -qxF "$GH_LINE" "$HOME/.bashrc" 2>/dev/null; then
  printf '\n# Expose the GitHub token to OpenCode (github MCP)\n%s\n' "$GH_LINE" >> "$HOME/.bashrc"
fi

echo "==> Exposing CRAWL4AI_API_TOKEN to OpenCode via ~/.bashrc"
# opencode.json references it as {env:CRAWL4AI_API_TOKEN} for the crawl4ai MCP's
# Authorization header. Same dynamic-load pattern as the gateway key above: the
# file is generated by start-mcp.sh and may not exist yet at postCreate time,
# so the guard handles that per shell.
MCP_ENV_ABS="$(pwd)/.devcontainer/mcp/mcp.env"
C4_LINE="[ -f \"$MCP_ENV_ABS\" ] && export CRAWL4AI_API_TOKEN=\"\$(grep -E '^CRAWL4AI_API_TOKEN=' \"$MCP_ENV_ABS\" | cut -d= -f2-)\""
if ! grep -qF "$MCP_ENV_ABS" "$HOME/.bashrc" 2>/dev/null; then
  printf '\n# Expose the crawl4ai MCP token to OpenCode\n%s\n' "$C4_LINE" >> "$HOME/.bashrc"
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

echo "==> Setting up GPG commit signing from the mounted host keyring"
# WHY THIS EXISTS: signing kept dying. The container never held key material —
# it relied on VS Code's *implicit* gpg-agent forwarding to the host, an
# undeclared socket bind that drops on window reloads, container restarts, host
# agent exit, and WSL2 sleep/resume. When it drops, a LOCAL keyless agent answers
# on the same socket path, so gpg reports "No secret key" instead of a connection
# error — it looks like the key vanished. ~/.gnupg was also not persisted, so a
# rebuild wiped it.
#
# FIX: the host keyring is bind-mounted READ-ONLY at ~/.gnupg-host and copied to
# ~/.gnupg here. The agent then runs INSIDE the container against real local key
# material — no forwarding, nothing to disconnect.
#
# Why copy instead of using the mount directly: gpg demands 0700 on its home and
# writes sockets/trustdb/random_seed at runtime, so a read-only mount can't serve
# as GNUPGHOME. Mounting read-WRITE instead would let this container corrupt the
# host keyring, so we don't. The copy is ephemeral and refreshed every rebuild;
# the host keyring stays the single source of truth.
if [ -d "$HOME/.gnupg-host" ]; then
  mkdir -p "$HOME/.gnupg"
  cp -r "$HOME/.gnupg-host/." "$HOME/.gnupg/" 2>/dev/null || echo "WARN: partial GPG keyring copy (continuing)"
  # Stale sockets copied from the host point at an agent that isn't ours.
  rm -f "$HOME/.gnupg"/S.gpg-agent* 2>/dev/null
  chmod 700 "$HOME/.gnupg"
  find "$HOME/.gnupg" -type d -exec chmod 700 {} + 2>/dev/null
  find "$HOME/.gnupg" -type f -exec chmod 600 {} + 2>/dev/null

  # Cache the passphrase for the container's whole life (400 days) so a long
  # unattended agent run isn't interrupted by a re-prompt mid-session.
  # allow-loopback-pinentry lets gpg prompt without a GUI pinentry present.
  cat > "$HOME/.gnupg/gpg-agent.conf" <<'AGENT_EOF'
default-cache-ttl 34560000
max-cache-ttl 34560000
allow-loopback-pinentry
AGENT_EOF
  echo 'pinentry-mode loopback' > "$HOME/.gnupg/gpg.conf"
  chmod 600 "$HOME/.gnupg/gpg-agent.conf" "$HOME/.gnupg/gpg.conf"
  gpgconf --kill gpg-agent >/dev/null 2>&1 || true

  # Prove a secret key is actually reachable, rather than assuming. This is the
  # exact check that silently failed before: a keyless agent answers happily.
  if gpg --list-secret-keys >/dev/null 2>&1 && [ -n "$(gpg --list-secret-keys --with-colons 2>/dev/null | grep '^sec')" ]; then
    echo "    secret key present: $(gpg --list-secret-keys --keyid-format=long --with-colons 2>/dev/null | awk -F: '/^sec/{print $5; exit}')"
  else
    echo "WARN: host keyring mounted but NO SECRET KEY is reachable — commits will fail."
    echo "      On the HOST run: gpg -K   and confirm a 'sec' line exists in ~/.gnupg."
    echo "      If the key lives on a smartcard/YubiKey it cannot be mounted this way."
  fi
else
  echo "WARN: no host keyring at ~/.gnupg-host — commits will FAIL (commit.gpgsign is true)."
  echo "      The bind mount in devcontainer.json expects ~/.gnupg to exist on the host."
fi

echo "==> postCreate complete"
