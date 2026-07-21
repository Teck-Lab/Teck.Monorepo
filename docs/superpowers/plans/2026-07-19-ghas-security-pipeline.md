# GHAS Security Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrate all security scan results and SBOMs from SecObserve + Dependency-Track to GitHub Advanced Security (code scanning, dependency graph, Dependabot, dependency review, Trivy release gate).

**Architecture:** 1:1 in-place replacement per the approved spec `docs/superpowers/specs/2026-07-19-ghas-security-pipeline-design.md`: Semgrep SARIF → code scanning (per-service `category`), CycloneDX SBOMs → dependency-graph snapshots via a small repo script, DT release gate → Trivy `--exit-code 1` gate in the same job id, PR dependency gate → `dependency-review-action`, Dependabot via new `dependabot.yml`. Gitleaks and all cosign/VEX/signing flows are untouched.

**Tech Stack:** GitHub Actions YAML, bash + jq (snapshot transform), `gh api` (dependency submission), Dependabot v2 config.

## Global Constraints

- Pin every new third-party action by full SHA with a version comment (repo convention):
  - `github/codeql-action/upload-sarif@d03f4cc0498d5a63039d157eb274d9c2fc50f17a` `# v3.37.1`
  - `actions/dependency-review-action@2031cfc080254a8a887f58cffee85186f0e49e48` `# v4.9.0`
- Commits are conventional (`type(scope): description`), English, and GPG-signed (repo `commit.gpgsign=true`). Husky pre-commit (Biome + staged Gitleaks) and pre-push (full `tools/security-scan.sh`) fire on every commit/push — they must pass.
- Do NOT touch: the `gitleaks` job, all cosign/sign-sboms/generate-vex/sign-image signing logic, `ci.yml`, `deploy/`.
- Keep the reusable's `security-gate` job id and `sign-image.needs` list intact.
- `security-scans.yml` uses unpinned `actions/checkout@v4` — match that file's local convention there; the reusable uses SHA-pinned checkouts — match its convention there.
- **Spec deviation (approved-design amendment):** the spec said the release gate reuses the VEX-sourcing scan ("no second scan"). This plan instead has the `security-gate` job run its own release-only Trivy scan with `--exit-code 1`, because exit-coding the VEX scan would fail `scan-vulnerabilities` and silently skip VEX generation + leave a hollow gate job. Cost: one extra image scan, release builds only.
- YAML validation command used throughout: `npx -y js-yaml <file> > /dev/null` (exit 0 = valid).

---

### Task 1: SBOM → dependency-graph submission script

**Files:**
- Create: `.github/scripts/submit-sbom-dependency-graph.sh`

**Interfaces:**
- Consumes: a CycloneDX SBOM file whose `.components[]` carry `purl` fields (anchore/syft output from `generate-sboms`, and Trivy's frontend CycloneDX — both populate `purl`).
- Produces: CLI contract used by Tasks 2 and 3 —
  `submit-sbom-dependency-graph.sh <sbom-file> <correlator>` with env `GITHUB_TOKEN`, `GITHUB_REPOSITORY`, `GITHUB_SHA`, `GITHUB_REF`, optional `GITHUB_RUN_ID`; `DRY_RUN=1` prints the snapshot payload and exits 0 without POSTing. Exit 0 on HTTP 201, non-zero otherwise.

- [ ] **Step 1: Write the failing test (sample SBOM + payload assertion)**

Create `/tmp/sample.cdx.json`:

```json
{
  "bomFormat": "CycloneDX",
  "components": [
    { "name": "MessagePack", "version": "3.1.7", "purl": "pkg:nuget/MessagePack@3.1.7" },
    { "name": "no-purl-component" },
    { "name": "react", "version": "19.0.0", "purl": "pkg:npm/react@19.0.0" }
  ]
}
```

Create `/tmp/test-submit.sh`:

```bash
#!/usr/bin/env bash
set -euo pipefail
OUT=$(DRY_RUN=1 GITHUB_REPOSITORY=Teck-Lab/Teck.Monorepo GITHUB_SHA=abc123 \
      GITHUB_REF=refs/heads/main .github/scripts/submit-sbom-dependency-graph.sh \
      /tmp/sample.cdx.json sbom/commerce/order)
echo "$OUT" | jq -e '.version == 0' >/dev/null
echo "$OUT" | jq -e '.job.correlator == "sbom/commerce/order"' >/dev/null
echo "$OUT" | jq -e '.manifests["sbom/commerce/order"].resolved["pkg:nuget/MessagePack@3.1.7"].package_url == "pkg:nuget/MessagePack@3.1.7"' >/dev/null
echo "$OUT" | jq -e '[.manifests["sbom/commerce/order"].resolved | keys[]] | length == 2' >/dev/null
echo "PAYLOAD_OK"
```

Expected length is 2: the component without a `purl` is dropped.

- [ ] **Step 2: Run the test to verify it fails**

Run: `bash /tmp/test-submit.sh`
Expected: FAIL — `.github/scripts/submit-sbom-dependency-graph.sh: No such file or directory`

- [ ] **Step 3: Write the script**

Create `.github/scripts/submit-sbom-dependency-graph.sh`:

```bash
#!/usr/bin/env bash
# Submit a CycloneDX SBOM to the GitHub dependency graph via the dependency
# submission API (POST /repos/{owner}/{repo}/dependency-graph/snapshots).
# Replaces the Dependency Track upload: the graph shows the SBOM's packages per
# correlator, and Dependabot alerts fire on known-vulnerable submitted deps.
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
```

Make it executable: `chmod +x .github/scripts/submit-sbom-dependency-graph.sh`

- [ ] **Step 4: Run the test to verify it passes**

Run: `bash /tmp/test-submit.sh`
Expected: `PAYLOAD_OK`

- [ ] **Step 5: Commit**

```bash
git add .github/scripts/submit-sbom-dependency-graph.sh
git commit -m "feat(ci): add SBOM to dependency-graph submission script"
```

---

### Task 2: security-scans.yml → code scanning + dependency review + SBOM submission

**Files:**
- Modify: `.github/workflows/security-scans.yml`

**Interfaces:**
- Consumes: `submit-sbom-dependency-graph.sh` from Task 1 (invoked as `.github/scripts/submit-sbom-dependency-graph.sh sbom-frontend.cyclonedx.json sbom/frontend`); the existing `semgrep-${{ matrix.product }}.sarif` files.
- Produces: code-scanning alerts categorized by `${{ matrix.product }}`; a `dependency-review` PR gate; frontend SBOM snapshots in the dependency graph under correlator `sbom/frontend`.

- [ ] **Step 1: Run the "before" assertions to verify they fail**

Run:
```bash
grep -c "SecObserve" .github/workflows/security-scans.yml
grep -c "dependency-review-action" .github/workflows/security-scans.yml
```
Expected: first prints a number > 0 (SecObserve still present); second prints 0 / exits 1 (gate missing).

- [ ] **Step 2: Replace the header comment and env (lines 12-31, 39-41)**

Replace lines 12-31 (the `Findings are imported into SecObserve…` block through the `See .github/scripts/discover-frontend.sh…` line) with:

```yaml
# Findings go to GitHub Advanced Security: Semgrep SARIF -> code scanning (one
# category per service), frontend SBOM -> dependency graph (correlator
# sbom/frontend), dependency-review gates PRs on new HIGH+ dependencies.
# Gitleaks is the repo-wide secrets hard gate and stays CI-fail-only.
# Per-service topology (see .github/scripts/discover-services.sh):
#   code-scanning category = nx release group = the `product` field.
# No external security services or secrets are required.
```

Delete the `env:` block (lines 39-41: `SO_API_BASE_URL`, `SO_BRANCH_NAME`) entirely.

Change the top-level `permissions:` block (lines 32-33) to:

```yaml
permissions:
  contents: read
  security-events: write
```

- [ ] **Step 3: Replace the SecObserve upload step in the semgrep job (lines 126-137)**

Replace the step `Import Semgrep results into SecObserve` (and its preceding comment lines 126-128) with:

```yaml
      # Sink = GHAS code scanning. category keeps the per-service split that
      # SecObserve products provided; alerts annotate PRs and land in the
      # Security tab. Exit codes: Semgrep itself still exits 0 on findings;
      # the merge gate is the branch-protection code-scanning rule (repo setting).
      - name: Upload Semgrep SARIF to code scanning
        uses: github/codeql-action/upload-sarif@d03f4cc0498d5a63039d157eb274d9c2fc50f17a # v3.37.1
        with:
          sarif_file: semgrep-${{ matrix.product }}.sarif
          category: ${{ matrix.product }}
```

- [ ] **Step 4: Add the dependency-review PR gate**

Replace the comment block at lines 139-142 (`# Dependency SCA is intentionally NOT run here…`) and lines 162-166 (`# No GitHub-native dependency-review job…`) with:

```yaml
  # Source-level SCA: dependency graph (native manifests + submitted SBOMs) +
  # Dependabot alerts/fix PRs. PR-time new-dependency gate below; the
  # release-time image gate lives in reusable-build-sign-sbom.yml.

  dependency-review:
    name: Dependency Review (PR gate)
    runs-on: ubuntu-latest
    if: github.event_name == 'pull_request'
    permissions:
      contents: read
    steps:
      - uses: actions/checkout@v4

      - name: Dependency review
        uses: actions/dependency-review-action@2031cfc080254a8a887f58cffee85186f0e49e48 # v4.9.0
        with:
          fail-on-severity: high
```

(Delete both comment blocks — lines 139-142 and lines 162-166 — and insert the comment + `dependency-review` job above between the `semgrep` and `gitleaks` jobs. Keep the surrounding jobs' YAML anchors intact.)

- [ ] **Step 5: Replace the frontend-sbom DT upload and its comment (lines 183-196, 227-237)**

Replace the job's leading comment (lines 183-196) with:

```yaml
  frontend-sbom:
    # Frontend SCA + licenses. One CycloneDX SBOM for the whole Bun workspace
    # from the root bun.lock, submitted to the GitHub dependency graph
    # (correlator sbom/frontend) so Dependabot alerts cover frontend deps.
    # Runs off-PR; PR-time coverage is dependency-review above.
    #
    # Trivy parses bun.lock reliably; cdxgen@11 does NOT for this lockfileVersion
    # (it yields an empty BOM), so Trivy is the engine here.
    #
    # Per-app SBOMs stay deferred exactly as before: wire
    # .github/scripts/discover-frontend.sh into a per-app job when apps declare
    # their own deps (correlators web/<app>).
```

Replace the step `Upload SBOM to Dependency Track` (lines 227-237) with:

```yaml
      - name: Submit frontend SBOM to the dependency graph
        env:
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
        run: .github/scripts/submit-sbom-dependency-graph.sh sbom-frontend.cyclonedx.json sbom/frontend
```

Add explicit permissions to the `frontend-sbom` job (immediately under its `runs-on: ubuntu-latest` line):

```yaml
    permissions:
      contents: write
```

- [ ] **Step 6: Delete the SecObserve merge-gate job (lines 239-258)**

Delete the entire `security-gate` job (the `SecObserve Merge Gate (${{ matrix.product }})` job and its leading comment lines 239-243). The replacement gate is the branch-protection code-scanning rule configured in repo settings (spec §7) — no YAML.

- [ ] **Step 7: Run the "after" assertions**

Run:
```bash
npx -y js-yaml .github/workflows/security-scans.yml > /dev/null && echo "YAML_OK"
grep -c "SecObserve" .github/workflows/security-scans.yml || echo "SECOBSERVE_GONE"
grep -c "DEPENDENCYTRACK" .github/workflows/security-scans.yml || echo "DT_GONE"
grep -c "upload-sarif\|dependency-review-action" .github/workflows/security-scans.yml
```
Expected: `YAML_OK`, `SECOBSERVE_GONE`, `DT_GONE`, then `2`.

- [ ] **Step 8: Commit**

```bash
git add .github/workflows/security-scans.yml
git commit -m "feat(ci): move semgrep to code scanning, add dependency-review, submit frontend SBOM to dependency graph"
```

---

### Task 3: reusable-build-sign-sbom.yml → SBOM submission + Trivy release gate

**Files:**
- Modify: `.github/workflows/reusable-build-sign-sbom.yml`

**Interfaces:**
- Consumes: `submit-sbom-dependency-graph.sh` from Task 1 (invoked with the per-service CycloneDX artifact and correlator `sbom/{group}/{service}`); existing artifacts `sbom-cyclonedx-{group}-{service}`; `needs.build-and-push.outputs.image-ref`.
- Produces: job `submit-sbom-dependency-graph` (replaces `upload-sbom-dependency-track`); `security-gate` job id preserved but now a release-only Trivy `--exit-code 1` scan; `sign-image.needs` unchanged: `[build-and-push, scan-vulnerabilities, security-gate, generate-sboms]`.

- [ ] **Step 1: Run the "before" assertions**

Run:
```bash
grep -c "dependencytrack" .github/workflows/reusable-build-sign-sbom.yml
```
Expected: > 0 (DT still wired).

- [ ] **Step 2: Remove the DT secret declaration (lines 52-54)**

Delete from the `secrets:` block:

```yaml
      dependencytrack-api-key:
        description: Dependency Track API key for uploading the SBOM and reading the gate
        required: true
```

Also update the `is-latest` input description (lines 41-47): replace `Mark this SBOM as the Dependency Track project's latest version…` with:

```yaml
      is-latest:
        description: >-
          Retained for caller compatibility; no longer consumed after the
          Dependency Track retirement. Only GA releases pass true.
        required: false
        type: boolean
        default: false
```

- [ ] **Step 3: Replace upload-sbom-dependency-track with submit-sbom-dependency-graph (lines 295-320)**

Replace the entire job with:

```yaml
  submit-sbom-dependency-graph:
    name: Submit SBOM to dependency graph for ${{ inputs.service-group }}/${{ inputs.service-name }}
    runs-on: ubuntu-latest
    needs: [generate-sboms]
    permissions:
      contents: write
    steps:
      - name: Download CycloneDX SBOM
        uses: actions/download-artifact@3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c # v8.0.1
        with:
          name: sbom-cyclonedx-${{ inputs.service-group }}-${{ inputs.service-name }}

      - name: Checkout repository (submission script)
        uses: actions/checkout@de0fac2e4500dabe0009e67214ff5f5447ce83dd # v6.0.2
        with:
          ref: ${{ inputs.source-ref != '' && inputs.source-ref || format('refs/tags/v{0}', inputs.version) }}

      # The dependency graph becomes the SCA engine + SBOM registry: packages
      # land per correlator (sbom/{group}/{service}) and Dependabot alerts fire
      # on known-vulnerable submissions.
      - name: Submit CycloneDX SBOM to the dependency graph
        env:
          GITHUB_TOKEN: ${{ secrets.github-token }}
        run: .github/scripts/submit-sbom-dependency-graph.sh "sbom-${{ inputs.service-group }}-${{ inputs.service-name }}.cyclonedx.json" "sbom/${{ inputs.service-group }}/${{ inputs.service-name }}"
```

- [ ] **Step 4: Rewrite the security-gate job as the Trivy release gate (lines 216-243)**

Replace the entire `security-gate` job with:

```yaml
  security-gate:
    name: Trivy release gate for ${{ inputs.service-group }}/${{ inputs.service-name }}
    runs-on: ubuntu-latest
    needs: [build-and-push]
    permissions:
      contents: read
      packages: read
    steps:
      - name: Log in to GitHub Container Registry
        if: inputs.publish-mode == 'release'
        uses: docker/login-action@4907a6ddec9925e35a0a9e82d7399ccc52663121 # v4.1.0
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.github-token }}

      # Release gate (replaces the Dependency Track gate): enforced ONLY for
      # release/RC builds. The same Trivy engine the pre-push hook runs, with an
      # exit code on HIGH/CRITICAL. A failure exits non-zero, which fails this
      # job and therefore skips sign-image, leaving the image unsigned (and
      # rejected by the cosign-verify deploy policy). Pre-release/preview builds
      # skip the gate; their SBOM is still submitted (flagged), just not blocking.
      - name: Trivy image scan (release gate)
        if: inputs.publish-mode == 'release'
        uses: aquasecurity/trivy-action@57a97c7e7821a5776cebc9bb87c984fa69cba8f1 # 0.35.0
        env:
          TRIVY_USERNAME: ${{ github.actor }}
          TRIVY_PASSWORD: ${{ secrets.github-token }}
        with:
          version: 'v0.69.3'
          image-ref: ${{ needs.build-and-push.outputs.image-ref }}
          format: 'table'
          severity: 'CRITICAL,HIGH'
          ignore-unfixed: true
          exit-code: '1'
```

- [ ] **Step 5: Update stale comments**

- In `scan-vulnerabilities` (lines 190-193), replace the comment `Dependency SCA is owned by Dependency Track…` with:

```yaml
      # Dependency SCA lives in the GitHub dependency graph (the image SBOM is
      # submitted in submit-sbom-dependency-graph). This Trivy image scan exists
      # only to source the OpenVEX document below.
```

- In `sign-image` (lines 515-517), replace the comment `security-gate gates signing: a failed Dependency Track gate…` with:

```yaml
    # security-gate gates signing: a failed Trivy release gate (release builds
    # only) leaves the image unsigned, so the downstream cosign-verify deploy
    # policy rejects it. For pre-release/preview builds the gate is skipped, so
    # signing proceeds.
```

- [ ] **Step 6: Run the "after" assertions**

Run:
```bash
npx -y js-yaml .github/workflows/reusable-build-sign-sbom.yml > /dev/null && echo "YAML_OK"
grep -ci "dependencytrack\|dependency track" .github/workflows/reusable-build-sign-sbom.yml || echo "DT_GONE"
grep -n "submit-sbom-dependency-graph\|Trivy release gate\|needs: \[build-and-push, scan-vulnerabilities, security-gate, generate-sboms\]" .github/workflows/reusable-build-sign-sbom.yml | wc -l
```
Expected: `YAML_OK`, `DT_GONE`, then `4` (the four matches are: the `submit-sbom-dependency-graph:` job id, the `.github/scripts/submit-sbom-dependency-graph.sh` invocation, the `Trivy release gate for ...` job name, and the unchanged `sign-image` needs line).

- [ ] **Step 7: Commit**

```bash
git add .github/workflows/reusable-build-sign-sbom.yml
git commit -m "feat(ci): submit image SBOMs to dependency graph, gate releases with trivy exit-code"
```

---

### Task 4: Retire DT secret passthroughs in the lane workflows

**Files:**
- Modify: `.github/workflows/release-candidate.yml:80` and `:180`
- Modify: `.github/workflows/prerelease.yml:77`
- Modify: `.github/workflows/canary-preview.yml:77`
- Modify: `.github/workflows/pr-validation.yml:98`

**Interfaces:**
- Consumes: Task 3's reusable (no `dependencytrack-api-key` secret declared anymore — callers must stop passing it or the workflow call is invalid).
- Produces: four deleted passthrough lines; one updated comment.

- [ ] **Step 1: Run the "before" assertion**

Run: `grep -rn "dependencytrack-api-key\|DEPENDENCYTRACK_API_KEY\|SECOBSERVE" .github/workflows/release-candidate.yml .github/workflows/prerelease.yml .github/workflows/canary-preview.yml .github/workflows/pr-validation.yml | wc -l`
Expected: `5`

- [ ] **Step 2: Delete the passthrough lines**

In each of `release-candidate.yml` (lines 80 and 180), `prerelease.yml` (line 77), `canary-preview.yml` (line 77), delete exactly this line:

```yaml
      dependencytrack-api-key: ${{ secrets.DEPENDENCYTRACK_API_KEY }}
```

In `pr-validation.yml` line 98, replace:

```yaml
    # Pass SECOBSERVE_API_TOKEN (and any other repo/org secrets) to the reusable workflow.
```

with:

```yaml
    # No external security secrets exist anymore; inherit passes GITHUB_TOKEN-scoped secrets.
```

- [ ] **Step 3: Run the "after" assertions**

Run:
```bash
for f in release-candidate prerelease canary-preview pr-validation; do npx -y js-yaml ".github/workflows/$f.yml" > /dev/null || echo "YAML_FAIL $f"; done; echo "YAML_ALL_OK"
grep -rn "dependencytrack-api-key\|DEPENDENCYTRACK_API_KEY\|SECOBSERVE" .github/workflows/ || echo "ALL_GONE"
```
Expected: `YAML_ALL_OK`, `ALL_GONE`.

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/release-candidate.yml .github/workflows/prerelease.yml .github/workflows/canary-preview.yml .github/workflows/pr-validation.yml
git commit -m "chore(ci): drop dependency-track secret passthroughs from release lanes"
```

---

### Task 5: Delete dt-security-gate.sh and update discover-script comments

**Files:**
- Delete: `.github/scripts/dt-security-gate.sh`
- Modify: `.github/scripts/discover-services.sh:13-15`
- Modify: `.github/scripts/discover-frontend.sh:19-21`

**Interfaces:**
- Consumes: Tasks 2-4 (no remaining references to DT/SO in workflows).
- Produces: zero DT/SO references under `.github/`.

- [ ] **Step 1: Run the "before" assertion**

Run: `grep -rli "secobserve\|dependency.track\|dependencytrack" .github/ | wc -l`
Expected: > 0

- [ ] **Step 2: Delete the script and update comments**

```bash
git rm .github/scripts/dt-security-gate.sh
```

In `discover-services.sh`, replace lines 13-15:

```bash
#   product      - SecObserve product name AND nx release group name. Equal to the
#                  service, except gateway sub-services are prefixed (gateway-public)
#                  because "public"/"internal" are ambiguous on their own.
```

with:

```bash
#   product      - code-scanning category AND nx release group name. Equal to the
#                  service, except gateway sub-services are prefixed (gateway-public)
#                  because "public"/"internal" are ambiguous on their own.
```

In `discover-frontend.sh`, replace the `product` and `dtProject` comment lines (currently lines 19-21):

```bash
#   product    - SecObserve product name (== app; frontend apps are unambiguous, no prefix)
#   tag        - business/release-group tag (always `web`)
#   dtProject  - Dependency Track project name (web/<app>)
```

with:

```bash
#   product    - code-scanning category (== app; frontend apps are unambiguous, no prefix)
#   tag        - business/release-group tag (always `web`)
#   dtProject  - dependency-graph SBOM correlator (web/<app>) once per-app SBOMs ship
```

- [ ] **Step 3: Run the "after" assertion**

Run: `grep -rli "secobserve\|dependency.track\|dependencytrack" .github/ || echo "CLEAN"`
Expected: `CLEAN`

- [ ] **Step 4: Commit**

```bash
git add -A .github/scripts/
git commit -m "chore(ci): remove dt-security-gate script, retarget discover-script comments to GHAS"
```

---

### Task 6: dependabot.yml

**Files:**
- Create: `.github/dependabot.yml`

**Interfaces:**
- Consumes: repo manifests (`Directory.Packages.props` + csproj via the `nuget` ecosystem; root `bun.lock` via `bun`; `.devcontainer/Dockerfile` via `docker`; workflows via `github-actions`).
- Produces: weekly grouped Dependabot PRs. Post-merge validation: GitHub → Insights → Dependency graph → Dependabot shows no config errors (if the runner rejects `package-ecosystem: bun`, change that one key to `npm` in a follow-up commit — everything else stays).

- [ ] **Step 1: Write the config**

Create `.github/dependabot.yml`:

```yaml
version: 2
updates:
  # .NET via central package management (Directory.Packages.props at root).
  - package-ecosystem: nuget
    directory: /
    schedule:
      interval: weekly
      day: monday
    groups:
      dotnet-minor-patch:
        patterns:
          - "*"
        update-types:
          - minor
          - patch

  # Bun workspace (root bun.lock). If GitHub rejects the `bun` ecosystem key,
  # change it to `npm` — the lockfile is what matters.
  - package-ecosystem: bun
    directory: /
    schedule:
      interval: weekly
      day: monday
    groups:
      frontend-minor-patch:
        patterns:
          - "*"
        update-types:
          - minor
          - patch

  # Devcontainer base images. deploy/Containerfile.template is not a
  # Dependabot-detectable filename; its pins ride the release trivy gate instead.
  - package-ecosystem: docker
    directory: /.devcontainer
    schedule:
      interval: weekly
      day: monday

  # Workflow action pins (SHA-pinned with version comments).
  - package-ecosystem: github-actions
    directory: /
    schedule:
      interval: weekly
      day: monday
    groups:
      actions-minor-patch:
        patterns:
          - "*"
        update-types:
          - minor
          - patch
```

- [ ] **Step 2: Validate**

Run: `npx -y js-yaml .github/dependabot.yml > /dev/null && echo "YAML_OK"`
Expected: `YAML_OK`

- [ ] **Step 3: Commit**

```bash
git add .github/dependabot.yml
git commit -m "feat(ci): enable dependabot alerts and grouped auto-fix PRs"
```

---

### Task 7: Update the security-review skill's sink references

**Files:**
- Modify: `.opencode/skills/security-review/SKILL.md` (findings table + "Honest limits" section)

**Interfaces:**
- Consumes: the merged migration (Tasks 1-6).
- Produces: agent-facing triage doc that names GitHub as the console instead of SecObserve/DT.

- [ ] **Step 1: Replace the findings table**

Replace the table under `## What each finding means`:

```markdown
| Scanner | Finding | Severity in CI |
|---|---|---|
| **Gitleaks** | a secret in the code or git history | **HARD BLOCK** — fails the merge gate |
| **Semgrep** | SAST issue (`p/csharp`, `p/secrets`, `p/r2c-security-audit`) | code scanning alert (Security tab); PR merge gate via branch protection |
| **Trivy** | HIGH/CRITICAL dependency vuln | dependency graph alert (Dependabot); release gate blocks image signing |
```

- [ ] **Step 2: Replace the "Honest limits" section**

Replace the section body with:

```markdown
## Honest limits

This reproduces the **scanners**, not the **gates**. The merge decisions live in
GitHub: the branch-protection code-scanning rule and dependency-review on PRs,
and the Trivy release gate + dependency graph on the release path. A clean local
run makes CI very likely to pass — it does not guarantee it. Say that rather
than promising a green pipeline.
```

- [ ] **Step 3: Verify no stale references**

Run: `grep -in "secobserve\|dependency.track" .opencode/skills/security-review/SKILL.md || echo "CLEAN"`
Expected: `CLEAN`

- [ ] **Step 4: Commit**

```bash
git add .opencode/skills/security-review/SKILL.md
git commit -m "docs(security-review): point triage guidance at GHAS consoles"
```

---

## Post-merge manual steps (not plan tasks)

Execute spec §7 in order: confirm GHAS features on the (public) repo; enable Dependabot alerts + security updates; enable secret scanning + push protection; add the branch-protection code-scanning rule on `main`; run the spec §8 verification sequence; only then delete `SECOBSERVE_API_TOKEN`, `SECOBSERVE_API_BASE_URL`, `DEPENDENCYTRACK_API_KEY`, `DEPENDENCYTRACK_HOSTNAME` from repo settings.
