#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/local-common.sh"

name=""
runtime_dir=""
cleanup_on_error() {
  if [ "$?" -ne 0 ] && [ -n "$name" ]; then
    pass-cli logout --force >/dev/null 2>&1 || true
    containers="$(docker ps -aq --filter "label=com.docker.compose.project=$name")"
    [ -z "$containers" ] || docker rm -f $containers >/dev/null 2>&1 || true
    [ -z "$runtime_dir" ] || rm -rf -- "$runtime_dir"
  fi
}
trap cleanup_on_error EXIT

[ -s "$orca_codex_auth_file" ] || { echo "Codex credential file missing: $orca_codex_auth_file" >&2; exit 1; }
[ -s "$orca_opencode_auth_file" ] || { echo "OpenCode credential file missing: $orca_opencode_auth_file" >&2; exit 1; }
[ -s "$orca_provider_refs_file" ] || { echo "Provider references missing: $orca_provider_refs_file" >&2; exit 1; }
[ -s "$orca_proton_pat_file" ] || { echo "Proton PAT missing: $orca_proton_pat_file" >&2; exit 1; }
command -v pass-cli >/dev/null || { echo 'Proton Pass CLI is required on the WSL host.' >&2; exit 1; }
ensure_key
identity_file="$(wslpath -w "$orca_key_file")"

raw_name="orca-${ORCA_VM_RECIPE_ID:-local}-${ORCA_VM_INSTANCE_ID:-$(date +%s)}"
name="$(printf '%s' "$raw_name" | tr '[:upper:]' '[:lower:]' | tr -cs 'a-z0-9_-' '-' | cut -c1-63)"
runtime_dir="${XDG_STATE_HOME:-$HOME/.local/state}/teck-orca/runtimes/$name"
workspace_dir="$runtime_dir/workspace"
install -d -m 0700 "$runtime_dir"
provider_env="$runtime_dir/providers.env"
provider_session="$runtime_dir/proton-session"
install -d -m 0700 "$provider_session"
export PROTON_PASS_SESSION_DIR="$provider_session"
export PROTON_PASS_KEY_PROVIDER=fs PROTON_PASS_DISABLE_TELEMETRY=1
export PROTON_PASS_PERSONAL_ACCESS_TOKEN="$(tr -d '\r\n' < "$orca_proton_pat_file")"
pass-cli login >/dev/null
unset PROTON_PASS_PERSONAL_ACCESS_TOKEN
pass-cli run --env-file "$orca_provider_refs_file" -- \
  "$orca_vm_dir/materialize-provider-env.sh" "$provider_env"
pass-cli logout --force >/dev/null 2>&1 || true
rm -rf -- "$provider_session"

# Each parent feature receives its own checkout. The Dev Container CLI reads
# the definition from that checkout, so changes merged into the selected ref
# are applied automatically on the next Orca workspace creation.
git clone --shared --no-checkout "$orca_repo_root" "$workspace_dir" >&2
source_ref="${ORCA_REPO_REF:-$(git -C "$orca_repo_root" branch --show-current)}"
[ -n "$source_ref" ] || source_ref=main
git -C "$workspace_dir" checkout "$source_ref" >&2
git -C "$workspace_dir" remote set-url origin "$(git -C "$orca_repo_root" remote get-url origin)"

ssh_port="$(node -e 'const net=require("net");const s=net.createServer();s.listen(0,"127.0.0.1",()=>{console.log(s.address().port);s.close()})')"
runtime_override="$runtime_dir/compose.runtime.json"
jq -n --arg runtimeDir "$runtime_dir" --arg sshKey "$(<"$orca_key_file.pub")" --arg sshPort "$ssh_port" \
  '{services:{workspace:{restart:"unless-stopped",entrypoint:["/usr/local/share/docker-init.sh","/usr/local/bin/orca-docker-ssh-entrypoint"],
      ports:[("0.0.0.0:"+$sshPort+":22")],labels:{"teck.orca.runtime-state-dir":$runtimeDir},
      environment:{ORCA_SSH_PUBLIC_KEY:$sshKey}}}}' > "$runtime_override"
chmod 600 "$runtime_override"

runtime_config="$runtime_dir/devcontainer.json"
jq --arg base "$workspace_dir/.devcontainer" --arg override "$runtime_override" \
  '.dockerComposeFile = [($base + "/compose.yaml"), ($base + "/mcp/compose.yaml"), $override]' \
  "$workspace_dir/.devcontainer/devcontainer.json" > "$runtime_config"

export COMPOSE_PROJECT_NAME="$name"
export TECK_MCP_ENV_FILE="$orca_repo_root/.devcontainer/mcp/mcp.env"
export TECK_PROVIDER_ENV_FILE="$provider_env"
devcontainer_up=(npx --yes @devcontainers/cli@0.88.0 up --workspace-folder "$workspace_dir" --config "$runtime_config")
if ! up_result="$("${devcontainer_up[@]}")"; then
  echo 'Initial Dev Container startup failed after the image build; retrying once.' >&2
  up_result="$("${devcontainer_up[@]}")"
fi
container_id="$(jq -er '.containerId' <<<"$up_result")"
port="$(docker port "$container_id" 22/tcp | sed -nE 's/.*:([0-9]+)$/\1/p' | head -1)"
[ -n "$port" ] || { echo 'Could not resolve the published SSH port.' >&2; exit 1; }

ssh_ready=0
for _ in $(seq 1 45); do
  if /mnt/c/Windows/System32/OpenSSH/ssh.exe -o BatchMode=yes -o ConnectTimeout=1 \
      -o StrictHostKeyChecking=no -o UserKnownHostsFile=NUL -o LogLevel=ERROR \
      -o IdentitiesOnly=yes -i "$identity_file" -p "$port" vscode@127.0.0.1 true >/dev/null 2>&1; then
    ssh_ready=1; break
  fi
  sleep 1
done
[ "$ssh_ready" = 1 ] || { docker logs "$container_id" >&2 || true; echo 'Workspace SSH transport did not become ready.' >&2; exit 1; }

jq -cn --argjson port "$port" --arg key "$identity_file" --arg root "$orca_project_root" \
  --arg name "$name" --arg runtime "$runtime_dir" \
  '{schemaVersion:1,connection:{type:"ssh",projectRoot:$root,target:{label:"Teck Dev Container",host:"127.0.0.1",port:$port,username:"vscode",identityFile:$key,identitiesOnly:true}},userData:{provider:"devcontainer-cli",resourceId:$name,runtimeDir:$runtime}}'
trap - EXIT
