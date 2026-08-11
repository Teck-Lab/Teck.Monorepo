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
      volumes="$(docker volume ls -q --filter "label=com.docker.compose.project=$name")"
      [ -z "$volumes" ] || docker volume rm $volumes >/dev/null 2>&1 || true
      networks="$(docker network ls -q --filter "label=com.docker.compose.project=$name")"
      [ -z "$networks" ] || docker network rm $networks >/dev/null 2>&1 || true
      runtime_dir="${XDG_STATE_HOME:-$HOME/.local/state}/teck-orca/runtimes/$name"
      rm -rf -- "$runtime_dir"
    fi
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
[ -s "$orca_proton_config" ] || {
  echo "Proton reference file missing: $orca_proton_config" >&2
  exit 1
}
[ -s "$orca_proton_pat_file" ] || {
  echo "Proton PAT missing: $orca_proton_pat_file" >&2
  exit 1
}
ensure_key
codex_volume="$(resolve_volume ORCA_CODEX_VOLUME codex-config-)"
opencode_volume="$(resolve_volume ORCA_OPENCODE_VOLUME opencode-data-)"
identity_file="$(wslpath -w "$orca_key_file")"

raw_name="orca-${ORCA_VM_RECIPE_ID:-local}-${ORCA_VM_INSTANCE_ID:-$(date +%s)}"
name="$(printf '%s' "$raw_name" | tr '[:upper:]' '[:lower:]' | tr -cs 'a-z0-9_-' '-' | cut -c1-63)"
ssh_port="$(node -e 'const net=require("net"); const server=net.createServer(); server.listen(0,"127.0.0.1",()=>{console.log(server.address().port); server.close();});')"

"$orca_repo_root/.devcontainer/prepare-compose.sh"
mcp_env="$orca_repo_root/.devcontainer/mcp/mcp.env"
runtime_dir="${XDG_STATE_HOME:-$HOME/.local/state}/teck-orca/runtimes/$name"
install -d -m 0700 "$runtime_dir"
runtime_override="$runtime_dir/compose.runtime.json"
# Docker runs inside WSL2 while Orca's SSH relay runs on Windows. Publishing
# only on WSL loopback drops Windows connections before SSH key exchange, so
# expose the port on the WSL interfaces and return 127.0.0.1 to Orca.
jq -n \
  --arg image "$base_image" --arg codexVolume "$codex_volume" \
  --arg codexAuth "$orca_codex_auth_file" --arg opencodeVolume "$opencode_volume" \
  --arg opencodeAuth "$orca_opencode_auth_file" --arg protonPat "$orca_proton_pat_file" \
  --arg protonRefs "$orca_proton_config" --arg mcpEnv "$mcp_env" \
  --arg runtimeDir "$runtime_dir" --arg sshKey "$(<"$orca_key_file.pub")" --arg sshPort "$ssh_port" \
  '{volumes:{codex_config:{external:true,name:$codexVolume},opencode_data:{external:true,name:$opencodeVolume},
      dind_docker:{},dind_containerd:{}},
    secrets:{teck_mcp_env:{file:$mcpEnv}},services:{
    workspace:{image:$image,pull_policy:"never",restart:"unless-stopped",entrypoint:["/usr/local/share/docker-init.sh","/usr/local/bin/orca-docker-ssh-entrypoint"],
      ports:[("0.0.0.0:"+$sshPort+":22")],labels:{"teck.orca.runtime-state-dir":$runtimeDir},
      environment:{ORCA_SSH_PUBLIC_KEY:$sshKey,TECK_SKIP_PROTON_BOOTSTRAP:"0"},volumes:[
        "codex_config:/home/vscode/.codex",($codexAuth+":/home/vscode/.codex/auth.json"),
        "opencode_data:/home/vscode/.local/share/opencode",($opencodeAuth+":/home/vscode/.local/share/opencode/auth.json"),
        "dind_docker:/var/lib/docker","dind_containerd:/var/lib/containerd",
        ($protonPat+":/run/bootstrap/proton-pass.pat:ro"),($protonRefs+":/run/bootstrap/proton-pass.env:ro"),
        ($mcpEnv+":/run/secrets/teck-mcp/mcp.env:ro")],tmpfs:["/run/secrets/teck-runtime","/run/secrets/teck-github","/run/secrets/teck-ai"]},
    crawl4ai:{secrets:[{source:"teck_mcp_env",target:"/run/secrets/teck-mcp/mcp.env"}]}
  }}' > "$runtime_override"
chmod 600 "$runtime_override"

compose_args=(-p "$name" -f "$orca_repo_root/.devcontainer/compose.yaml" \
  -f "$orca_repo_root/.devcontainer/mcp/compose.yaml" -f "$runtime_override")
docker compose "${compose_args[@]}" up -d --no-build >&2
container_id="$(docker compose "${compose_args[@]}" ps -q workspace)"
[ -n "$container_id" ] || { echo 'Could not resolve the Compose workspace container.' >&2; exit 1; }
port="$(docker port "$container_id" 22/tcp | sed -nE 's/.*:([0-9]+)$/\1/p' | head -1)"
[ -n "$port" ] || { docker logs "$name" >&2; echo 'Could not resolve the published SSH port.' >&2; exit 1; }

# Orca needs the recipe result before its provisioning handshake deadline.
# Research services can become healthy in parallel; only SSH is required to
# attach and create the Git worktree. The WSL socket becomes reachable before
# Windows localhost forwarding is ready, so validate the same authenticated
# Windows OpenSSH path that Orca's relay will use.
ssh_ready=0
for _ in $(seq 1 45); do
  if /mnt/c/Windows/System32/OpenSSH/ssh.exe \
      -o BatchMode=yes -o ConnectTimeout=1 -o StrictHostKeyChecking=no \
      -o UserKnownHostsFile=NUL -o LogLevel=ERROR -o IdentitiesOnly=yes \
      -i "$identity_file" -p "$port" vscode@127.0.0.1 true \
      >/dev/null 2>&1; then
    ssh_ready=1
    break
  fi
  if [ "$(docker inspect "$container_id" --format '{{.State.Running}}' 2>/dev/null || true)" != true ]; then
    break
  fi
  sleep 1
done
[ "$ssh_ready" = 1 ] || {
  docker logs "$container_id" >&2 || true
  echo 'Workspace SSH transport did not become ready.' >&2
  exit 1
}

if [ -n "$source_commit" ]; then
  docker exec -u vscode -e "ORCA_REPO_REF=$repo_ref" -e "ORCA_SOURCE_COMMIT=$source_commit" "$container_id" bash -lc \
    'set -euo pipefail; cd /workspaces/Teck.Monorepo
     git cat-file -e "$ORCA_SOURCE_COMMIT^{commit}"
     git checkout -B "$ORCA_REPO_REF" "$ORCA_SOURCE_COMMIT"' >&2
elif [ -n "$repo_url" ]; then
  docker exec -u vscode -e "ORCA_REPO_REF=$repo_ref" "$container_id" bash -lc \
    'set -euo pipefail; cd /workspaces/Teck.Monorepo
     GH_TOKEN="$(teck-github-app-token read)"; export GH_TOKEN
     askpass=/tmp/orca-git-askpass
     printf "%s\n" "#!/usr/bin/env bash" "case \"\$1\" in *Username*) echo x-access-token;; *Password*) echo \"\$GH_TOKEN\";; esac" > "$askpass"
     chmod 700 "$askpass"; export GIT_ASKPASS="$askpass" GIT_TERMINAL_PROMPT=0
     git fetch origin "$ORCA_REPO_REF" && git checkout -B "$ORCA_REPO_REF" FETCH_HEAD
     rm -f "$askpass"' >&2
fi

jq -cn --argjson port "$port" --arg key "$identity_file" --arg root "$project_root" \
  --arg name "$name" --arg image "$base_image" \
  '{schemaVersion:1,connection:{type:"ssh",projectRoot:$root,target:{label:"Teck local dev container",
    host:"127.0.0.1",port:$port,username:"vscode",identityFile:$key,identitiesOnly:true}},
    userData:{provider:"local-docker",resourceId:$name,image:$image}}'
trap - EXIT
