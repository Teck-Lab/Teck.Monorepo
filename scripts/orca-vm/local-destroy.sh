#!/usr/bin/env bash
set -euo pipefail
payload="$(cat)"
resource_id="$(python3 -c 'import json,sys; d=json.loads(sys.argv[1]); print(d.get("recipeResult",{}).get("userData",{}).get("resourceId",""),end="")' "$payload")"
[ -n "$resource_id" ] || { echo 'No Docker container id in lifecycle payload.' >&2; exit 1; }
runtime_secrets_dir="$(docker inspect --format '{{index .Config.Labels "teck.orca.runtime-secrets-dir"}}' "$resource_id" 2>/dev/null || true)"
docker rm -f "$resource_id" >/dev/null
if [ -n "$runtime_secrets_dir" ]; then
  case "$runtime_secrets_dir" in
    /dev/shm/teck-orca-secrets.*) rm -rf -- "$runtime_secrets_dir" ;;
    *) echo "Refusing to remove unexpected runtime-secret path: $runtime_secrets_dir" >&2; exit 1 ;;
  esac
fi
