#!/usr/bin/env bash
set -euo pipefail
payload="$(cat)"
resource_id="$(jq -er '.recipeResult.userData.resourceId // empty' <<<"$payload" 2>/dev/null || true)"
[ -n "$resource_id" ] || { echo 'No Docker Compose project id in lifecycle payload.' >&2; exit 1; }
workspace_id="$(docker ps -aq --filter "label=com.docker.compose.project=$resource_id" --filter 'label=com.docker.compose.service=workspace' | head -1)"
runtime_secrets_dir="$(docker inspect --format '{{index .Config.Labels "teck.orca.runtime-secrets-dir"}}' "$workspace_id" 2>/dev/null || true)"
containers="$(docker ps -aq --filter "label=com.docker.compose.project=$resource_id")"
[ -z "$containers" ] || docker rm -f $containers >/dev/null
volumes="$(docker volume ls -q --filter "label=com.docker.compose.project=$resource_id")"
[ -z "$volumes" ] || docker volume rm $volumes >/dev/null
networks="$(docker network ls -q --filter "label=com.docker.compose.project=$resource_id")"
[ -z "$networks" ] || docker network rm $networks >/dev/null
if [ -n "$runtime_secrets_dir" ]; then
  case "$runtime_secrets_dir" in
    /dev/shm/teck-orca-secrets.*) rm -rf -- "$runtime_secrets_dir" ;;
    *) echo "Refusing to remove unexpected runtime-secret path: $runtime_secrets_dir" >&2; exit 1 ;;
  esac
fi
