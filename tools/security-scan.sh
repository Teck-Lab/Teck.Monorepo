#!/usr/bin/env bash
# Local security scans that MIRROR CI (.github/workflows/security-scans.yml).
#
# Scanners run in Docker pinned to the same versions/configs CI uses, so local
# findings match the CI gate instead of drifting from it. Nothing is installed
# into the image (and the container's python3 stdlib is broken, so `pip install
# semgrep` is not a reliable path here).
#
#   ./tools/security-scan.sh            # changed files vs the base branch (fast)
#   ./tools/security-scan.sh --all      # whole repo
#   ./tools/security-scan.sh --secrets  # gitleaks only (fast; the CI hard gate)
#   ./tools/security-scan.sh --staged   # gitleaks on staged changes (pre-commit hook)
#
# Exit codes: 0 = clean, 1 = findings, 2 = could not run (docker/network).
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT_DIR="$REPO_ROOT/.security"
BASE_REF="${SECURITY_SCAN_BASE:-origin/main}"

# Versions pinned to CI. Semgrep 1.97.0 + these three configs come straight from
# .github/workflows/security-scans.yml — keep them in sync or local != CI.
SEMGREP_IMAGE="semgrep/semgrep:1.97.0"
SEMGREP_CONFIGS=(--config p/csharp --config p/secrets --config p/r2c-security-audit)
# Exact image+tag CI uses (security-scans.yml). Do NOT "upgrade" this casually:
# a newer gitleaks has different rules and will report findings the CI gate does
# not, which is how local scans lose their meaning.
GITLEAKS_IMAGE="ghcr.io/gitleaks/gitleaks:v8.18.4"
TRIVY_IMAGE="aquasec/trivy:0.58.0"

MODE="changed"
case "${1:-}" in
  --all)     MODE="all" ;;
  --secrets) MODE="secrets" ;;
  --staged)  MODE="staged" ;;
  ""|--changed) MODE="changed" ;;
  -h|--help) sed -n '2,12p' "$0"; exit 0 ;;
  *) echo "unknown arg: $1 (try --help)"; exit 2 ;;
esac

command -v docker >/dev/null 2>&1 || { echo "ERROR: docker not available"; exit 2; }
mkdir -p "$OUT_DIR"
FAILED=0

echo "=============================================="
echo " Local security scan (mode: $MODE)"
echo " Mirrors .github/workflows/security-scans.yml"
echo "=============================================="

# ---------------------------------------------------------------- secrets ----
# Gitleaks is a HARD GATE in CI, so it runs in every mode.
echo
echo "--- [1] Gitleaks: secret detection ---"
GITLEAKS_CMD=(detect)
[ "$MODE" = "staged" ] && GITLEAKS_CMD=(protect --staged)
if docker run --rm -v "$REPO_ROOT:/repo" "$GITLEAKS_IMAGE" \
     "${GITLEAKS_CMD[@]}" --source=/repo --redact --no-banner \
     --report-format=json --report-path=/repo/.security/gitleaks.json 2>&1 | tail -20; then
  echo "PASS: no secrets detected"
else
  echo "FAIL: secrets detected -> .security/gitleaks.json (this BLOCKS merge in CI)"
  FAILED=1
fi
if [ "$MODE" = "secrets" ] || [ "$MODE" = "staged" ]; then echo; echo "done ($MODE-only)"; exit $FAILED; fi

# ------------------------------------------------------------------- SAST ----
echo
echo "--- [2] Semgrep SAST (${SEMGREP_IMAGE##*:}) ---"
SEMGREP_TARGET="/src"
if [ "$MODE" = "changed" ]; then
  mapfile -t CHANGED < <(git -C "$REPO_ROOT" diff --name-only --diff-filter=ACMR "$BASE_REF"...HEAD 2>/dev/null;
                         git -C "$REPO_ROOT" diff --name-only --diff-filter=ACMR HEAD 2>/dev/null)
  mapfile -t CHANGED < <(printf '%s\n' "${CHANGED[@]}" | sort -u | grep -v '^$')
  if [ "${#CHANGED[@]}" -eq 0 ]; then
    echo "no changed files vs $BASE_REF — skipping SAST (use --all to force)"
    SEMGREP_TARGET=""
  else
    echo "scanning ${#CHANGED[@]} changed file(s) vs $BASE_REF"
    SEMGREP_TARGET="$(printf '/src/%s ' "${CHANGED[@]}")"
  fi
fi

if [ -n "$SEMGREP_TARGET" ]; then
  # shellcheck disable=SC2086
  if docker run --rm -v "$REPO_ROOT:/src" -w /src "$SEMGREP_IMAGE" \
       semgrep "${SEMGREP_CONFIGS[@]}" --error --quiet \
       --sarif --output /src/.security/semgrep.sarif $SEMGREP_TARGET 2>&1 | tail -30; then
    echo "PASS: no Semgrep findings"
  else
    echo "FAIL: Semgrep findings -> .security/semgrep.sarif (CI sends these to SecObserve)"
    FAILED=1
  fi
fi

# -------------------------------------------------------------------- SCA ----
echo
echo "--- [3] Trivy: dependency vulnerabilities ---"
# CI evaluates dependencies from a source checkout (SBOM), so scan the same view:
# skip agent worktrees (.claude) and regenerable build outputs (bin/obj), whose
# stale lock files/deps.json would otherwise report versions CI never sees.
if docker run --rm -v "$REPO_ROOT:/src" "$TRIVY_IMAGE" \
     fs --scanners vuln --severity HIGH,CRITICAL --exit-code 1 --quiet \
     --skip-dirs "/src/.claude" --skip-dirs "**/bin" --skip-dirs "**/obj" /src 2>&1 | tail -30; then
  echo "PASS: no HIGH/CRITICAL dependency vulnerabilities"
else
  echo "FAIL: HIGH/CRITICAL dependency vulns (CI gates these via Dependency-Track)"
  FAILED=1
fi

echo
echo "=============================================="
[ "$FAILED" -eq 0 ] && echo " RESULT: CLEAN" || echo " RESULT: FINDINGS — see .security/"
echo "=============================================="
exit $FAILED
