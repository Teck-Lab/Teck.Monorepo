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
