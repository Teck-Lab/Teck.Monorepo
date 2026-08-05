#!/usr/bin/env bash
set -euo pipefail

access="${1:-}"
[ "$#" -ge 3 ] && [ "$2" = -- ] || {
  echo "Usage: teck-git-with-github-app read|write -- git <args...>" >&2
  exit 2
}
shift 2
[ "${1:-}" = git ] || { echo "Only Git commands are supported." >&2; exit 2; }

if command -v teck-github-app-token >/dev/null 2>&1; then
  token_helper=teck-github-app-token
else
  token_helper="$(dirname "$0")/github-app-token.sh"
fi
token="$("$token_helper" "$access")"
askpass="$(mktemp)"
trap 'rm -f "$askpass"' EXIT
printf '%s\n' '#!/usr/bin/env bash' \
  'case "$1" in *Username*) echo x-access-token;; *Password*) echo "$TECK_GITHUB_APP_TOKEN";; esac' \
  > "$askpass"
chmod 700 "$askpass"
TECK_GITHUB_APP_TOKEN="$token" GIT_ASKPASS="$askpass" GIT_TERMINAL_PROMPT=0 "$@"
