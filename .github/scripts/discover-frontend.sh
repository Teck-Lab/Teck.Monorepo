#!/usr/bin/env bash
# Discover shippable frontend apps and emit a JSON array describing each one.
#
# This is the frontend counterpart to discover-services.sh, kept SEPARATE on purpose:
# the release/build pipeline (release.yml -> reusable-build-sign-sbom.yml) is .NET-only,
# so a frontend app must never leak into that matrix.
#
# STATUS: prepared, not yet consumed. Per-app frontend SBOMs are blocked by the single
# hoisted bun.lock - apps declare almost no dependencies of their own, so a per-app SBOM
# today would just duplicate the aggregate. Until each app declares its own deps
# (un-hoist), the frontend ships one aggregate SBOM to the GitHub dependency graph
# (`frontend` correlator) only, and NO GitHub Code Scanning frontend category is created
# (an aggregate category would only have to be retired at the split). When apps un-hoist,
# wire this script into a per-app SBOM job and create the per-app GitHub Code Scanning
# categories then - once, permanently. The naming below is already the final scheme, so
# nothing is renamed.
#
# Emitted object per app:
#   app        - app directory name under src/apps (web, mobile, website, admin-webapp)
#   product    - GitHub Code Scanning SARIF category (== app; frontend apps are unambiguous, no prefix)
#   tag        - business/release-group tag (always `web`)
#   dtProject  - dependency graph correlator name (web/<app>)
#   appPath    - path to the app directory (SBOM scope once deps are un-hoisted)
#
# storybook is excluded (a dev tool, not a shipped artifact). Add other non-shipped apps
# to EXCLUDE below.
set -euo pipefail

EXCLUDE_REGEX='^(storybook)$'

objs=()
while IFS= read -r appdir; do
  app=$(basename "$appdir")
  [[ "$app" =~ $EXCLUDE_REGEX ]] && continue
  # Only real nx apps (must have a project.json).
  [ -f "$appdir/project.json" ] || continue

  objs+=("$(jq -nc \
    --arg app "$app" \
    --arg product "$app" \
    --arg tag "web" \
    --arg dtproject "web/$app" \
    --arg apppath "$appdir" \
    '{app:$app, product:$product, tag:$tag, dtProject:$dtproject, appPath:$apppath}')")
done < <(find src/apps -mindepth 1 -maxdepth 1 -type d | sort)

printf '%s\n' "${objs[@]}" | jq -s '.'
