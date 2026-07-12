#!/usr/bin/env bash
# Discover deployable backend services and emit a JSON array describing each one.
#
# A "service" is a directory src/services/<group>/<service> that contains a
# runnable Host project. The Host csproj is either <service>/*.Host/*.Host.csproj
# (commerce services) or a single csproj directly in the service dir (gateway).
#
# The emitted object is the single source of truth joined against by both
# security-scans.yml (per-service source scans) and release.yml (per-service image
# build). Each field:
#   group        - release/business group (commerce, gateway, ...)
#   service      - service directory name (order, public, ...)
#   product      - SecObserve product name AND nx release group name. Equal to the
#                  service, except gateway sub-services are prefixed (gateway-public)
#                  because "public"/"internal" are ambiguous on their own.
#   nxProject    - nx project name of the Host (Order.Host, Gateway.Public, ...)
#   projectPath  - path to the Host csproj (build target for the image)
#   scanPath     - directory the source scanners target for this service
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

  objs+=("$(jq -nc \
    --arg group "$group" \
    --arg service "$service" \
    --arg product "$product" \
    --arg nxproject "$nxproj" \
    --arg projectpath "$host" \
    --arg scanpath "$svcdir" \
    '{group:$group, service:$service, product:$product, nxProject:$nxproject, projectPath:$projectpath, scanPath:$scanpath}')")
done < <(find src/services -mindepth 2 -maxdepth 2 -type d | sort)

printf '%s\n' "${objs[@]}" | jq -s '.'
