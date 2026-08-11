#!/usr/bin/env bash
set -euo pipefail

/usr/local/bin/teck-proton-bootstrap

load_env_file() {
  local file="$1"
  [ -s "$file" ] || return 0
  set -a
  # Files are generated locally or materialized from Proton Pass and mounted
  # read-only. They contain simple NAME=value assignments, never shell code.
  # shellcheck disable=SC1090
  source "$file"
  set +a
}

load_env_file /run/secrets/teck-ai/providers.env
load_env_file /run/secrets/teck-mcp/mcp.env

if [ "$#" -eq 0 ]; then
  exec sleep infinity
fi
exec "$@"
