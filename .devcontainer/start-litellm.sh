#!/usr/bin/env bash
# Bring up the LiteLLM gateway via docker compose. Invoked by the devcontainer
# `postStartCommand`, so it runs on EVERY container start (through docker-in-docker).
#
# A thin wrapper around `docker compose up -d`: compose owns the service
# definition (.devcontainer/litellm/compose.yaml); this script adds the two things
# compose can't do on its own — skip cleanly when no keys are present yet, and
# wait for the gateway to report healthy. Tolerant of failure so it never blocks
# the container from coming up (mirrors postCreate.sh's convenience-first style).
#
# To apply edits to config.yaml on an already-running gateway:
#   docker compose -f .devcontainer/litellm/compose.yaml up -d --force-recreate
set -uo pipefail

PORT=4000
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE_FILE="$SCRIPT_DIR/litellm/compose.yaml"
ENV_FILE="$SCRIPT_DIR/litellm/litellm.env"

if [ ! -f "$ENV_FILE" ]; then
  echo "==> LiteLLM: no key file at $ENV_FILE — skipping gateway startup."
  echo "    cp .devcontainer/litellm/litellm.env.example .devcontainer/litellm/litellm.env,"
  echo "    fill in keys, then re-run: bash .devcontainer/start-litellm.sh"
  exit 0
fi

if ! docker compose version >/dev/null 2>&1; then
  echo "WARN: LiteLLM: 'docker compose' not available; skipping gateway startup (continuing)."
  exit 0
fi

echo "==> LiteLLM: docker compose up -d"
docker compose -f "$COMPOSE_FILE" up -d \
  || { echo "WARN: LiteLLM: 'docker compose up' failed (continuing)."; exit 0; }

echo "==> LiteLLM: waiting for /health/liveliness ..."
for _ in $(seq 1 30); do
  if curl -fsS "http://localhost:${PORT}/health/liveliness" >/dev/null 2>&1; then
    echo "==> LiteLLM: gateway healthy at http://localhost:${PORT}"
    exit 0
  fi
  sleep 1
done

echo "WARN: LiteLLM: gateway did not report healthy within 30s. Recent logs:"
docker compose -f "$COMPOSE_FILE" logs --tail 20 2>&1 || true
echo "     (continuing; the container may still be starting)"
exit 0
