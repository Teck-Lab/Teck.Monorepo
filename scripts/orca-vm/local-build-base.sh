#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/local-common.sh"

source_image="teck-devcontainer:orca-source"
repo_url="${ORCA_REPO_URL:-$(git -C "$orca_repo_root" remote get-url origin)}"
default_ref="$(git -C "$orca_repo_root" symbolic-ref --short refs/remotes/origin/HEAD 2>/dev/null || true)"
default_ref="${default_ref#origin/}"
repo_ref="${ORCA_REPO_REF:-$default_ref}"
[ -n "$repo_ref" ] || repo_ref=main
token="$(git_token)"
[ -n "$token" ] || { echo 'GitHub token missing; export GH_TOKEN or run gh auth login.' >&2; exit 1; }

echo 'Building the existing dev-container definition...' >&2
npx --yes @devcontainers/cli build \
  --workspace-folder "$orca_repo_root" \
  --image-name "$source_image" >&2

echo 'Adding the local SSH transport...' >&2
docker build --build-arg "DEVCONTAINER_IMAGE=$source_image" \
  -f "$orca_vm_dir/Dockerfile" -t "$orca_base_image" "$orca_repo_root" >&2

# Clone and run the repo setup before snapshotting, keeping credentials out of
# the committed layer. A failed preparation always removes its container.
ensure_key
prep="teck-orca-base-prep"
docker rm -f "$prep" >/dev/null 2>&1 || true
cleanup() { docker rm -f "$prep" >/dev/null 2>&1 || true; }
trap cleanup EXIT
docker run -d --name "$prep" \
  -e "ORCA_SSH_PUBLIC_KEY=$(<"$orca_key_file.pub")" \
  -e "GH_TOKEN=$token" "$orca_base_image" >/dev/null
docker exec "$prep" bash -lc 'mkdir -p /workspaces && chown vscode:vscode /workspaces'
docker exec -u vscode \
  -e "GH_TOKEN=$token" -e "ORCA_REPO_URL=$repo_url" -e "ORCA_REPO_REF=$repo_ref" \
  "$prep" bash -lc 'set -euo pipefail
    askpass=/tmp/orca-git-askpass
    printf "%s\n" "#!/usr/bin/env bash" "case \"\$1\" in *Username*) echo x-access-token;; *Password*) echo \"\$GH_TOKEN\";; esac" > "$askpass"
    chmod 700 "$askpass"
    export GIT_ASKPASS="$askpass" GIT_TERMINAL_PROMPT=0
    git clone --branch "$ORCA_REPO_REF" --single-branch "$ORCA_REPO_URL" /workspaces/Teck.Monorepo
    rm -f "$askpass"
    cd /workspaces/Teck.Monorepo
    bash .devcontainer/postCreate.sh'
docker exec "$prep" rm -f /tmp/orca-git-askpass
docker commit --change='ENTRYPOINT ["/usr/local/bin/orca-docker-ssh-entrypoint"]' \
  "$prep" "$orca_base_image" >/dev/null
cleanup
trap - EXIT

python3 -c 'import json,os,sys; p,base,url,ref,root=sys.argv[1:]; open(p,"w").write(json.dumps({"baseImage":base,"repoUrl":url,"repoRef":ref,"projectRoot":root},indent=2)+"\n"); os.chmod(p,0o600)' \
  "$orca_state_file" "$orca_base_image" "$repo_url" "$repo_ref" "$orca_project_root"
echo "Base image ready: $orca_base_image" >&2
echo 'Codex and OpenCode auth will be mounted from the dev-container volumes.' >&2
