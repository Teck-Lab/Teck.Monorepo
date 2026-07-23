#!/usr/bin/env bash
# Submit a CycloneDX SBOM to the GitHub dependency graph via the dependency
# submission API (POST /repos/{owner}/{repo}/dependency-graph/snapshots).
# The graph shows the SBOM's packages per correlator, and Dependabot alerts fire
# on known-vulnerable submitted deps.
#
# Usage:
#   submit-sbom-dependency-graph.sh <sbom-file> <correlator>
# Env: GITHUB_TOKEN, GITHUB_REPOSITORY, GITHUB_SHA, GITHUB_REF (required),
#      GITHUB_RUN_ID (optional), DETECTOR_NAME (optional), DRY_RUN=1 (print, don't POST)
set -euo pipefail

SBOM_FILE="${1:?usage: submit-sbom-dependency-graph.sh <sbom-file> <correlator>}"
CORRELATOR="${2:?usage: submit-sbom-dependency-graph.sh <sbom-file> <correlator>}"
DETECTOR_NAME="${DETECTOR_NAME:-teck-sbom-submission}"
REPO="${GITHUB_REPOSITORY:?GITHUB_REPOSITORY is required}"
SHA="${GITHUB_SHA:?GITHUB_SHA is required}"
REF="${GITHUB_REF:?GITHUB_REF is required}"
RUN_ID="${GITHUB_RUN_ID:-local}"
SCANNED="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

PAYLOAD="$(jq -n \
  --arg sha "$SHA" --arg ref "$REF" --arg correlator "$CORRELATOR" \
  --arg runid "$RUN_ID" --arg detector "$DETECTOR_NAME" \
  --arg repourl "https://github.com/$REPO" --arg scanned "$SCANNED" \
  --slurpfile sbom "$SBOM_FILE" \
  '{
    version: 0,
    sha: $sha,
    ref: $ref,
    job: { correlator: $correlator, id: $runid },
    detector: { name: $detector, version: "1.0.0", url: $repourl },
    scanned: $scanned,
    manifests: {
      ($correlator): {
        name: $correlator,
        resolved: (
          [ $sbom[0].components[]? | select(.purl? != null and .purl != "")
            | { key: .purl, value: { package_url: .purl } } ]
          | from_entries
        )
      }
    }
  }')"

if [ "${DRY_RUN:-0}" = "1" ]; then
  echo "$PAYLOAD"
  exit 0
fi

: "${GITHUB_TOKEN:?GITHUB_TOKEN is required}"
# gh api authenticates with GITHUB_TOKEN automatically inside Actions.
echo "$PAYLOAD" | gh api "repos/$REPO/dependency-graph/snapshots" \
  -X POST --input - \
  --jq '.result + ": " + .message'
