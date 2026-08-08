#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
settings="$script_dir/mcp/searxng/settings.yml"
mcp_env="$script_dir/mcp/mcp.env"

if [ ! -f "$settings" ]; then
  secret="$(head -c 32 /dev/urandom | base64 | tr -d '/+=' | head -c 32)"
  sed "s|__SECRET_KEY__|$secret|" "$script_dir/mcp/searxng/settings.template.yml" > "$settings"
fi

if [ ! -f "$mcp_env" ]; then
  token="$(head -c 32 /dev/urandom | base64 | tr -d '/+=' | head -c 32)"
  printf 'CRAWL4AI_API_TOKEN=%s\n' "$token" > "$mcp_env"
  chmod 600 "$mcp_env"
fi

provider_env="${AI_PROVIDER_ENV_FILE:-$script_dir/ai/providers.env}"
if [ ! -s "$provider_env" ] && command -v wslpath >/dev/null 2>&1 \
  && [ -s "$script_dir/github-app/proton-pass.env" ]; then
  # Reuse the Orca Proton loader so the PAT scope and item references have one
  # implementation. Ordinary devcontainers persist only the provider subset in
  # this gitignored 0600 file; Orca workspaces continue using tmpfs.
  source "$script_dir/../scripts/orca-vm/local-common.sh"
  prepare_runtime_secrets
  install -m 600 "$orca_ai_provider_env_file" "$provider_env"
  cleanup_runtime_secrets
fi
[ -s "$provider_env" ] || {
  echo "AI provider credentials are missing: $provider_env" >&2
  exit 1
}
