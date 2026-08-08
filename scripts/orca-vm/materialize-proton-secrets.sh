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
# The TECK_LITELLM_* input names are retained for compatibility with the
# existing Proton item fields; no LiteLLM service or gateway is used.
: "${TECK_LITELLM_OPENCODE_GO_KEY:?missing OpenCode Go A Proton reference}"
: "${TECK_LITELLM_OPENCODE_GO_KEY_2:?missing TECK_LITELLM_OPENCODE_GO_KEY_2 Proton reference}"
: "${TECK_LITELLM_DEEPSEEK_API_KEY:?missing TECK_LITELLM_DEEPSEEK_API_KEY Proton reference}"
: "${TECK_LITELLM_NVIDIA_API_KEY:?missing TECK_LITELLM_NVIDIA_API_KEY Proton reference}"
: "${TECK_LITELLM_OPENROUTER_API_KEY:?missing TECK_LITELLM_OPENROUTER_API_KEY Proton reference}"

secret_dir="$runtime_dir/container"
umask 077
printf 'GITHUB_APP_ID=%s\nGITHUB_APP_INSTALLATION_ID=%s\n' \
  "$TECK_GITHUB_APP_ID" "$TECK_GITHUB_APP_INSTALLATION_ID" > "$secret_dir/github-app.env"
printf '%s\n' "$TECK_GITHUB_APP_PRIVATE_KEY" > "$secret_dir/github-app.pem"

printf '%s\n' \
  "OPENCODE_GO_KEY=$TECK_LITELLM_OPENCODE_GO_KEY" \
  "OPENCODE_GO_KEY_2=$TECK_LITELLM_OPENCODE_GO_KEY_2" \
  "DEEPSEEK_API_KEY=$TECK_LITELLM_DEEPSEEK_API_KEY" \
  "NVIDIA_API_KEY=$TECK_LITELLM_NVIDIA_API_KEY" \
  "OPENROUTER_API_KEY=$TECK_LITELLM_OPENROUTER_API_KEY" \
  > "$secret_dir/ai-providers.env"

openssl pkey -in "$secret_dir/github-app.pem" -noout -check >/dev/null
chmod 600 "$secret_dir/github-app.env" "$secret_dir/github-app.pem" "$secret_dir/ai-providers.env"
