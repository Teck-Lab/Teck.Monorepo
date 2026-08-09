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
#   ./tools/security-scan.sh --pre-push # gitleaks on refs introduced by a push (pre-push hook)
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
  --pre-push) MODE="pre-push" ;;
  ""|--changed) MODE="changed" ;;
  -h|--help) sed -n '2,14p' "$0"; exit 0 ;;
  *) echo "unknown arg: $1 (try --help)"; exit 2 ;;
esac

# Pre-push mode reads the git hook's stdin. Parse and validate every physical
# record before touching Docker or the base ref; a deletion-only push must be
# able to exit cleanly without any prerequisites.
PRE_PUSH_UPDATES=()
if [ "$MODE" = "pre-push" ]; then
  BASE_REF="origin/main"
  ZERO_SHA="0000000000000000000000000000000000000000"
  DELETE_REF="(delete)"
  while IFS= read -r line || [ -n "$line" ]; do
    # Every nonempty physical line must be a valid record.
    if [[ -z "${line//[[:space:]]/}" ]]; then
      echo "ERROR: blank line in pre-push ref update stream" >&2
      exit 2
    fi

    read -r local_ref local_sha remote_ref remote_sha extra <<< "$line"
    if [ -z "${local_ref:-}" ] || [ -z "${local_sha:-}" ] || [ -z "${remote_ref:-}" ] || [ -z "${remote_sha:-}" ] || [ -n "${extra:-}" ]; then
      echo "ERROR: malformed pre-push ref update" >&2
      exit 2
    fi
    if [[ ! "$remote_ref" =~ ^refs/ ]] || [[ ! "$remote_sha" =~ ^[0-9a-f]{40}$ ]] || [[ ! "$local_sha" =~ ^[0-9a-f]{40}$ ]]; then
      echo "ERROR: malformed pre-push ref update" >&2
      exit 2
    fi
    if [ "$local_ref" = "$DELETE_REF" ]; then
      if [ "$local_sha" != "$ZERO_SHA" ]; then
        echo "ERROR: malformed pre-push ref update" >&2
        exit 2
      fi
    elif [[ "$local_ref" =~ ^refs/ ]] || [ "$local_ref" = "HEAD" ]; then
      if [ "$local_sha" = "$ZERO_SHA" ]; then
        echo "ERROR: malformed pre-push ref update" >&2
        exit 2
      fi
    else
      echo "ERROR: malformed pre-push ref update" >&2
      exit 2
    fi

    PRE_PUSH_UPDATES+=("${local_ref} ${local_sha} ${remote_ref} ${remote_sha}")
  done

  if [ "${#PRE_PUSH_UPDATES[@]}" -eq 0 ]; then
    echo "ERROR: no valid pre-push ref updates received" >&2
    exit 2
  fi

  DELETION_ONLY=true
  for update in "${PRE_PUSH_UPDATES[@]}"; do
    read -r _ local_sha _ _ <<< "$update"
    if [ "$local_sha" != "$ZERO_SHA" ]; then
      DELETION_ONLY=false
      break
    fi
  done

  if [ "$DELETION_ONLY" = true ]; then
    echo "no commits introduced by this push — skipping security scan"
    exit 0
  fi
fi

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

# Linked worktrees use a gitfile (.git is a file). The gitfile and the
# worktree-specific Git metadata point to absolute paths outside the source
# tree, so Gitleaks inside Docker needs the worktree and both Git directories
# mounted at their original absolute paths.
GITLEAKS_SOURCE="/repo"
GITLEAKS_REPORT="/repo/.security/gitleaks.json"
GITLEAKS_MOUNTS=(-v "$REPO_ROOT:/repo")
if [ -f "$REPO_ROOT/.git" ]; then
  GITDIR=$(git -C "$REPO_ROOT" rev-parse --git-dir 2>/dev/null) || GITDIR=""
  COMMONDIR=$(git -C "$REPO_ROOT" rev-parse --git-common-dir 2>/dev/null) || COMMONDIR=""
  if [ -n "$GITDIR" ] && [ -n "$COMMONDIR" ] && [ "$GITDIR" != "$COMMONDIR" ]; then
    GITLEAKS_SOURCE="$REPO_ROOT"
    GITLEAKS_REPORT="$REPO_ROOT/.security/gitleaks.json"
    GITLEAKS_MOUNTS=(-v "$REPO_ROOT:$REPO_ROOT" -v "$GITDIR:$GITDIR:ro" -v "$COMMONDIR:$COMMONDIR:ro")
  fi
fi

GITLEAKS_CMD=(detect)
GITLEAKS_OPTIONS=(--source="$GITLEAKS_SOURCE" --redact --no-banner
  --report-format=json --report-path="$GITLEAKS_REPORT")

if [ "$MODE" = "staged" ]; then
  GITLEAKS_CMD=(protect --staged)
elif [ "$MODE" = "pre-push" ]; then
  git -C "$REPO_ROOT" rev-parse --verify --quiet "${BASE_REF}^{commit}" >/dev/null \
    || { echo "ERROR: cannot resolve origin/main; run 'git fetch origin main'" >&2; exit 2; }

  GITLEAKS_RANGES=()
  for update in "${PRE_PUSH_UPDATES[@]}"; do
    read -r local_ref local_sha remote_ref remote_sha <<< "$update"
    [ "$local_sha" = "$ZERO_SHA" ] && continue
    git -C "$REPO_ROOT" rev-parse --verify --quiet "${local_sha}^{commit}" >/dev/null \
      || { echo "ERROR: cannot resolve pushed commit $local_sha" >&2; exit 2; }
    GITLEAKS_RANGES+=("${BASE_REF}..${local_sha}")
  done

  GITLEAKS_OPTIONS+=(--log-opts="${GITLEAKS_RANGES[*]}")
fi

if docker run --rm "${GITLEAKS_MOUNTS[@]}" "$GITLEAKS_IMAGE" \
     "${GITLEAKS_CMD[@]}" "${GITLEAKS_OPTIONS[@]}" 2>&1 | tail -20; then
  echo "PASS: no secrets detected"
else
  echo "FAIL: secrets detected -> .security/gitleaks.json (this BLOCKS merge in CI)"
  FAILED=1
fi
if [ "$MODE" = "secrets" ] || [ "$MODE" = "staged" ]; then echo; echo "done ($MODE-only)"; exit $FAILED; fi

# ------------------------------------------------------------------- SAST ----
echo
echo "--- [2] Semgrep SAST (${SEMGREP_IMAGE##*:}) ---"
SEMGREP_ROOT="/src"
SEMGREP_WORKDIR="/src"
SEMGREP_MOUNTS=(-v "$REPO_ROOT:/src")
if [ -f "$REPO_ROOT/.git" ] && [ -n "${GITDIR:-}" ] && [ -n "${COMMONDIR:-}" ] && [ "$GITDIR" != "$COMMONDIR" ]; then
  SEMGREP_ROOT="$REPO_ROOT"
  SEMGREP_WORKDIR="$REPO_ROOT"
  SEMGREP_MOUNTS=(-v "$REPO_ROOT:$REPO_ROOT" -v "$GITDIR:$GITDIR:ro" -v "$COMMONDIR:$COMMONDIR:ro")
fi
SEMGREP_TARGET="$SEMGREP_ROOT"
if [ "$MODE" = "changed" ] || [ "$MODE" = "pre-push" ]; then
  # Collect changed paths but keep only regular tracked files (modes 100644/100755).
  # Symlinks (120000) and other git objects must not be passed to Semgrep because
  # their targets may resolve outside the Docker /src mount and fail the scan.
  mapfile -t CHANGED < <(git -C "$REPO_ROOT" diff --raw --diff-filter=ACMR "$BASE_REF"...HEAD 2>/dev/null;
                         git -C "$REPO_ROOT" diff --raw --diff-filter=ACMR HEAD 2>/dev/null)
  mapfile -t CHANGED < <(printf '%s\n' "${CHANGED[@]}" | \
    awk -F'\t' 'NF>=2 {split($1,a," "); m=a[2]; if (m=="100644" || m=="100755") { gsub(/^"|"$/,"",$NF); print $NF }}' | \
    sort -u | grep -v '^$')
  if [ "${#CHANGED[@]}" -eq 0 ]; then
    echo "no changed files vs $BASE_REF — skipping SAST (use --all to force)"
    SEMGREP_TARGET=""
  else
    echo "scanning ${#CHANGED[@]} changed file(s) vs $BASE_REF"
    SEMGREP_TARGET="$(printf "$SEMGREP_ROOT/%s " "${CHANGED[@]}")"
  fi
fi

if [ -n "$SEMGREP_TARGET" ]; then
  # shellcheck disable=SC2086
  if docker run --rm "${SEMGREP_MOUNTS[@]}" -w "$SEMGREP_WORKDIR" "$SEMGREP_IMAGE" \
       semgrep "${SEMGREP_CONFIGS[@]}" --error --quiet \
       --sarif --output "$SEMGREP_ROOT/.security/semgrep.sarif" $SEMGREP_TARGET 2>&1 | tail -30; then
    echo "PASS: no Semgrep findings"
  else
    echo "FAIL: Semgrep findings -> .security/semgrep.sarif (CI uploads SARIF to GitHub Code Scanning)"
    FAILED=1
  fi
fi

# -------------------------------------------------------------------- SCA ----
echo
echo "--- [3] Trivy: dependency vulnerabilities ---"
# CI evaluates dependencies from a source checkout (SBOM), so scan the same view:
# skip agent worktrees/runtime caches (.claude/.omo) and regenerable build
# outputs (bin/obj), whose stale lock files/deps.json would otherwise report
# versions CI never sees.
if docker run --rm -v "$REPO_ROOT:/src" "$TRIVY_IMAGE" \
     fs --scanners vuln --severity HIGH,CRITICAL --exit-code 1 --quiet \
     --skip-dirs "/src/.claude" --skip-dirs "/src/.omo" \
     --skip-dirs "**/bin" --skip-dirs "**/obj" /src 2>&1 | tail -30; then
  echo "PASS: no HIGH/CRITICAL dependency vulnerabilities"
else
  echo "FAIL: HIGH/CRITICAL dependency vulns (Trivy is the direct local dependency scan/report source; Dependabot/dependency review governs PR dependency policy)"
  FAILED=1
fi

echo
echo "=============================================="
[ "$FAILED" -eq 0 ] && echo " RESULT: CLEAN" || echo " RESULT: FINDINGS — see .security/"
echo "=============================================="
exit $FAILED
