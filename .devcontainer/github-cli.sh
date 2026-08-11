#!/usr/bin/env bash
set -euo pipefail

# Prefer credentials explicitly supplied by a caller. Otherwise mint a fresh
# installation token so ordinary gh commands use the same repository-scoped
# GitHub App as MCP and Git promotion, without persisting an expiring token.
if [ -z "${GH_TOKEN:-}" ] && [ -z "${GITHUB_TOKEN:-}" ]; then
  GH_TOKEN="$(teck-github-app-token installation)"
  export GH_TOKEN
fi

exec /usr/bin/gh "$@"
