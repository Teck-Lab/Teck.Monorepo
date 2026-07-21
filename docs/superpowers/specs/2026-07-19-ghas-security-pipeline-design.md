# GHAS Security Pipeline — Design

**Date:** 2026-07-19
**Status:** Approved (design), pending implementation plan
**Scope:** Migrate all security scan results and SBOMs from SecObserve + Dependency-Track to GitHub Advanced Security (GHAS). Retire both external services.

## Context

The security pipeline today spans six workflows and three helper scripts:

- **SAST:** Semgrep runs natively (pinned 1.97.0, configs `p/csharp`, `p/secrets`, `p/r2c-security-audit`) per affected service + `src/shared`; SARIF uploaded to SecObserve per product; a per-service SecObserve merge gate blocks PRs.
- **Secrets:** Gitleaks (pinned `ghcr.io/gitleaks/gitleaks:v8.18.4`) scans full git history as a CI-fail gate; not exported anywhere. Local Husky hooks mirror this scan (`tools/security-scan.sh --staged`), and `.gitleaksignore` records reviewed false positives.
- **SCA:** No source-level SCA in CI. Dependency-Track (DT) owns dependency monitoring via per-service container-image CycloneDX SBOMs (projects `{group}/{service}`) plus an aggregate frontend SBOM (project `frontend`). A DT release gate (`dt-security-gate.sh` polling the DT API) runs only in release mode and blocks image signing; staging lanes upload but skip the gate.
- **SLSA:** cosign keyless signing of images, SPDX + CycloneDX SBOMs, and OpenVEX documents; CycloneDX SBOM attested to the image. Orthogonal to SO/DT — unchanged by this migration.
- **Local mirror:** `tools/security-scan.sh` reproduces the three scanners (Gitleaks/Semgrep/Trivy) pinned to CI versions; Husky pre-commit (Biome + staged Gitleaks) and pre-push (full scan) enforce it.

## Decisions (from design Q&A)

1. **Repo is / will be public** → GHAS features are free (code scanning, secret scanning + push protection, dependency graph, Dependabot, dependency review).
2. **Gating model:** PR-time gates (code scanning protection + dependency-review-action) **plus** a release-time Trivy exit-code gate before image signing (same effect as the DT gate, zero new infrastructure).
3. **Secrets:** Keep the Gitleaks CI job byte-identical (local mirror + `.gitleaksignore` preserved) **and** enable GHAS secret scanning + push protection as a second platform layer.
4. **Dependabot:** Alerts **and** auto-fix PRs via a new `dependabot.yml` (nuget, bun/npm, docker, github-actions).
5. **Approach A (chosen over B "manifests-only" and C "parallel-then-cutover"):** every SO/DT flow gets a 1:1 GitHub-native replacement in the same jobs, so coverage never dips; cutover in a single migration PR.

## Design

### 1. SAST → code scanning (Semgrep unchanged, sink replaced)

The `semgrep` matrix job in `security-scans.yml` keeps everything through SARIF generation — same pinned Semgrep 1.97.0, same three configs, same per-service + `src/shared` scan paths, same `ulimit -s 8192` workaround. The local `tools/security-scan.sh` mirror stays valid untouched.

The SecObserve upload step is replaced by `github/codeql-action/upload-sarif` (SHA-pinned per repo convention):

- `sarif_file: semgrep-${{ matrix.product }}.sarif`
- `category: ${{ matrix.product }}` — the per-service split survives as code-scanning categories, mapping SecObserve products 1:1.

Findings appear as PR annotations and in the Security tab. The workflow's `permissions` gains `security-events: write`. The SecObserve merge-gate job is **deleted**; its replacement is a branch-protection code-scanning rule on `main` (repo settings, see §7), which blocks PRs introducing new alerts.

### 2. Dependency stack → dependency graph + Dependabot + PR gate

Three layers replace DT's source-side role:

- **Dependency graph** (auto-available on public repos) parses `Directory.Packages.props`/csproj and `bun.lock` natively. No SBOM needed for source manifests.
- **New `.github/dependabot.yml`**: ecosystems `nuget` (`/`), Bun workspace (`/`; exact ecosystem key — `bun` vs `npm` — verified at plan time), `docker` (each directory containing a Containerfile/Dockerfile, enumerated at plan time), and `github-actions` (`/`). Weekly schedule; minor/patch updates grouped to limit PR noise.
- **New `dependency-review` job** in `security-scans.yml` (PR-only): `actions/dependency-review-action` (SHA-pinned) with `fail-on-severity: high`. This is the PR-time new-dependency gate the current workflow comments already anticipate.

### 3. SBOMs → dependency graph submission

Every CycloneDX SBOM currently going to DT instead goes to GitHub's dependency graph via the dependency submission API:

- `reusable-build-sign-sbom.yml`: the `upload-sbom-dependency-track` job becomes `submit-sbom-dependency-graph`, submitting the per-service image SBOM with a correlator matching today's DT project name (`{group}/{service}`). Each service keeps its own dependency view.
- `security-scans.yml`: the `frontend-sbom` job swaps its DT upload step for the same submission (correlator `frontend`), still off-PR only.

Implementation: a small `gh api` call to the dependency submission REST endpoint (`POST /repos/{owner}/{repo}/dependency-graph/snapshots`) with `GITHUB_TOKEN` (`contents: write`) — no new external action and no new secrets; the `advanced-security` CycloneDX submission action is an acceptable SHA-pinned alternative if the plan prefers it. SPDX generation, cosign signing of SBOMs, OpenVEX generation, and image attestation are **untouched**.

### 4. Release gate → Trivy exit-code

The `security-gate` job in the reusable **keeps its job id** (so `sign-image.needs` wiring is unchanged) but its contents change: on `publish-mode == 'release'`, the job fails when the image Trivy scan finds HIGH/CRITICAL — `--exit-code 1 --severity HIGH,CRITICAL` on the scan that already runs for VEX sourcing (no second scan). Gate fails → `sign-image` skipped → image unsigned → deploy admission rejects. Same semantics as the DT gate. Staging lanes (`prerelease`, `canary-preview`) skip the gate, as today. `.github/scripts/dt-security-gate.sh` is deleted.

### 5. Secrets layer → Gitleaks stays + platform layer added

The `gitleaks` CI job is **byte-identical** — pinned image, full history, exit-code 1, honors `.gitleaksignore`. GHAS secret scanning does not read `.gitleaksignore` and runs as a platform setting, so it cannot replace this job without breaking the local↔CI mirror. In repo settings (checklist, §7): enable **secret scanning + push protection** as a second layer.

### 6. Deletions and doc updates

**Workflow/script deletions:**

- `security-scans.yml`: SecObserve upload step (semgrep job), `security-gate` (SecObserve) job, DT upload step (frontend-sbom job), `SO_API_BASE_URL`/`SO_BRANCH_NAME` env, required-config header comment.
- `reusable-build-sign-sbom.yml`: `upload-sbom-dependency-track` job (replaced per §3), DT `security-gate` contents (replaced per §4), `dependencytrack-api-key` secret declaration.
- `release-candidate.yml` (×2 lanes), `prerelease.yml`, `canary-preview.yml`: `dependencytrack-api-key` passthrough.
- `.github/scripts/dt-security-gate.sh`: deleted.

**Comment/doc updates (name the new sinks):** workflow headers, `.github/scripts/discover-services.sh` (`product` comment: SecObserve → code-scanning category), `.github/scripts/discover-frontend.sh` (`dtProject` comment), `pr-validation.yml` (`secrets: inherit` comment), `.opencode/skills/security-review/SKILL.md` (findings table: Semgrep → code scanning, Trivy → dependency graph + release gate; "Honest limits" paragraph rewrite). Root `AGENTS.md` key-files list remains accurate (`security-scans.yml` still exists).

**Secrets/vars retired (repo settings, after first green release run):** `SECOBSERVE_API_TOKEN`, `SECOBSERVE_API_BASE_URL`, `DEPENDENCYTRACK_API_KEY`, `DEPENDENCYTRACK_HOSTNAME`.

### 7. Repo-settings checklist (manual, documented in the migration PR)

1. Confirm public repo (or GHAS enabled): code scanning, dependency graph available.
2. Enable Dependabot alerts + security updates (Dependabot section).
3. Enable secret scanning + push protection.
4. Branch protection on `main`: require the `Security Scans` workflow checks and the code-scanning protection rule (block on new alerts, severity: high and above).
5. After first green release-lane run: delete the four SO/DT secrets/vars.

### 8. Verification

After the migration PR merges, in order:

1. Test PR: Semgrep alerts appear under Security → Code scanning, categorized per service; dependency-review job comments/fails appropriately.
2. Scratch PR with a deliberately downgraded patch-level dependency: both PR gates (code scanning, dependency-review) block.
3. Next RC-lane run: per-service SBOMs visible under Insights → Dependency graph; Trivy release gate runs before `sign-image` (job id unchanged).
4. Throwaway branch push of a fake secret: push protection blocks it.
5. Only then: delete the four SO/DT secrets/vars (§6).

**Error handling:** SARIF/SBOM upload failures fail their job (visible, retryable on re-run). Dependency-review and Trivy failures are the intended gates. Empty Semgrep SARIF (no findings) uploads cleanly. Rollback at any point = revert the migration PR; the secrets remain until the final step.

## Honest trade-offs

- Code scanning's merge gate acts on **new alerts per PR** via branch protection, not SecObserve's per-product thresholds — coarser, but enforceable and simpler.
- Dependency graph shows per-service views via submission correlators, not DT's per-project policy engine and continuous-analysis console.
- Image-level dependency alerting between releases is reduced: release-time gate + Dependabot on manifests remain, but DT's continuous re-analysis of published image SBOMs has no GHAS equivalent.

## Out of scope

- Secret scanning alert migration (dismissing GHAS secret-scanning alerts that duplicate `.gitleaksignore` entries) — handled ad hoc after enablement.
- Per-app frontend SBOMs (`discover-frontend.sh` remains prepared-but-unconsumed, as today).
- CodeQL analysis (Semgrep remains the SAST engine; no CodeQL workflow added).
- Changes to cosign/SLSA provenance, deploy admission, or the `ci.yml` build/test path.
