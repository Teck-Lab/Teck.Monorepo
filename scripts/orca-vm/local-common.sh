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
orca_windows_profile="$(/mnt/c/Windows/System32/cmd.exe /d /c 'echo %USERPROFILE%' 2>/dev/null | tr -d '\r')"
orca_key_file="${ORCA_SSH_KEY_FILE:-$(wslpath -u "$orca_windows_profile")/.ssh/orca-teck-local-ed25519}"
orca_codex_auth_file="${ORCA_CODEX_AUTH_FILE:-$HOME/.codex/auth.json}"
orca_opencode_auth_file="${ORCA_OPENCODE_AUTH_FILE:-$HOME/.local/share/opencode/auth.json}"
orca_provider_refs_file="${ORCA_PROVIDER_REFS_FILE:-$orca_vm_dir/providers.env}"
orca_proton_pat_file="${PROTON_PASS_PERSONAL_ACCESS_TOKEN_FILE:-${XDG_CONFIG_HOME:-$HOME/.config}/teck-orca/proton-pass.pat}"
orca_project_root="/workspaces/Teck.Monorepo"

ensure_key() {
  if [ ! -s "$orca_key_file" ]; then
    mkdir -p "$(dirname "$orca_key_file")"
    /mnt/c/Windows/System32/OpenSSH/ssh-keygen.exe -q -t ed25519 -N '' \
      -f "$(wslpath -w "$orca_key_file")" -C 'orca-local-workspace'
  fi
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
