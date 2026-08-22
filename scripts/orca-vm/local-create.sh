#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/local-common.sh"

name=""
runtime_dir=""
created_this_attempt=0
cleanup_on_error() {
  if [ "$?" -ne 0 ] && [ "$created_this_attempt" = 1 ] && [ -n "$name" ]; then
    containers="$(docker ps -aq --filter "label=com.docker.compose.project=$name")"
    [ -z "$containers" ] || docker rm -f $containers >/dev/null 2>&1 || true
    [ -z "$runtime_dir" ] || rm -rf -- "$runtime_dir"
  fi
}
trap cleanup_on_error EXIT

[ -s "$orca_codex_auth_file" ] || { echo "Codex credential file missing: $orca_codex_auth_file" >&2; exit 1; }
ensure_key
identity_file="$(wslpath -w "$orca_key_file")"

raw_name="orca-${ORCA_RECIPE_ID:-${ORCA_VM_RECIPE_ID:-local}}-${ORCA_VM_INSTANCE_ID:-workspace}"
name_prefix="$(printf '%s' "$raw_name" | tr '[:upper:]' '[:lower:]' | tr -cs 'a-z0-9_-' '-' | cut -c1-52)"
name="${name_prefix%-}"
runtime_dir="$orca_runtime_state_root/$name"
workspace_dir="$runtime_dir/workspace"
if [ -d "$runtime_dir" ]; then
  validate_runtime_dir "$runtime_dir"
  containers="$(docker ps -aq --filter "label=com.docker.compose.project=$name")"
  [ -n "$containers" ] || { echo "Runtime state exists without containers: $runtime_dir" >&2; exit 1; }
  docker start $containers >/dev/null
  emit_workspace_recipe_result "$name" "$runtime_dir" "$identity_file"
  trap - EXIT
  exit 0
fi
install -d -m 0700 "$runtime_dir"
created_this_attempt=1

# Each parent feature receives its own checkout. Runtime infrastructure comes
# from the latest main branch rather than the requested worktree base: an old
# feature branch must not boot obsolete credentials, mounts, or permissions.
# Orca fetches and checks out ORCA_REPO_REF after connecting to this runtime.
git clone --no-checkout "$orca_repo_root" "$workspace_dir" >&2
source_ref="${ORCA_ENVIRONMENT_REF:-origin/main}"
if [ -z "${ORCA_ENVIRONMENT_REF:-}" ]; then
  git -C "$orca_repo_root" fetch origin refs/heads/main:refs/remotes/origin/main >&2
fi
source_commit="$(git -C "$orca_repo_root" rev-parse --verify "${source_ref}^{commit}")"
git -C "$workspace_dir" checkout --detach "$source_commit" >&2
git -C "$workspace_dir" remote set-url origin "${ORCA_REPO_URL:-$(git -C "$orca_repo_root" remote get-url origin)}"

ssh_port="$(node -e 'const net=require("net");const s=net.createServer();s.listen(0,"127.0.0.1",()=>{console.log(s.address().port);s.close()})')"
runtime_override="$runtime_dir/compose.runtime.json"
jq -n --arg runtimeDir "$runtime_dir" --arg sshKey "$(<"$orca_key_file.pub")" --arg sshPort "$ssh_port" \
  '{services:{workspace:{restart:"unless-stopped",entrypoint:["/usr/local/share/docker-init.sh","/usr/local/bin/orca-docker-ssh-entrypoint"],
      ports:[("0.0.0.0:"+$sshPort+":22")],labels:{"teck.orca.runtime-state-dir":$runtimeDir},
      environment:{ORCA_SSH_PUBLIC_KEY:$sshKey}}}}' > "$runtime_override"
chmod 600 "$runtime_override"

# Keep the generated config inside the checkout's .devcontainer directory and
# retain the required devcontainer.json filename. Local Feature paths resolve
# relative to this nested config, so shift them back to the source feature dir.
runtime_config_dir="$workspace_dir/.devcontainer/.orca-runtime"
runtime_config="$runtime_config_dir/devcontainer.json"
install -d "$runtime_config_dir"
jq --arg base "$workspace_dir/.devcontainer" --arg override "$runtime_override" \
  '.dockerComposeFile = [($base + "/compose.yaml"), ($base + "/mcp/compose.yaml"), $override]
   | .features |= with_entries(.key |= sub("^\\./features/"; "../features/"))' \
  "$workspace_dir/.devcontainer/devcontainer.json" > "$runtime_config"

export COMPOSE_PROJECT_NAME="$name"
export TECK_MCP_ENV_FILE="$workspace_dir/.devcontainer/mcp/mcp.env"
up_result="$(npx --yes @devcontainers/cli@0.88.0 up --workspace-folder "$workspace_dir" --config "$runtime_config")"
container_id="$(jq -er '.containerId' <<<"$up_result")"

emit_workspace_recipe_result "$name" "$runtime_dir" "$identity_file"
trap - EXIT
