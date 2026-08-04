#!/usr/bin/env bash
set -euo pipefail
payload="$(cat)"
resource_id="$(python3 -c 'import json,sys; d=json.loads(sys.argv[1]); print(d.get("recipeResult",{}).get("userData",{}).get("resourceId",""),end="")' "$payload")"
[ -n "$resource_id" ] || { echo 'No Docker container id in lifecycle payload.' >&2; exit 1; }
docker rm -f "$resource_id" >/dev/null
