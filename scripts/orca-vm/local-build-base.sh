#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/local-common.sh"

source_image="teck-devcontainer:orca-source"
repo_url="${ORCA_REPO_URL:-$(git -C "$orca_repo_root" remote get-url origin)}"
source_ref="${ORCA_SOURCE_REF:-$(git -C "$orca_repo_root" branch --show-current)}"
[ -n "$source_ref" ] || { echo 'Could not resolve the local source ref.' >&2; exit 1; }
# A standalone bundle cannot use a shallow boundary as an implicit parent.
# Hydrate history on the host before creating the credential-free snapshot.
if [ "$(git -C "$orca_repo_root" rev-parse --is-shallow-repository)" = true ]; then
  git -C "$orca_repo_root" fetch --unshallow origin >&2
fi
source_commit="$(git -C "$orca_repo_root" rev-parse "$source_ref")"
repo_ref="${ORCA_REPO_REF:-$source_ref}"

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
source_bundle="$(mktemp /tmp/teck-orca-source.XXXXXX.bundle)"
git -C "$orca_repo_root" bundle create "$source_bundle" "$source_ref" >&2
docker rm -f "$prep" >/dev/null 2>&1 || true
cleanup() {
  docker rm -f "$prep" >/dev/null 2>&1 || true
  rm -f -- "$source_bundle"
  cleanup_runtime_secrets || true
}
trap cleanup EXIT
docker run -d --name "$prep" \
  -e "ORCA_SSH_PUBLIC_KEY=$(<"$orca_key_file.pub")" "$orca_base_image" >/dev/null
docker exec "$prep" bash -lc 'mkdir -p /workspaces && chown vscode:vscode /workspaces'
docker cp "$source_bundle" "$prep:/tmp/teck-orca-source.bundle"
docker exec "$prep" chown vscode:vscode /tmp/teck-orca-source.bundle
docker exec -u vscode \
  -e "ORCA_REPO_URL=$repo_url" -e "ORCA_REPO_REF=$repo_ref" -e "ORCA_SOURCE_COMMIT=$source_commit" \
  "$prep" bash -lc 'set -euo pipefail
    git clone /tmp/teck-orca-source.bundle /workspaces/Teck.Monorepo
    cd /workspaces/Teck.Monorepo
    git remote set-url origin "$ORCA_REPO_URL"
    git checkout -B "$ORCA_REPO_REF" "$ORCA_SOURCE_COMMIT"
    bash .devcontainer/postCreate.sh'
docker commit --change='ENTRYPOINT ["/usr/local/bin/orca-docker-ssh-entrypoint"]' \
  "$prep" "$orca_base_image" >/dev/null
cleanup
trap - EXIT

bun -e 'import { chmodSync, writeFileSync } from "node:fs"; const [path,baseImage,repoUrl,repoRef,projectRoot,sourceCommit]=process.argv.slice(2); writeFileSync(path, JSON.stringify({baseImage,repoUrl,repoRef,projectRoot,sourceCommit}, null, 2)+"\n"); chmodSync(path, 0o600);' \
  "$orca_state_file" "$orca_base_image" "$repo_url" "$repo_ref" "$orca_project_root" "$source_commit"
echo "Base image ready: $orca_base_image" >&2
echo 'Codex and OpenCode auth will be mounted from the dev-container volumes.' >&2
