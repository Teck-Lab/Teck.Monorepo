#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/../.." && pwd)"
recipe="$repo_root/orca.yaml"
create="$repo_root/scripts/orca-vm/local-create.sh"
common="$repo_root/scripts/orca-vm/local-common.sh"

if grep -q 'checkoutMode:' "$recipe"; then
  echo 'recipe must leave checkout ownership with Orca' >&2
  exit 1
fi
if grep -q 'issueCommand:' "$recipe"; then
  echo 'recipe must use the native startup draft instead of a second terminal' >&2
  exit 1
fi
grep -q 'local-suspend.cmd' "$recipe"
grep -q 'local-resume.cmd' "$recipe"
grep -q "create: '%ORCA_REPO_PATH%" "$recipe"
grep -q 'schemaVersion:1,connection:' "$common"
if grep -q 'provisioned-root\|schemaVersion:2' "$common" "$create"; then
  echo 'schema-v2 provisioned-root behavior is still present' >&2
  exit 1
fi
grep -q 'ORCA_VM_INSTANCE_ID:-workspace' "$create"
grep -q 'ORCA_RECIPE_ID:-' "$create"
grep -q 'created_this_attempt=0' "$create"
grep -q 'created_this_attempt" = 1' "$create"
grep -q 'ORCA_REPO_URL' "$create"
if grep -q 'attempt_suffix' "$create"; then
  echo 'create still uses an attempt timestamp instead of stable instance identity' >&2
  exit 1
fi

for script in local-common.sh local-create.sh local-suspend.sh local-resume.sh local-destroy.sh; do
  bash -n "$repo_root/scripts/orca-vm/$script"
done
for launcher in local-create.cmd local-suspend.cmd local-resume.cmd local-destroy.cmd; do
  grep -q 'pushd "%SystemRoot%"' "$repo_root/scripts/orca-vm/$launcher"
  grep -q 'powershell.exe -NoProfile -ExecutionPolicy Bypass' "$repo_root/scripts/orca-vm/$launcher"
done
grep -q "ORCA_REPO_PATH/up" "$repo_root/scripts/orca-vm/local-launch.ps1"
grep -q "ORCA_RECIPE_RESULT_SCHEMA_VERSION" "$repo_root/scripts/orca-vm/local-launch.ps1"
grep -q "ORCA_RECIPE_ID" "$repo_root/scripts/orca-vm/local-launch.ps1"
grep -q "ORCA_VM_INSTANCE_ID" "$repo_root/scripts/orca-vm/local-launch.ps1"
grep -q "ORCA_REPO_REF_HEAD" "$repo_root/scripts/orca-vm/local-launch.ps1"
grep -q 'Select-Object -Unique' "$repo_root/scripts/orca-vm/local-launch.ps1"
grep -q '\[Console\]::In.ReadToEnd()' "$repo_root/scripts/orca-vm/local-launch.ps1"

fixture="$(mktemp -d)"
trap 'rm -rf "$fixture"' EXIT
runtime="$fixture/state/teck-orca/runtimes/orca-test"
mkdir -p "$runtime/workspace/.devcontainer/.orca-runtime" "$fixture/bin"
printf '{}\n' >"$runtime/workspace/.devcontainer/.orca-runtime/devcontainer.json"
printf 'key\n' >"$fixture/key"

cat >"$fixture/bin/docker" <<'EOF'
#!/usr/bin/env bash
case "$1" in
  ps) printf 'workspace-id\n' ;;
  port) printf '0.0.0.0:44167\n' ;;
  start|stop) printf '%s\n' "$*" >>"$ORCA_TEST_DOCKER_LOG" ;;
  logs) ;;
  *) exit 1 ;;
esac
EOF
cat >"$fixture/bin/wslpath" <<'EOF'
#!/usr/bin/env bash
printf '%s\n' "${2:-$1}"
EOF
cat >"$fixture/bin/ssh-ok" <<'EOF'
#!/usr/bin/env bash
exit 0
EOF
chmod +x "$fixture/bin/docker" "$fixture/bin/wslpath" "$fixture/bin/ssh-ok"

payload="$(jq -cn --arg runtime "$runtime" '{recipeResult:{userData:{resourceId:"orca-test",runtimeDir:$runtime}}}')"
export PATH="$fixture/bin:$PATH"
export XDG_STATE_HOME="$fixture/state"
export ORCA_WINDOWS_PROFILE='C:\\Users\\test'
export ORCA_SSH_KEY_FILE="$fixture/key"
export ORCA_WINDOWS_SSH_COMMAND="$fixture/bin/ssh-ok"
export ORCA_TEST_DOCKER_LOG="$fixture/docker.log"

resume_result="$(printf '%s' "$payload" | "$repo_root/scripts/orca-vm/local-resume.sh")"
jq -e '.schemaVersion == 1 and (has("checkoutMode") | not) and .connection.target.port == 44167 and .userData.resourceId == "orca-test"' <<<"$resume_result" >/dev/null
legacy_payload="$(jq -cn --arg runtime "$runtime" '{recipeResult:{schemaVersion:1,userData:{resourceId:"orca-test",runtimeDir:$runtime}}}')"
legacy_result="$(printf '%s' "$legacy_payload" | "$repo_root/scripts/orca-vm/local-resume.sh")"
jq -e '.schemaVersion == 1 and (has("checkoutMode") | not) and .connection.target.port == 44167' <<<"$legacy_result" >/dev/null
printf '%s' "$payload" | "$repo_root/scripts/orca-vm/local-suspend.sh"
grep -q '^start workspace-id$' "$fixture/docker.log"
grep -q '^stop workspace-id$' "$fixture/docker.log"

echo 'Orca local environment lifecycle contract passed.'
