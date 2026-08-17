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

for command_name in git jq tmux curl codex opencode bun dotnet python3 make g++ docker; do
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
[ -s "$HOME/.local/share/opencode/auth.json" ] \
  && pass 'OpenCode authentication is mounted' \
  || fail 'OpenCode authentication is missing'

if [ -s /run/secrets/teck-ai/providers.env ]; then
  mode="$(stat -c '%a' /run/secrets/teck-ai/providers.env 2>/dev/null || true)"
  case "$mode" in
    400|440|600|640) pass 'Provider credentials are mounted with restricted permissions' ;;
    *) fail "Provider credential file mode is ${mode:-unknown}" ;;
  esac
else
  fail 'Provider credentials are not mounted'
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

omo_config="$HOME/.omo/omo.jsonc"
if [ -s "$omo_config" ]; then
  expected_sisyphus='["opencode-go-a/kimi-k2.7-code","opencode-go-b/kimi-k2.7-code","openai/gpt-5.6-sol"]'
  actual_sisyphus="$(jq -c '[."[opencode]".agents.sisyphus.models[] | if type == "string" then . else .model end]' "$omo_config" 2>/dev/null || true)"
  [ "$actual_sisyphus" = "$expected_sisyphus" ] \
    && pass 'Sisyphus has the expected Kimi K2.7-to-GPT fallback chain' \
    || fail 'Sisyphus fallback chain is missing or out of order'
  expected_deepseek_routes='["deepseek/deepseek-v4-flash","deepseek/deepseek-v4-flash","deepseek/deepseek-v4-flash"]'
  actual_deepseek_routes="$(jq -c '[
    ."[opencode]".agents.librarian.models[1].model,
    ."[opencode]".agents.explore.models[1].model,
    ."[opencode]".categories.quick.models[1].model
  ]' "$omo_config" 2>/dev/null || true)"
  [ "$actual_deepseek_routes" = "$expected_deepseek_routes" ] \
    && pass 'Direct DeepSeek V4 Flash backs supported utility routes' \
    || fail 'DeepSeek V4 Flash utility fallbacks are missing or use the wrong provider'
  unexpected="$(jq -r '[
    (."[opencode]".agents | to_entries[] | select(.key != "sisyphus") | .value | (.model?, .models[]?.model?)),
    (."[opencode]".categories | to_entries[] | .value | (.model?, .models[]?.model?))
  ] | .[] | select(startswith("openai/gpt-") | not) | select(. != "deepseek/deepseek-v4-flash")' "$omo_config" 2>/dev/null || true)"
  [ -z "$unexpected" ] \
    && pass 'Other orchestrated OMO routes remain GPT-only' \
    || fail 'An unsupported non-GPT OMO route is configured'
else
  fail 'OMO configuration is missing'
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

if [ -n "${SSH_TTY:-}" ]; then
  [ -n "${TMUX:-}" ] \
    && pass 'Interactive SSH terminal is attached to tmux' \
    || fail 'Interactive SSH terminal is not attached to tmux'
else
  pass 'Non-interactive session correctly skips tmux attachment'
fi

printf '\nRuntime doctor: %d failure(s), %d warning(s)\n' "$failures" "$warnings"
[ "$failures" -eq 0 ]
