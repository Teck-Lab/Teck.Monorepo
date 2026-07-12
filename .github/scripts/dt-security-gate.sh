#!/usr/bin/env bash
# Synchronous release gate against Dependency Track.
#
# DT is the SCA engine: it scans the uploaded CycloneDX SBOM and produces findings +
# policy violations. SecObserve pulls DT's findings ASYNCHRONOUSLY (server-side, on a
# schedule) and therefore cannot gate at the exact release moment - so the release
# gate lives here, polling DT directly for the just-uploaded project version and
# failing the build (which leaves the image unsigned) when the gate is violated.
#
# Only invoked for release/RC builds (publish-mode=release). Pre-release/preview builds
# upload their SBOM to DT for continuous, FLAGGED analysis but do not run this gate.
#
# Required env:
#   DT_HOSTNAME         - Dependency Track hostname (no scheme)
#   DT_API_KEY          - DT API key with VIEW_VULNERABILITY / VIEW_POLICY_VIOLATION
#   DT_PROJECT_NAME     - DT project name (e.g. commerce/order)
#   DT_PROJECT_VERSION  - DT project version (the release/RC version)
# Optional env:
#   DT_FAIL_ON          - comma severities that fail the gate (default CRITICAL,HIGH)
#   DT_TIMEOUT_SECONDS  - max wait for the project + analysis to appear (default 300)
#   DT_POLL_INTERVAL_SECONDS - poll interval (default 10)
#   DT_ANALYSIS_GRACE_SECONDS - grace after the project appears, for DT to finish
#                               analysing the freshly uploaded BOM (default 20)
set -euo pipefail

: "${DT_HOSTNAME:?DT_HOSTNAME required}"
: "${DT_API_KEY:?DT_API_KEY required}"
: "${DT_PROJECT_NAME:?DT_PROJECT_NAME required}"
: "${DT_PROJECT_VERSION:?DT_PROJECT_VERSION required}"

FAIL_ON="${DT_FAIL_ON:-CRITICAL,HIGH}"
TIMEOUT="${DT_TIMEOUT_SECONDS:-300}"
INTERVAL="${DT_POLL_INTERVAL_SECONDS:-10}"
GRACE="${DT_ANALYSIS_GRACE_SECONDS:-20}"

base="https://${DT_HOSTNAME}"
api_key_header="X-Api-Key: ${DT_API_KEY}"

echo "Gate: ${DT_PROJECT_NAME}@${DT_PROJECT_VERSION} (fail on: ${FAIL_ON})"

# 1. Resolve the project UUID. autocreate + BOM processing can lag, so retry.
uuid=""
deadline=$(( $(date +%s) + TIMEOUT ))
while [ -z "$uuid" ] && [ "$(date +%s)" -lt "$deadline" ]; do
  uuid=$(curl -fsS -H "$api_key_header" -G "${base}/api/v1/project/lookup" \
    --data-urlencode "name=${DT_PROJECT_NAME}" \
    --data-urlencode "version=${DT_PROJECT_VERSION}" 2>/dev/null | jq -r '.uuid // empty' || true)
  [ -z "$uuid" ] && { echo "  project not visible yet, retrying in ${INTERVAL}s..."; sleep "$INTERVAL"; }
done
if [ -z "$uuid" ]; then
  echo "::error::Dependency Track project ${DT_PROJECT_NAME}@${DT_PROJECT_VERSION} not found within ${TIMEOUT}s"
  exit 1
fi
echo "  project uuid: ${uuid}"

# 2. Give DT a moment to finish analysing the freshly uploaded BOM before reading.
sleep "$GRACE"

# 3. Fetch active (non-suppressed) findings and count the failing severities.
findings=$(curl -fsS -H "$api_key_header" "${base}/api/v1/finding/project/${uuid}?suppressed=false")
sev_count=$(echo "$findings" | jq --arg sevs "$FAIL_ON" '
  ($sevs | ascii_upcase | split(",") | map(ltrimstr(" ") | rtrimstr(" "))) as $fail
  | [ .[] | (.vulnerability.severity // "" | ascii_upcase) | select(. as $s | $fail | index($s)) ]
  | length')

# 4. Fetch FAIL-state policy violations (licence/severity/operational policies).
violations=$(curl -fsS -H "$api_key_header" "${base}/api/v1/violation/project/${uuid}?suppressed=false" || echo '[]')
viol_fail=$(echo "$violations" | jq '[ .[] | select(.policyCondition.policy.violationState == "FAIL") ] | length')

echo "  failing-severity findings: ${sev_count}"
echo "  FAIL-state policy violations: ${viol_fail}"

if [ "$sev_count" -gt 0 ] || [ "$viol_fail" -gt 0 ]; then
  echo "::error::Dependency Track gate FAILED for ${DT_PROJECT_NAME}@${DT_PROJECT_VERSION}: ${sev_count} ${FAIL_ON} finding(s), ${viol_fail} policy violation(s). Image will not be signed."
  exit 1
fi

echo "Dependency Track gate PASSED for ${DT_PROJECT_NAME}@${DT_PROJECT_VERSION}."
