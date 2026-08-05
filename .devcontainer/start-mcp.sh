#!/usr/bin/env bash
# Bring up the self-hosted MCP backends (searxng, crawl4ai) via docker compose.
# Invoked from the devcontainer `postStartCommand`, so it runs on EVERY container
# start (through docker-in-docker).
#
# Deliberately failure-tolerant, mirroring start-litellm.sh: these are optional
# research backends for OMO agents and must never block the container from
# coming up.
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE_FILE="$SCRIPT_DIR/mcp/compose.yaml"

if ! docker compose version >/dev/null 2>&1; then
  echo "WARN: MCP: 'docker compose' not available; skipping (continuing)."
  exit 0
fi

# --- Generate local secrets on first run (both gitignored) --------------------
# Kept out of git and generated rather than committed, so no shared default
# credential ships in the repo. Both are local-only: the services bind to this
# container and are not reachable from outside it.

SEARX_SETTINGS="$SCRIPT_DIR/mcp/searxng/settings.yml"
if [ ! -f "$SEARX_SETTINGS" ]; then
  echo "==> MCP: rendering searxng settings.yml (first run)"
  secret="$(head -c 32 /dev/urandom | base64 | tr -d '/+=' | head -c 32)"
  sed "s|__SECRET_KEY__|$secret|" "$SCRIPT_DIR/mcp/searxng/settings.template.yml" > "$SEARX_SETTINGS" \
    || echo "WARN: MCP: could not render searxng settings (continuing)"
fi

MCP_ENV="$SCRIPT_DIR/mcp/mcp.env"
if [ ! -f "$MCP_ENV" ]; then
  echo "==> MCP: generating mcp.env with a crawl4ai API token (first run)"
  token="$(head -c 32 /dev/urandom | base64 | tr -d '/+=' | head -c 32)"
  printf 'CRAWL4AI_API_TOKEN=%s\n' "$token" > "$MCP_ENV" \
    || echo "WARN: MCP: could not write mcp.env (continuing)"
  chmod 600 "$MCP_ENV" 2>/dev/null || true
fi

echo "==> MCP backends: docker compose up -d"
if ! docker compose -f "$COMPOSE_FILE" up -d; then
  # Same docker-in-docker failure start-litellm.sh handles: after a devcontainer
  # rebuild, `up -d` fails with "RWLayer of container <id> is unexpectedly nil".
  # The DinD daemon persists /var/lib/docker (preserving the image cache) so the
  # container RECORD survives, but its read-write layer is invalidated when the
  # outer container is recreated. Retrying alone doesn't help — the stale record
  # must be removed. Safe to recreate: both services are stateless apart from the
  # named searxng-config volume, which is untouched by removing the container.
  echo "    'up -d' failed — removing any stale container records and retrying once"
  docker rm -f teck-searxng teck-crawl4ai >/dev/null 2>&1 || true
  docker compose -f "$COMPOSE_FILE" up -d \
    || { echo "WARN: MCP: 'docker compose up' failed after retry (continuing)."; exit 0; }
fi

echo "==> MCP backends: waiting for health ..."
for _ in $(seq 1 60); do
  if curl -fsS "http://localhost:8888/healthz" >/dev/null 2>&1 \
     && curl -fsS "http://localhost:11235/health" >/dev/null 2>&1; then
    echo "==> MCP backends: searxng :8888 and crawl4ai :11235 healthy"
    exit 0
  fi
  sleep 2
done

echo "WARN: MCP backends did not report healthy within 120s. Recent logs:"
docker compose -f "$COMPOSE_FILE" logs --tail 20 2>&1 || true
echo "     (continuing; images may still be pulling on first start)"
exit 0
