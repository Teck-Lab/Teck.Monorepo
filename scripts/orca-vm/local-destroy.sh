#!/usr/bin/env bash
set -euo pipefail
payload="$(cat)"
resource_id="$(jq -er '.recipeResult.userData.resourceId // empty' <<<"$payload" 2>/dev/null || true)"
runtime_dir="$(jq -er '.recipeResult.userData.runtimeDir // empty' <<<"$payload" 2>/dev/null || true)"
[ -n "$resource_id" ] || { echo 'No Dev Container resource id in lifecycle payload.' >&2; exit 1; }
state_root="${XDG_STATE_HOME:-$HOME/.local/state}/teck-orca/runtimes"
case "$runtime_dir" in "$state_root/"*) ;; *) echo "Refusing unexpected runtime path: $runtime_dir" >&2; exit 1 ;; esac

[ -s "$runtime_dir/workspace/.devcontainer/.orca-runtime/devcontainer.json" ] \
  && [ -d "$runtime_dir/workspace" ] || {
  echo 'Dev Container metadata missing; refusing an unscoped cleanup.' >&2
  exit 1
}
# The official CLI creates containers with Compose labels but has no `down`
# command. Remove only resources carrying this recipe's exact project label.
containers="$(docker ps -aq --filter "label=com.docker.compose.project=$resource_id")"
[ -z "$containers" ] || docker rm -f $containers >/dev/null
volumes="$(docker volume ls -q --filter "label=com.docker.compose.project=$resource_id")"
[ -z "$volumes" ] || docker volume rm $volumes >/dev/null
networks="$(docker network ls -q --filter "label=com.docker.compose.project=$resource_id")"
[ -z "$networks" ] || docker network rm $networks >/dev/null
rm -rf -- "$runtime_dir"
