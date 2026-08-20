#!/usr/bin/env bash
set -euo pipefail

test -s "$HOME/.config/opencode/opencode.json" || {
  echo "OpenCode configuration is missing" >&2
  exit 1
}

exec opencode --agent "Prometheus - Plan Builder"
