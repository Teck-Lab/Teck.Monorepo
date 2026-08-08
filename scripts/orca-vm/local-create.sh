#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/local-common.sh"

name=""
container_id=""
cleanup_on_error() {
  if [ "$?" -ne 0 ]; then
    if [ -n "$name" ]; then
      containers="$(docker ps -aq --filter "label=com.docker.compose.project=$name")"
      [ -z "$containers" ] || docker rm -f $containers >/dev/null 2>&1 || true
      networks="$(docker network ls -q --filter "label=com.docker.compose.project=$name")"
      [ -z "$networks" ] || docker network rm $networks >/dev/null 2>&1 || true
    fi
    cleanup_runtime_secrets || true
  fi
}
trap cleanup_on_error EXIT

base_image="${ORCA_BASE_IMAGE:-$(state_value baseImage)}"
[ -n "$base_image" ] || base_image="$orca_base_image"
project_root="${ORCA_PROJECT_ROOT:-$(state_value projectRoot)}"
[ -n "$project_root" ] || project_root="$orca_project_root"
repo_url="${ORCA_REPO_URL:-$(state_value repoUrl)}"
state_repo_ref="$(state_value repoRef)"
repo_ref="${ORCA_REPO_REF:-$state_repo_ref}"
[ -n "$repo_ref" ] || repo_ref=main
source_commit="$(state_value sourceCommit)"
# Orca supplies the configured ref during normal provisioning. Preserve the
# local snapshot when it matches; only a different ref (or an explicit flag)
# requests remote state.
if [ "${ORCA_FETCH_REMOTE:-0}" = 1 ] || { [ -n "${ORCA_REPO_REF:-}" ] && [ "$repo_ref" != "$state_repo_ref" ]; }; then
  source_commit=""
fi
prepare_runtime_secrets
docker image inspect "$base_image" >/dev/null 2>&1 || {
  echo "Base image missing; run local-build-base.sh first." >&2
  exit 1
}
[ -s "$orca_codex_auth_file" ] || {
  echo "Codex credential file missing: $orca_codex_auth_file" >&2
  exit 1
}
[ -s "$orca_opencode_auth_file" ] || {
  echo "OpenCode credential file missing: $orca_opencode_auth_file" >&2
  echo "Run 'opencode auth login' in WSL2 before provisioning." >&2
  exit 1
}
[ -s "$orca_ai_provider_env_file" ] || {
  echo "AI provider credential file missing: $orca_ai_provider_env_file" >&2
  echo "Configure Proton Pass or create .devcontainer/ai/providers.env." >&2
  exit 1
}
ensure_key
codex_volume="$(resolve_volume ORCA_CODEX_VOLUME codex-config-)"
opencode_volume="$(resolve_volume ORCA_OPENCODE_VOLUME opencode-data-)"
identity_file="$(wslpath -w "$orca_key_file")"

raw_name="orca-${ORCA_VM_RECIPE_ID:-local}-${ORCA_VM_INSTANCE_ID:-$(date +%s)}"
name="$(printf '%s' "$raw_name" | tr '[:upper:]' '[:lower:]' | tr -cs 'a-z0-9_-' '-' | cut -c1-63)"

AI_PROVIDER_ENV_FILE="$orca_ai_provider_env_file" "$orca_repo_root/.devcontainer/prepare-compose.sh"
mcp_env="$orca_runtime_secrets_dir/container/mcp.env"
printf 'CRAWL4AI_API_TOKEN=%s\n' "$(openssl rand -hex 32)" > "$mcp_env"
chmod 600 "$mcp_env"
runtime_override="$orca_runtime_secrets_dir/compose.runtime.json"
python3 - "$runtime_override" "$base_image" "$codex_volume" "$orca_codex_auth_file" \
  "$opencode_volume" "$orca_opencode_auth_file" "$orca_github_secrets_dir" \
  "$orca_ai_provider_env_file" "$mcp_env" "$orca_runtime_secrets_dir" "$(<"$orca_key_file.pub")" <<'PY'
import json, sys
out, image, codex_volume, codex_auth, opencode_volume, opencode_auth, github_secrets, provider_env, mcp_env, secrets_dir, ssh_key = sys.argv[1:]
config = {"services": {
  "workspace": {
    "image": image,
    "pull_policy": "never",
    "ports": ["127.0.0.1::22"],
    "labels": {"teck.orca.runtime-secrets-dir": secrets_dir},
    "environment": {"ORCA_SSH_PUBLIC_KEY": ssh_key},
    "env_file": [provider_env, mcp_env],
    "volumes": [
      f"{codex_volume}:/home/vscode/.codex",
      f"{codex_auth}:/home/vscode/.codex/auth.json",
      f"{opencode_volume}:/home/vscode/.local/share/opencode",
      f"{opencode_auth}:/home/vscode/.local/share/opencode/auth.json",
      f"{github_secrets}:/run/secrets/teck-github:ro"
    ]
  },
  "crawl4ai": {"env_file": [mcp_env]}
}}
with open(out, "w") as handle:
  json.dump(config, handle)
PY

compose_args=(-p "$name" -f "$orca_repo_root/.devcontainer/compose.yaml" \
  -f "$orca_repo_root/.devcontainer/mcp/compose.yaml" -f "$runtime_override")
docker compose "${compose_args[@]}" up -d --no-build --wait >&2
container_id="$(docker compose "${compose_args[@]}" ps -q workspace)"
[ -n "$container_id" ] || { echo 'Could not resolve the Compose workspace container.' >&2; exit 1; }
port="$(docker port "$container_id" 22/tcp | sed -nE 's/.*:([0-9]+)$/\1/p' | head -1)"
[ -n "$port" ] || { docker logs "$name" >&2; echo 'Could not resolve the published SSH port.' >&2; exit 1; }

token="$(github_app_token read)"
if [ -n "$source_commit" ]; then
  docker exec -u vscode -e "ORCA_REPO_REF=$repo_ref" -e "ORCA_SOURCE_COMMIT=$source_commit" "$container_id" bash -lc \
    'set -euo pipefail; cd /workspaces/Teck.Monorepo
     git cat-file -e "$ORCA_SOURCE_COMMIT^{commit}"
     git checkout -B "$ORCA_REPO_REF" "$ORCA_SOURCE_COMMIT"' >&2
elif [ -n "$token" ] && [ -n "$repo_url" ]; then
  docker exec -u vscode -e "GH_TOKEN=$token" -e "ORCA_REPO_REF=$repo_ref" "$container_id" bash -lc \
    'set -euo pipefail; cd /workspaces/Teck.Monorepo
     askpass=/tmp/orca-git-askpass
     printf "%s\n" "#!/usr/bin/env bash" "case \"\$1\" in *Username*) echo x-access-token;; *Password*) echo \"\$GH_TOKEN\";; esac" > "$askpass"
     chmod 700 "$askpass"; export GIT_ASKPASS="$askpass" GIT_TERMINAL_PROMPT=0
     git fetch origin "$ORCA_REPO_REF" && git checkout -B "$ORCA_REPO_REF" FETCH_HEAD
     rm -f "$askpass"' >&2
fi

python3 -c 'import json,sys; port,user,key,root,name,image=sys.argv[1:]; print(json.dumps({"schemaVersion":1,"connection":{"type":"ssh","projectRoot":root,"target":{"label":"Teck local dev container","host":"127.0.0.1","port":int(port),"username":user,"identityFile":key,"identitiesOnly":True}},"userData":{"provider":"local-docker","resourceId":name,"image":image}},separators=(",",":")))' \
  "$port" vscode "$identity_file" "$project_root" "$name" "$base_image"
trap - EXIT
