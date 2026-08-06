#!/usr/bin/env bash
set -euo pipefail

secret_dir="${TECK_GITHUB_SECRET_DIR:-/run/secrets/teck-github}"
git_env="$secret_dir/git.env"
private_key="$secret_dir/signing-private.asc"

if [ ! -r "$git_env" ] || [ ! -r "$private_key" ]; then
  echo "    dedicated automation signing bundle not configured yet"
  return 0 2>/dev/null || exit 0
fi

set -a
# shellcheck disable=SC1090
. "$git_env"
set +a
: "${GIT_AUTOMATION_NAME:?GIT_AUTOMATION_NAME is missing from git.env}"
: "${GIT_AUTOMATION_EMAIL:?GIT_AUTOMATION_EMAIL is missing from git.env}"
: "${GIT_AUTOMATION_SIGNING_KEY:?GIT_AUTOMATION_SIGNING_KEY is missing from git.env}"

mkdir -p "$HOME/.gnupg"
chmod 700 "$HOME/.gnupg"
gpg --batch --import "$private_key" >/dev/null 2>&1

git config --global user.name "$GIT_AUTOMATION_NAME"
git config --global user.email "$GIT_AUTOMATION_EMAIL"
git config --global user.signingkey "$GIT_AUTOMATION_SIGNING_KEY"
git config --global commit.gpgsign true
git config --global gpg.program gpg

fingerprint="$(gpg --batch --with-colons --list-secret-keys "$GIT_AUTOMATION_SIGNING_KEY" 2>/dev/null | awk -F: '/^fpr:/{print $10; exit}')"
[ -n "$fingerprint" ] || {
  echo "automation signing key was imported but no secret key is usable" >&2
  exit 1
}

probe="$(mktemp)"
trap 'rm -f "$probe"' EXIT
printf 'teck signing probe\n' > "$probe"
gpg --batch --yes --local-user "$GIT_AUTOMATION_SIGNING_KEY" --detach-sign "$probe" >/dev/null 2>&1
rm -f "$probe" "$probe.sig"
trap - EXIT
echo "    automation commits: $GIT_AUTOMATION_NAME <$GIT_AUTOMATION_EMAIL> ($fingerprint)"
