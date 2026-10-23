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

is_affected() {
  local nxproj="$1"
  [ -z "$affected" ] && return 0
  echo "$affected" | jq -e --arg p "$nxproj" 'index($p) != null' >/dev/null
}

objs=()
while IFS= read -r svcdir; do
  group=$(basename "$(dirname "$svcdir")")
  service=$(basename "$svcdir")
  # Shared gateway code is a library, not a deployable reverse proxy.
  [ "$group" = "gateway" ] && [ "$service" = "shared" ] && continue

  host=$(find "$svcdir" -name '*.Host.csproj' | head -1)
  if [ -z "$host" ]; then
    host=$(find "$svcdir" -maxdepth 1 -name '*.csproj' | head -1)
  fi
  [ -z "$host" ] && continue

  nxproj=$(basename "$host" .csproj)
  is_affected "$nxproj" || continue

  if [ "$group" = "gateway" ]; then
    product="gateway-$service"
  else
    product="$service"
  fi

  # Product names are the default service groups; operations is deliberately
  # one fixed multi-service group.
  release_group="$product"
  if [ "$group" = "operations" ]; then
    release_group="operations"
  fi

  objs+=("$(jq -nc \
    --arg group "$group" \
    --arg service "$service" \
    --arg product "$product" \
    --arg nxproject "$nxproj" \
    --arg projectpath "$host" \
    --arg scanpath "$svcdir" \
    --arg releasegroup "$release_group" \
    '{group:$group, service:$service, product:$product, nxProject:$nxproject, projectPath:$projectpath, scanPath:$scanpath, releaseGroup:$releasegroup}')")
done < <(find src/services -mindepth 2 -maxdepth 2 -type d | sort)

printf '%s\n' "${objs[@]}" | jq -s '.'
