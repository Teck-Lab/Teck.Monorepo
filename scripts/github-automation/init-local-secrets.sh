#!/usr/bin/env bash
set -euo pipefail

repo_root="$(git rev-parse --show-toplevel)"
secret_dir="$repo_root/.devcontainer/github-app"
automation_name="${1:-Teck Agent}"
automation_email="${2:-jl@tecklab.dk}"

mkdir -p "$secret_dir"
chmod 700 "$secret_dir"

for target in git.env signing-private.asc signing-public.asc; do
  [ ! -e "$secret_dir/$target" ] || {
    echo "Refusing to overwrite existing $secret_dir/$target" >&2
    exit 1
  }
done

temporary_home="$(mktemp -d)"
cleanup() { rm -rf "$temporary_home"; }
trap cleanup EXIT
chmod 700 "$temporary_home"

gpg --batch --homedir "$temporary_home" --passphrase '' --quick-generate-key \
  "$automation_name <$automation_email>" ed25519 sign 2y
fingerprint="$(gpg --batch --homedir "$temporary_home" --with-colons --list-secret-keys "$automation_email" | awk -F: '/^fpr:/{print $10; exit}')"
[ -n "$fingerprint" ] || { echo "Could not resolve generated signing-key fingerprint" >&2; exit 1; }

gpg --batch --homedir "$temporary_home" --armor --export-secret-keys "$fingerprint" > "$secret_dir/signing-private.asc"
gpg --batch --homedir "$temporary_home" --armor --export "$fingerprint" > "$secret_dir/signing-public.asc"
printf 'GIT_AUTOMATION_NAME=%q\nGIT_AUTOMATION_EMAIL=%q\nGIT_AUTOMATION_SIGNING_KEY=%q\n' \
  "$automation_name" "$automation_email" "$fingerprint" > "$secret_dir/git.env"
chmod 600 "$secret_dir/git.env" "$secret_dir/signing-private.asc"
chmod 644 "$secret_dir/signing-public.asc"

echo "Generated local automation signing key: $fingerprint"
echo "Add this public key to the GitHub account for $automation_email:"
echo "  $secret_dir/signing-public.asc"
echo "Next, create github-app.env and place github-app.pem in the same directory."
