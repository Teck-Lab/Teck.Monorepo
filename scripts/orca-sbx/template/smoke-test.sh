#!/usr/bin/env sh
set -eu

test "$(dotnet --version)" = "10.0.300"
test "$(bun --version)" = "1.4.0"
test "$(omp --version)" = "omp/18.0.4"
install -d -m 700 "$HOME/.omp/agent/skills/orchestration"
install -m 600 /tmp/orchestration.SKILL.md "$HOME/.omp/agent/skills/orchestration/SKILL.md"
test "$(typescript-language-server --version)" = "4.4.1"
test "$(tsc --version)" = "Version 5.9.3"
test "$(npm list --global --depth=0 --json | jq -r '.dependencies["next-devtools-mcp"].version')" = "0.4.0"
test -x "$(command -v next-devtools-mcp)"
test "$(google-chrome-stable --version | awk '{print $3}')" = "153.0.8010.36"
test -r "$HOME/.omp/agent/skills/orchestration/SKILL.md"
tr -d '\r' < "$HOME/.omp/agent/skills/orchestration/SKILL.md" | grep -q '^name: orchestration$'
google-chrome-stable --headless --no-sandbox --disable-gpu \
  --dump-dom 'data:text/html,<title>omp-browser-ready</title>' 2>/dev/null |
  grep -q omp-browser-ready
PUPPETEER_EXECUTABLE_PATH=/usr/bin/google-chrome-stable \
PUPPETEER_PROXY="${PUPPETEER_PROXY:-}" \
PUPPETEER_PROXY_IGNORE_CERT_ERRORS="${PUPPETEER_PROXY_IGNORE_CERT_ERRORS:-false}" \
  google-chrome-stable --headless --no-sandbox --disable-gpu \
  ${PUPPETEER_PROXY:+--proxy-server="$PUPPETEER_PROXY"} \
  --dump-dom 'data:text/html,<title>omp-browser-proxy-ready</title>' 2>/dev/null |
  grep -q omp-browser-proxy-ready
