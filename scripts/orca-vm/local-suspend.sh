#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/local-common.sh"

payload="$(cat)"
resource_id="$(recipe_payload_value "$payload" '.recipeResult.userData.resourceId')"
runtime_dir="$(recipe_payload_value "$payload" '.recipeResult.userData.runtimeDir')"
[ -n "$resource_id" ] || { echo 'No Dev Container resource id in lifecycle payload.' >&2; exit 1; }
validate_runtime_dir "$runtime_dir"
containers="$(docker ps -q --filter "label=com.docker.compose.project=$resource_id")"
[ -z "$containers" ] || docker stop $containers >/dev/null
