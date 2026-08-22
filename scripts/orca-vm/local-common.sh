#!/usr/bin/env bash
set -euo pipefail

# `wsl.exe --exec` starts a non-login shell, so user-local CLIs are not
# guaranteed to be present on PATH even when they work interactively.
export PATH="$HOME/.local/bin:$PATH"
if ! command -v node >/dev/null 2>&1; then
  for node_bin in "$HOME"/.nvm/versions/node/*/bin; do
    [ -x "$node_bin/node" ] && export PATH="$node_bin:$PATH"
  done
fi

orca_vm_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
orca_repo_root="$(cd "$orca_vm_dir/../.." && pwd)"
# Lifecycle commands receive their recipe payload on stdin. Windows processes
# launched through WSL can otherwise inherit and consume that stream before the
# caller reads it, leaving suspend/resume/destroy with an empty payload.
orca_windows_profile="${ORCA_WINDOWS_PROFILE:-$(/mnt/c/Windows/System32/cmd.exe /d /c 'echo %USERPROFILE%' </dev/null 2>/dev/null | tr -d '\r')}"
orca_key_file="${ORCA_SSH_KEY_FILE:-$(wslpath -u "$orca_windows_profile")/.ssh/orca-teck-local-ed25519}"
orca_codex_auth_file="${ORCA_CODEX_AUTH_FILE:-$HOME/.codex/auth.json}"
orca_project_root="/workspaces/Teck.Monorepo"
orca_runtime_state_root="${XDG_STATE_HOME:-$HOME/.local/state}/teck-orca/runtimes"
orca_windows_ssh_command="${ORCA_WINDOWS_SSH_COMMAND:-/mnt/c/Windows/System32/OpenSSH/ssh.exe}"

ensure_key() {
  if [ ! -s "$orca_key_file" ]; then
    mkdir -p "$(dirname "$orca_key_file")"
    /mnt/c/Windows/System32/OpenSSH/ssh-keygen.exe -q -t ed25519 -N '' \
      -f "$(wslpath -w "$orca_key_file")" -C 'orca-local-workspace'
  fi
}

recipe_payload_value() {
  local payload="$1" query="$2"
  jq -er "$query // empty" <<<"$payload" 2>/dev/null || true
}

validate_runtime_dir() {
  local runtime_dir="$1"
  case "$runtime_dir" in
    "$orca_runtime_state_root"/*) ;;
    *) echo "Refusing unexpected runtime path: $runtime_dir" >&2; return 1 ;;
  esac
  [ -s "$runtime_dir/workspace/.devcontainer/.orca-runtime/devcontainer.json" ] \
    && [ -d "$runtime_dir/workspace" ] || {
    echo 'Dev Container metadata missing; refusing an unscoped lifecycle action.' >&2
    return 1
  }
}

workspace_container_id() {
  local resource_id="$1"
  docker ps -aq \
    --filter "label=com.docker.compose.project=$resource_id" \
    --filter 'label=com.docker.compose.service=workspace' | head -1
}

wait_for_workspace_ssh() {
  local container_id="$1" port="$2" identity_file="$3" ssh_ready=0
  for _ in $(seq 1 45); do
    if "$orca_windows_ssh_command" -o BatchMode=yes -o ConnectTimeout=1 \
        -o StrictHostKeyChecking=no -o UserKnownHostsFile=NUL -o LogLevel=ERROR \
        -o IdentitiesOnly=yes -i "$identity_file" -p "$port" vscode@127.0.0.1 true >/dev/null 2>&1; then
      ssh_ready=1
      break
    fi
    sleep 1
  done
  [ "$ssh_ready" = 1 ] || {
    docker logs "$container_id" >&2 || true
    echo 'Workspace SSH transport did not become ready.' >&2
    return 1
  }
}

emit_workspace_recipe_result() {
  local resource_id="$1" runtime_dir="$2" identity_file="$3"
  local container_id port
  container_id="$(workspace_container_id "$resource_id")"
  [ -n "$container_id" ] || { echo "Workspace container missing for $resource_id" >&2; return 1; }
  port="$(docker port "$container_id" 22/tcp | sed -nE 's/.*:([0-9]+)$/\1/p' | head -1)"
  [ -n "$port" ] || { echo 'Could not resolve the published SSH port.' >&2; return 1; }
  wait_for_workspace_ssh "$container_id" "$port" "$identity_file"
  jq -cn --argjson port "$port" --arg key "$identity_file" --arg root "$orca_project_root" \
    --arg name "$resource_id" --arg runtime "$runtime_dir" \
    '{schemaVersion:1,connection:{type:"ssh",projectRoot:$root,target:{label:"Teck Dev Container",host:"127.0.0.1",port:$port,username:"vscode",identityFile:$key,identitiesOnly:true}},userData:{provider:"devcontainer-cli",resourceId:$name,runtimeDir:$runtime}}'
}

state_value() {
  local key="$1"
  [ -s "$orca_state_file" ] || return 0
  jq -er --arg key "$key" '.[$key] // empty' "$orca_state_file" 2>/dev/null || true
}

resolve_volume() {
  local env_name="$1" prefix="$2" configured="" matches=""
  configured="$(printenv "$env_name" 2>/dev/null || true)"
  if [ -n "$configured" ]; then
    docker volume inspect "$configured" >/dev/null 2>&1 || {
      echo "Docker volume from $env_name does not exist: $configured" >&2
      return 1
    }
    printf '%s' "$configured"
    return
  fi
  matches="$(docker volume ls --format '{{.Name}}' | awk -v p="$prefix" 'index($0,p)==1')"
  if [ "$(printf '%s\n' "$matches" | sed '/^$/d' | wc -l)" -ne 1 ]; then
    echo "Expected exactly one ${prefix}* volume. Set $env_name to the correct volume name." >&2
    return 1
  fi
  printf '%s' "$matches"
}
