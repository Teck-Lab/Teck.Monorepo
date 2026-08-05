#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/local-common.sh"

name=""
cleanup_on_error() {
  if [ "$?" -ne 0 ]; then
    [ -z "$name" ] || docker rm -f "$name" >/dev/null 2>&1 || true
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
prepare_github_secrets
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
ensure_key
codex_volume="$(resolve_volume ORCA_CODEX_VOLUME codex-config-)"
opencode_volume="$(resolve_volume ORCA_OPENCODE_VOLUME opencode-data-)"
identity_file="$(wslpath -w "$orca_key_file")"

raw_name="orca-${ORCA_VM_RECIPE_ID:-local}-${ORCA_VM_INSTANCE_ID:-$(date +%s)}"
name="$(printf '%s' "$raw_name" | tr -cs 'A-Za-z0-9_.-' '-' | cut -c1-63)"

docker_args=(run -d --name "$name" -p 127.0.0.1::22)
[ -z "$orca_runtime_secrets_dir" ] || docker_args+=(
  --label "teck.orca.runtime-secrets-dir=$orca_runtime_secrets_dir"
)
docker "${docker_args[@]}" \
  -v "$codex_volume:/home/vscode/.codex" \
  -v "$orca_codex_auth_file:/home/vscode/.codex/auth.json" \
  -v "$opencode_volume:/home/vscode/.local/share/opencode" \
  -v "$orca_opencode_auth_file:/home/vscode/.local/share/opencode/auth.json" \
  -v "$orca_github_secrets_dir:/run/secrets/teck-github:ro" \
  -e "ORCA_SSH_PUBLIC_KEY=$(<"$orca_key_file.pub")" "$base_image" >/dev/null
docker exec -u vscode "$name" teck-setup-github-automation >&2
port="$(docker port "$name" 22/tcp | sed -nE 's/.*:([0-9]+)$/\1/p' | head -1)"
[ -n "$port" ] || { docker logs "$name" >&2; echo 'Could not resolve the published SSH port.' >&2; exit 1; }

token="$(github_app_token read)"
if [ -n "$source_commit" ]; then
  docker exec -u vscode -e "ORCA_REPO_REF=$repo_ref" -e "ORCA_SOURCE_COMMIT=$source_commit" "$name" bash -lc \
    'set -euo pipefail; cd /workspaces/Teck.Monorepo
     git cat-file -e "$ORCA_SOURCE_COMMIT^{commit}"
     git checkout -B "$ORCA_REPO_REF" "$ORCA_SOURCE_COMMIT"' >&2
elif [ -n "$token" ] && [ -n "$repo_url" ]; then
  docker exec -u vscode -e "GH_TOKEN=$token" -e "ORCA_REPO_REF=$repo_ref" "$name" bash -lc \
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
