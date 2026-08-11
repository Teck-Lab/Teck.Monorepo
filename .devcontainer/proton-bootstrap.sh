#!/usr/bin/env bash
set -euo pipefail

[ "${TECK_SKIP_PROTON_BOOTSTRAP:-0}" != 1 ] || exit 0
pat_file="${PROTON_PASS_PAT_FILE:-/run/bootstrap/proton-pass.pat}"
refs_file="${PROTON_PASS_REFS_FILE:-/run/bootstrap/proton-pass.env}"
secret_root=/run/secrets/teck-runtime

if [ ! -s "$pat_file" ] || [ ! -s "$refs_file" ]; then
  [ -s /run/secrets/teck-github/github-app.env ] && [ -s /run/secrets/teck-ai/providers.env ] && exit 0
  echo "Proton bootstrap inputs are missing: $pat_file and $refs_file" >&2
  exit 1
fi

install -d -m 0700 "$secret_root"
find "$secret_root" -mindepth 1 -maxdepth 1 -exec rm -rf -- {} +
install -d -m 0700 "$secret_root/proton-session" "$secret_root/container"
export PROTON_PASS_SESSION_DIR="$secret_root/proton-session"
export PROTON_PASS_KEY_PROVIDER=fs PROTON_PASS_DISABLE_TELEMETRY=1
export PROTON_PASS_PERSONAL_ACCESS_TOKEN="$(tr -d '\r\n' < "$pat_file")"
cleanup() {
  unset PROTON_PASS_PERSONAL_ACCESS_TOKEN
  pass-cli logout --force >/dev/null 2>&1 || true
  rm -rf -- "$secret_root/proton-session"
}
trap cleanup EXIT
pass-cli login >/dev/null
unset PROTON_PASS_PERSONAL_ACCESS_TOKEN
pass-cli run --env-file "$refs_file" -- teck-materialize-proton-secrets "$secret_root"

install -d -m 0755 /run/secrets/teck-github /run/secrets/teck-ai
install -m 0600 "$secret_root/container/github-app.env" /run/secrets/teck-github/github-app.env
install -m 0600 "$secret_root/container/github-app.pem" /run/secrets/teck-github/github-app.pem
install -m 0600 "$secret_root/container/ai-providers.env" /run/secrets/teck-ai/providers.env
chown -R vscode:vscode /run/secrets/teck-github /run/secrets/teck-ai
echo "Runtime credentials loaded from Proton Pass." >&2
