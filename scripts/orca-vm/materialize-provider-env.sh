#!/usr/bin/env bash
set -euo pipefail

output="${1:?provider output file is required}"
state_root="${XDG_STATE_HOME:-$HOME/.local/state}/teck-orca/runtimes"
case "$output" in
  "$state_root/"*/providers.env) ;;
  *) echo "Refusing unexpected provider output path: $output" >&2; exit 1 ;;
esac

: "${OPENCODE_GO_KEY:?missing OpenCode Go subscription 1 reference}"
: "${OPENCODE_GO_KEY_2:?missing OpenCode Go subscription 2 reference}"
: "${DEEPSEEK_API_KEY:?missing DeepSeek reference}"
: "${OPENROUTER_API_KEY:?missing OpenRouter reference}"

umask 077
printf '%s\n' \
  "OPENCODE_GO_KEY=$OPENCODE_GO_KEY" \
  "OPENCODE_GO_KEY_2=$OPENCODE_GO_KEY_2" \
  "DEEPSEEK_API_KEY=$DEEPSEEK_API_KEY" \
  "OPENROUTER_API_KEY=$OPENROUTER_API_KEY" \
  > "$output"
chmod 0600 "$output"
