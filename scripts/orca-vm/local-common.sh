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
orca_state_file="$orca_vm_dir/local-state.json"
orca_windows_profile="$(/mnt/c/Windows/System32/cmd.exe /d /c 'echo %USERPROFILE%' 2>/dev/null | tr -d '\r')"
orca_key_file="${ORCA_SSH_KEY_FILE:-$(wslpath -u "$orca_windows_profile")/.ssh/orca-teck-local-ed25519}"
orca_codex_auth_file="${ORCA_CODEX_AUTH_FILE:-$HOME/.codex/auth.json}"
orca_opencode_auth_file="${ORCA_OPENCODE_AUTH_FILE:-$HOME/.local/share/opencode/auth.json}"
orca_github_secrets_dir="${ORCA_GITHUB_SECRETS_DIR:-$orca_repo_root/.devcontainer/github-app}"
orca_ai_provider_env_file="${ORCA_AI_PROVIDER_ENV_FILE:-$orca_repo_root/.devcontainer/ai/providers.env}"
orca_proton_config="${ORCA_PROTON_CONFIG:-$orca_repo_root/.devcontainer/github-app/proton-pass.env}"
orca_proton_pat_file="${PROTON_PASS_PERSONAL_ACCESS_TOKEN_FILE:-${XDG_CONFIG_HOME:-$HOME/.config}/teck-orca/proton-pass.pat}"
orca_base_image="teck-devcontainer:orca-base"
orca_project_root="/workspaces/Teck.Monorepo"

orca_runtime_secrets_dir=""

cleanup_runtime_secrets() {
  local target="${1:-$orca_runtime_secrets_dir}"
  [ -n "$target" ] || return 0
  case "$target" in
    /dev/shm/teck-orca-secrets.*) rm -rf -- "$target" ;;
    *) echo "Refusing to remove unexpected runtime-secret path: $target" >&2; return 1 ;;
  esac
}

prepare_runtime_secrets() {
  [ -s "$orca_proton_config" ] || return 0
  command -v pass-cli >/dev/null 2>&1 || {
    echo "Proton Pass is configured but pass-cli is not installed in WSL." >&2
    echo "See https://protonpass.github.io/pass-cli/get-started/installation/" >&2
    return 1
  }

  local proton_pat="${PROTON_PASS_PERSONAL_ACCESS_TOKEN:-}"
  if [ -z "$proton_pat" ] && [ -s "$orca_proton_pat_file" ]; then
    proton_pat="$(<"$orca_proton_pat_file")"
  fi
  [ -n "$proton_pat" ] || {
    echo "Proton Pass PAT missing. Set PROTON_PASS_PERSONAL_ACCESS_TOKEN or create:" >&2
    echo "  $orca_proton_pat_file" >&2
    return 1
  }

  orca_runtime_secrets_dir="$(mktemp -d /dev/shm/teck-orca-secrets.XXXXXX)"
  chmod 700 "$orca_runtime_secrets_dir"
  mkdir -m 700 "$orca_runtime_secrets_dir/container"

  local session_dir="$orca_runtime_secrets_dir/proton-session"
  mkdir -m 700 "$session_dir"
  export PROTON_PASS_SESSION_DIR="$session_dir"
  export PROTON_PASS_KEY_PROVIDER=fs
  export PROTON_PASS_DISABLE_TELEMETRY=1
  export PROTON_PASS_PERSONAL_ACCESS_TOKEN="$proton_pat"

  if ! pass-cli login >/dev/null; then
    unset PROTON_PASS_PERSONAL_ACCESS_TOKEN
    cleanup_runtime_secrets
    return 1
  fi
  unset PROTON_PASS_PERSONAL_ACCESS_TOKEN proton_pat

  local materialize_status=0 logout_status=0
  pass-cli run --env-file "$orca_proton_config" -- \
    "$orca_vm_dir/materialize-proton-secrets.sh" "$orca_runtime_secrets_dir" \
    || materialize_status=$?
  pass-cli logout >/dev/null 2>&1 || logout_status=$?
  rm -rf -- "$session_dir"
  unset PROTON_PASS_SESSION_DIR PROTON_PASS_KEY_PROVIDER PROTON_PASS_DISABLE_TELEMETRY

  if [ "$materialize_status" -ne 0 ] || [ "$logout_status" -ne 0 ]; then
    echo "Proton Pass secret materialization or logout failed." >&2
    cleanup_runtime_secrets
    return 1
  fi

  orca_github_secrets_dir="$orca_runtime_secrets_dir/container"
  orca_ai_provider_env_file="$orca_runtime_secrets_dir/container/ai-providers.env"
  echo "GitHub, signing, and direct AI provider secrets loaded from Proton Pass into WSL tmpfs." >&2
}

ensure_key() {
  if [ ! -s "$orca_key_file" ]; then
    mkdir -p "$(dirname "$orca_key_file")"
    /mnt/c/Windows/System32/OpenSSH/ssh-keygen.exe -q -t ed25519 -N '' \
      -f "$(wslpath -w "$orca_key_file")" -C 'orca-local-workspace'
  fi
}

github_app_token() {
  local access="${1:-read}"
  "$orca_repo_root/.devcontainer/github-app-token.sh" "$access" "$orca_github_secrets_dir"
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
