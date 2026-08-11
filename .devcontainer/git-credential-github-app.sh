#!/usr/bin/env bash
set -euo pipefail

operation="${1:-}"
[ "$operation" = get ] || exit 0

host=""
while IFS='=' read -r key value; do
  [ "$key" = host ] && host="$value"
done

[ "$host" = github.com ] || exit 0
printf 'username=x-access-token\npassword=%s\n' "$(teck-github-app-token write)"
