#!/usr/bin/env bash
set -euo pipefail

orca_vm_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
orca_repo_root="$(cd "$orca_vm_dir/../.." && pwd)"
orca_state_file="$orca_vm_dir/local-state.json"
orca_windows_profile="$(/mnt/c/Windows/System32/cmd.exe /d /c 'echo %USERPROFILE%' 2>/dev/null | tr -d '\r')"
orca_key_file="${ORCA_SSH_KEY_FILE:-$(wslpath -u "$orca_windows_profile")/.ssh/orca-teck-local-ed25519}"
orca_codex_auth_file="${ORCA_CODEX_AUTH_FILE:-$HOME/.codex/auth.json}"
orca_base_image="teck-devcontainer:orca-base"
orca_project_root="/workspaces/Teck.Monorepo"

ensure_key() {
  if [ ! -s "$orca_key_file" ]; then
    mkdir -p "$(dirname "$orca_key_file")"
    /mnt/c/Windows/System32/OpenSSH/ssh-keygen.exe -q -t ed25519 -N '' \
      -f "$(wslpath -w "$orca_key_file")" -C 'orca-local-workspace'
  fi
}

git_token() {
  if [ -n "${GH_TOKEN:-}" ]; then printf '%s' "$GH_TOKEN"; return; fi
  if [ -n "${GITHUB_TOKEN:-}" ]; then printf '%s' "$GITHUB_TOKEN"; return; fi
  command -v gh >/dev/null 2>&1 && gh auth token 2>/dev/null || true
}

state_value() {
  local key="$1"
  [ -s "$orca_state_file" ] || return 0
  python3 -c 'import json,sys; d=json.load(open(sys.argv[1])); print(d.get(sys.argv[2], ""), end="")' "$orca_state_file" "$key"
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
