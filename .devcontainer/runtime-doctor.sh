#!/usr/bin/env bash
set -uo pipefail

failures=0
warnings=0

pass() { printf 'PASS  %s\n' "$1"; }
fail() { printf 'FAIL  %s\n' "$1"; failures=$((failures + 1)); }
warn() { printf 'WARN  %s\n' "$1"; warnings=$((warnings + 1)); }

for env_file in /run/secrets/teck-mcp/mcp.env; do
  if [ -s "$env_file" ]; then
    set -a
    # shellcheck disable=SC1090
    source "$env_file"
    set +a
  fi
done

for command_name in git jq curl claude codex omx orca bun dotnet python3 make g++ docker; do
  command -v "$command_name" >/dev/null 2>&1 \
    && pass "$command_name is installed" \
    || fail "$command_name is missing"
done

if python3 -c 'import shlex' >/dev/null 2>&1; then
  pass 'Python standard library supports node-gyp'
else
  fail 'Python standard library is incomplete (cannot import shlex)'
fi

if docker info >/dev/null 2>&1; then
  pass 'Docker-in-Docker daemon is available'
else
  fail 'Docker CLI cannot reach the Docker-in-Docker daemon'
fi

[ -s "$HOME/.codex/auth.json" ] \
  && pass 'Codex authentication is mounted' \
  || fail 'Codex authentication is missing'
if [ -s "$HOME/.claude/.credentials.json" ] && [ -s "$HOME/.claude.json" ]; then
  pass 'Claude authentication is mounted from WSL2'
else
  warn 'Claude authentication mounts are missing; use the Codex Sol/high coordinator fallback'
fi
if [ -d "$HOME/.claude" ] && [ -w "$HOME/.claude" ]; then
  pass 'Claude config directory is writable for transcripts and resume state'
else
  fail 'Claude config directory is not writable; transcript persistence will fail with EACCES'
fi
if gh auth status >/dev/null 2>&1; then
  pass 'GitHub CLI authentication works'
else
  fail 'GitHub CLI authentication failed'
fi

if [ "$(git config user.name 2>/dev/null || true)" = 'CptPowerTurtle' ] \
  && [ "$(git config user.email 2>/dev/null || true)" = 'jl@tecklab.dk' ]; then
  pass 'Git checkpoint identity is configured'
else
  fail 'Git checkpoint identity is missing or incorrect'
fi

[ "$(git config credential.https://github.com.helper 2>/dev/null || true)" = '!gh auth git-credential' ] \
  && pass 'Git transport uses GitHub CLI credentials' \
  || fail 'GitHub CLI credential helper is not configured'

if omx --version 2>/dev/null | grep -q '0\.20\.5'; then
  pass 'Oh My Codex 0.20.5 is installed'
else
  fail 'Oh My Codex 0.20.5 is not installed'
fi

if omx doctor >/dev/null 2>&1; then
  pass 'Oh My Codex setup is healthy'
else
  fail 'Oh My Codex doctor reported an unhealthy setup'
fi

if awk '/^\[agents\]$/{section=1; next} /^\[/{section=0} section && /^enabled = true$/{found=1} END{exit !found}' \
    "$HOME/.codex/config.toml" 2>/dev/null \
  && grep -q 'issue_dependency_read' "$HOME/.codex/config.toml" \
  && grep -q 'issue_dependency_write' "$HOME/.codex/config.toml" \
  && grep -q -- '--features issue_dependencies' /usr/local/bin/teck-github-mcp; then
  pass 'Codex multi-agent and GitHub dependency tools are configured'
else
  fail 'Codex multi-agent or GitHub dependency tools are missing'
fi

curl -fsS --max-time 5 "${SEARXNG_URL:-http://searxng:8080}/healthz" >/dev/null 2>&1 \
  && pass 'SearXNG is healthy' \
  || fail 'SearXNG health check failed'
curl -fsS --max-time 5 -H "Authorization: Bearer ${CRAWL4AI_API_TOKEN:-}" \
  http://crawl4ai:11235/health >/dev/null 2>&1 \
  && pass 'Crawl4AI is healthy' \
  || fail 'Crawl4AI health check failed'

if git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  pass 'Workspace is a Git repository'
else
  fail 'Current directory is not a Git repository'
fi

if [ "$(git config --bool commit.gpgsign 2>/dev/null || true)" = true ]; then
  signing_key="$(git config user.signingkey 2>/dev/null || true)"
  if [ -n "$signing_key" ] && gpg --list-secret-keys "$signing_key" >/dev/null 2>&1; then
    pass 'Git commit signing is configured and the private key is available'
  else
    warn 'Git requires signed commits but its configured private key is unavailable'
  fi
else
  pass 'Local checkpoint commits are intentionally unsigned'
fi

printf '\nRuntime doctor: %d failure(s), %d warning(s)\n' "$failures" "$warnings"
[ "$failures" -eq 0 ]
