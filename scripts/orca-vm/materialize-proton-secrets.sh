#!/usr/bin/env bash
set -euo pipefail

runtime_dir="${1:?runtime secret directory is required}"
case "$runtime_dir" in
  /dev/shm/teck-orca-secrets.*) ;;
  *) echo "Unexpected runtime-secret path: $runtime_dir" >&2; exit 1 ;;
esac

: "${TECK_GITHUB_APP_ID:?missing TECK_GITHUB_APP_ID Proton reference}"
: "${TECK_GITHUB_APP_INSTALLATION_ID:?missing TECK_GITHUB_APP_INSTALLATION_ID Proton reference}"
: "${TECK_GITHUB_APP_PRIVATE_KEY:?missing TECK_GITHUB_APP_PRIVATE_KEY Proton reference}"
: "${TECK_GIT_AUTOMATION_NAME:?missing TECK_GIT_AUTOMATION_NAME Proton reference}"
: "${TECK_GIT_AUTOMATION_EMAIL:?missing TECK_GIT_AUTOMATION_EMAIL Proton reference}"
: "${TECK_GIT_SIGNING_PRIVATE_KEY:?missing TECK_GIT_SIGNING_PRIVATE_KEY Proton reference}"

secret_dir="$runtime_dir/container"
umask 077
printf 'GITHUB_APP_ID=%s\nGITHUB_APP_INSTALLATION_ID=%s\n' \
  "$TECK_GITHUB_APP_ID" "$TECK_GITHUB_APP_INSTALLATION_ID" > "$secret_dir/github-app.env"
printf '%s\n' "$TECK_GITHUB_APP_PRIVATE_KEY" > "$secret_dir/github-app.pem"
printf '%s\n' "$TECK_GIT_SIGNING_PRIVATE_KEY" > "$secret_dir/signing-private.asc"

fingerprint="$(gpg --batch --with-colons --import-options show-only --import \
  "$secret_dir/signing-private.asc" 2>/dev/null | awk -F: '/^fpr:/{print $10; exit}')"
[ -n "$fingerprint" ] || { echo "Could not resolve Proton signing-key fingerprint." >&2; exit 1; }
printf 'GIT_AUTOMATION_NAME=%q\nGIT_AUTOMATION_EMAIL=%q\nGIT_AUTOMATION_SIGNING_KEY=%q\n' \
  "$TECK_GIT_AUTOMATION_NAME" "$TECK_GIT_AUTOMATION_EMAIL" "$fingerprint" > "$secret_dir/git.env"

openssl pkey -in "$secret_dir/github-app.pem" -noout -check >/dev/null
chmod 600 "$secret_dir/github-app.env" "$secret_dir/github-app.pem" \
  "$secret_dir/signing-private.asc" "$secret_dir/git.env"
