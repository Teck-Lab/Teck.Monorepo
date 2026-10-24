#!/usr/bin/env bash
set -euo pipefail

# Matrix values arrive as environment data, never as executable shell source.
mapfile -d '' -t scan_paths < <(jq -j '.[] | ., "\u0000"' <<< "$SEMGREP_SCAN_PATHS")
# Bound OCaml's stack so native Semgrep does not inherit unlimited RLIMIT_STACK.
ulimit -s 8192 || true
semgrep scan \
  --config p/csharp \
  --config p/secrets \
  --config p/r2c-security-audit \
  --sarif --output "semgrep-${SEMGREP_PRODUCT}.sarif" \
  --metrics=off \
  "${scan_paths[@]}" src/shared
