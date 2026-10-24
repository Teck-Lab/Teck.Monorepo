#!/usr/bin/env bash
# Discover deployable backend services and emit a JSON array describing each one.
#
# A "service" is a directory src/services/<group>/<service> that contains a
# runnable Host project. The Host csproj is either <service>/*.Host/*.Host.csproj
# (commerce services) or a runnable csproj directly in the service dir (gateway).
#
# The emitted object is the single source of truth joined against by both
# security-scans.yml (per-service source scans) and release.yml (per-service image
# build). Each field:
#   group        - release/business group (commerce, gateway, ...)
#   service      - service directory name (order, public, ...)
#   product      - service key/suffix used to construct the per-service
#                  `semgrep-${product}` GitHub Code Scanning SARIF category.
#                  Equal to the service except gateway sub-services are prefixed.
#   nxProject    - nx project name of the Host (Order.Host, Gateway.WebAdmin, ...)
#   projectPath  - path to the Host csproj (build target for the image)
#   scanPath     - directory the source scanners target for this service
#   releaseGroup - stable service grouping used for cache and scan metadata
#
# Keys are camelCase because GitHub Actions matrix access (matrix.scanPath) parses
# hyphens as subtraction; the reusable workflow maps them onto its hyphenated inputs.
#
# Optional filtering: set NX_AFFECTED to a JSON array of affected nx project names
# (e.g. the output of `nx show projects --affected --json`) to keep only services
# whose Host project is affected. Unset -> all services.
set -euo pipefail

affected="${NX_AFFECTED:-}"

# --semgrep-matrix adds the historical gateway-public SARIF category while main
# still has that category. It is not a deployable service or release product.
if [ "${1:-}" = "--semgrep-matrix" ]; then
  semgrep_matrix=true
else
  semgrep_matrix=false
fi

is_affected() {
  local nxproj="$1"
  [ -z "$affected" ] && return 0
  echo "$affected" | jq -e --arg p "$nxproj" 'index($p) != null' >/dev/null
}

objs=()
while IFS= read -r svcdir; do
  group=$(basename "$(dirname "$svcdir")")
  service=$(basename "$svcdir")

  # Find Host csproj (commerce services have a nested *.Host/ directory;
  # gateway has a direct csproj).
  host=$(find "$svcdir" -name '*.Host.csproj' | head -1)
  if [ -z "$host" ]; then
    host=$(find "$svcdir" -maxdepth 1 -name '*.csproj' | head -1)
  fi
  [ -z "$host" ] && continue

  nx_project=$(basename "$host" .csproj)
  is_affected "$nx_project" || continue

  if [ "$group" = "gateway" ]; then
    product="gateway-$service"
  else
    product="$service"
  fi
  scan_path="$svcdir"
  release_group="$product"
  if [ "$group" = "operations" ]; then
    release_group="operations"
  fi

  objs+=("$(jq -nc \
    --arg group "$group" \
    --arg service "$service" \
    --arg product "$product" \
    --arg nxproject "$nx_project" \
    --arg projectpath "$host" \
    --arg scanpath "$scan_path" \
    --arg releasegroup "$release_group" \
    '{group:$group, service:$service, product:$product, nxProject:$nxproject, projectPath:$projectpath, scanPath:$scanpath, releaseGroup:$releasegroup}')")
done < <(find src/services -mindepth 2 -maxdepth 2 -type d | sort)

if [ "$semgrep_matrix" = true ]; then
  # Scan both replacement hosts and their edge library under the retired
  # category; never satisfy the code-scanning rule with an empty SARIF upload.
  objs+=("$(jq -nc \
    '{product:"gateway-public", scanPaths:["src/services/gateway/web-admin", "src/services/gateway/web-customer", "src/services/gateway/shared"]}')")
fi

printf '%s\n' "${objs[@]}" | jq -s '.'
