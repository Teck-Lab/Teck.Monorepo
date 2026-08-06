#!/usr/bin/env bash
set -euo pipefail

access="${1:-read}"
secret_dir="${2:-${TECK_GITHUB_SECRET_DIR:-/run/secrets/teck-github}}"
case "$access" in
  read) permissions='{"contents":"read"}' ;;
  write) permissions='{"contents":"write"}' ;;
  projects-read) permissions='{"organization_projects":"read"}' ;;
  projects-write) permissions='{"organization_projects":"write"}' ;;
  *)
    echo "Usage: $0 read|write|projects-read|projects-write [secret-dir]" >&2
    exit 2
    ;;
esac

set -a
# shellcheck disable=SC1090
. "$secret_dir/github-app.env"
set +a
: "${GITHUB_APP_ID:?missing GITHUB_APP_ID}"
: "${GITHUB_APP_INSTALLATION_ID:?missing GITHUB_APP_INSTALLATION_ID}"
[ -s "$secret_dir/github-app.pem" ] || { echo "Missing GitHub App private key." >&2; exit 1; }

jwt="$(GITHUB_APP_PRIVATE_KEY_PATH="$secret_dir/github-app.pem" node - <<'NODE'
const crypto = require("node:crypto");
const fs = require("node:fs");
const now = Math.floor(Date.now() / 1000);
const encode = (value) => Buffer.from(JSON.stringify(value)).toString("base64url");
const header = encode({ alg: "RS256", typ: "JWT" });
const payload = encode({ iat: now - 60, exp: now + 540, iss: process.env.GITHUB_APP_ID });
const input = `${header}.${payload}`;
const signature = crypto.sign(
  "RSA-SHA256",
  Buffer.from(input),
  fs.readFileSync(process.env.GITHUB_APP_PRIVATE_KEY_PATH),
).toString("base64url");
process.stdout.write(`${input}.${signature}`);
NODE
)"

response="$(mktemp)"
trap 'rm -f "$response"' EXIT
status="$(curl -sS -o "$response" -w '%{http_code}' -X POST \
  -H 'Accept: application/vnd.github+json' \
  -H "Authorization: Bearer $jwt" \
  -H 'X-GitHub-Api-Version: 2022-11-28' \
  "https://api.github.com/app/installations/$GITHUB_APP_INSTALLATION_ID/access_tokens" \
  -d "{\"permissions\":$permissions}")"
if [ "$status" != 201 ]; then
  message="$(jq -r '.message // "unknown GitHub error"' "$response")"
  echo "Could not mint GitHub App $access token (HTTP $status): $message" >&2
  exit 1
fi
jq -er '.token' "$response"
