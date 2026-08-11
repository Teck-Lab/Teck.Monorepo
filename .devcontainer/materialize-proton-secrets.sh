#!/usr/bin/env bash
set -euo pipefail

secret_root="${1:?secret output directory is required}"
case "$secret_root" in
  /run/secrets/teck-runtime|/dev/shm/teck-orca-secrets.*) ;;
  *) echo "Unexpected secret output path: $secret_root" >&2; exit 1 ;;
esac

: "${TECK_GITHUB_APP_ID:?missing TECK_GITHUB_APP_ID Proton reference}"
: "${TECK_GITHUB_APP_INSTALLATION_ID:?missing TECK_GITHUB_APP_INSTALLATION_ID Proton reference}"
: "${TECK_GITHUB_APP_PRIVATE_KEY:?missing TECK_GITHUB_APP_PRIVATE_KEY Proton reference}"
: "${TECK_LITELLM_OPENCODE_GO_KEY:?missing OpenCode Go A Proton reference}"
: "${TECK_LITELLM_OPENCODE_GO_KEY_2:?missing OpenCode Go B Proton reference}"
: "${TECK_LITELLM_DEEPSEEK_API_KEY:?missing DeepSeek Proton reference}"
: "${TECK_LITELLM_NVIDIA_API_KEY:?missing NVIDIA Proton reference}"
: "${TECK_LITELLM_OPENROUTER_API_KEY:?missing OpenRouter Proton reference}"

secret_dir="$secret_root/container"
umask 077
install -d -m 0700 "$secret_dir"
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
chmod 0600 "$secret_dir"/*
