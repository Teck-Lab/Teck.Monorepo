#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/local-common.sh"

payload="$(cat)"
resource_id="$(jq -er '.recipeResult.userData.resourceId // empty' <<<"$payload" 2>/dev/null || true)"
runtime_dir="$(jq -er '.recipeResult.userData.runtimeDir // empty' <<<"$payload" 2>/dev/null || true)"
[ -n "$resource_id" ] || { echo 'No Dev Container resource id in lifecycle payload.' >&2; exit 1; }
validate_runtime_dir "$runtime_dir"
# The official CLI creates containers with Compose labels but has no `down`
# command. Remove only resources carrying this recipe's exact project label.
containers="$(docker ps -aq --filter "label=com.docker.compose.project=$resource_id")"
[ -z "$containers" ] || docker rm -f $containers >/dev/null
volumes="$(docker volume ls -q --filter "label=com.docker.compose.project=$resource_id")"
[ -z "$volumes" ] || docker volume rm $volumes >/dev/null
networks="$(docker network ls -q --filter "label=com.docker.compose.project=$resource_id")"
[ -z "$networks" ] || docker network rm $networks >/dev/null
rm -rf -- "$runtime_dir"
