#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/local-common.sh"

payload="$(cat)"
resource_id="$(recipe_payload_value "$payload" '.recipeResult.userData.resourceId')"
runtime_dir="$(recipe_payload_value "$payload" '.recipeResult.userData.runtimeDir')"
schema_version="$(recipe_payload_value "$payload" '.recipeResult.schemaVersion')"
[ "$schema_version" = 1 ] || schema_version=2
[ -n "$resource_id" ] || { echo 'No Dev Container resource id in lifecycle payload.' >&2; exit 1; }
validate_runtime_dir "$runtime_dir"
ensure_key
identity_file="$(wslpath -w "$orca_key_file")"
containers="$(docker ps -aq --filter "label=com.docker.compose.project=$resource_id")"
[ -n "$containers" ] || { echo "No containers found for $resource_id" >&2; exit 1; }
docker start $containers >/dev/null
emit_workspace_recipe_result "$resource_id" "$runtime_dir" "$identity_file" "$schema_version"
