# GHAS security pipeline migration design

## Goal

Replace SecObserve and Dependency-Track with GitHub-native security capabilities for this public repository while retaining existing scanners, SBOM production, signing, and attestations.

## Branch strategy

Start from current `main` in an isolated worktree. Port the three useful commits from the stale `feat/ghas-security-pipeline` branch rather than rebasing its 66-commit drift, then complete the missing migration work.

## Security architecture

- Add advanced CodeQL analysis for C# and JavaScript/TypeScript on pull requests to `main`, pushes to `main`, and a weekly schedule.
- Retain Semgrep. Upload its existing SARIF output to GitHub Code Scanning using stable, unique categories.
- Add `actions/dependency-review-action` for pull requests, failing on newly introduced High or Critical dependency vulnerabilities.
- Add Dependabot configuration for the repository's supported NuGet, Bun/npm, Docker, and GitHub Actions manifests; enable dependency graph, Dependabot alerts, and Dependabot security updates in repository settings.
- Create a repository-level `main` ruleset that requires CodeQL and Semgrep code-scanning results and blocks High-or-higher security alerts. This native merge protection applies to newly introduced pull-request alerts, not pre-existing default-branch alerts.
- Retain Gitleaks, Trivy, the frontend dependency audit, SBOM generation, SBOM signing, image signing, VEX, and image attestations.

## Workflow changes

- Replace SecObserve SARIF upload and per-service merge-gate actions with GitHub SARIF upload and native code-scanning merge protection.
- Remove Dependency-Track SBOM uploads, API inputs/secrets, staging passthroughs, and `dt-security-gate.sh` polling from reusable release, RC, prerelease, and canary workflows.
- Retain SBOM generation and signing as release artifacts. Dependency-Track is no longer a release gate; the required GitHub PR protections prevent new High-or-higher dependency and SAST findings from reaching `main`.
- Keep direct Trivy scanning and report/VEX generation without external-service integration.
- Update discovery scripts, local scanner messaging, and security-review documentation so they describe GitHub Code Scanning and native GitHub dependency security accurately.

## Repository settings

The implementation creates the `main` code-scanning ruleset. It also verifies and documents the GitHub settings that cannot be represented in workflow YAML: dependency graph, Dependabot alerts/security updates, CodeQL analysis, secret scanning, and push protection.

## Validation

- Validate workflow YAML and the existing security-scan script tests.
- Verify CodeQL and Semgrep upload code-scanning results on a pull request.
- Verify the dependency-review check fails a newly introduced High-or-Critical dependency vulnerability.
- Verify the `main` ruleset exposes and enforces the CodeQL and Semgrep High-or-higher merge policy.
- Verify no workflow, script, or documentation reference requires SecObserve or Dependency-Track credentials after migration.

## Non-goals

- Do not delete existing scanner coverage.
- Do not replace Trivy container scanning with GHAS.
- Do not retroactively block merges on pre-existing code-scanning alerts outside a pull request diff.
