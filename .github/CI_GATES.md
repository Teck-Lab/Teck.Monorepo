# Required pull-request checks

Require these exact check names in the branch rulesets for `main`, `alpha`,
`beta`, and `next`, with GitHub
Actions as the expected source:

| Check | Coverage |
| --- | --- |
| `CI Gate` | Commit format/scopes and squash PR titles; affected builds, tests, lint and type checks; affected Aspire and architecture suites; OpenAPI validation when applicable. |
| `Security Gate` | All CodeQL languages, Semgrep service categories, workflow/configuration scans, dependency review and Gitleaks. |

Publish and run the updated workflows before selecting the new gate checks in
the ruleset. Keep existing required checks until the new gates are reporting.
Neither aggregate replaces required reviews, the native code-scanning rule, or
the native `Require signed commits` rule. Use that rule for GitHub-verified GPG,
SSH or S/MIME signatures; there is no separate signature-checking Actions job.

## Failure handling

Both aggregate jobs use `always()` and inspect the results of their declared
`needs`. A failed, cancelled, missing or unexpectedly skipped prerequisite fails
the gate. A job that intentionally has no affected tests still runs successfully
and reports that no suites were selected.

`CI Gate` permits OpenAPI validation to skip only when its existing preview/main
condition does not apply. The OpenAPI job currently remains a placeholder until
the specs and validator are populated.

`Security Gate` permits only the skips defined by the scan topology:

- Semgrep skips when successful discovery explicitly finds no services.
- Dependency review runs on pull requests.
- The frontend audit runs on schedules and manual Security Scans runs.
- Frontend SBOM submission runs outside pull requests.

CodeQL is called as a reusable workflow by Security Scans, including its weekly
schedule and manual runs. Its entire language matrix must succeed. It has no
separate event triggers that would duplicate the scans.

## Findings and native merge protection

Successful scan execution does not mean there are no security findings.
Semgrep and configuration Trivy upload SARIF without failing on findings;
the frontend audit is informational. Dependency review rejects new dependencies
with high-or-higher vulnerabilities, and Gitleaks fails on detected secrets.

Keep the native code-scanning rule requiring CodeQL and Semgrep OSS at the
`high_or_higher` security threshold. The repository's existing
`main-code-scanning-high-or-higher` ruleset already provides this policy. Other
SARIF tools can be added to that rule if their findings should also block merges.

The workflows run on pull requests targeting and pushes to all four release
branches. Automation must use PRs and must not bypass these required checks.
If a merge queue is enabled, add and validate `merge_group` handling in
both workflows before requiring these checks for it.

The Release workflow listens for completion of CI and Security Scans. It checks
that the latest push run of both workflows succeeded for the exact branch and
source SHA before preparing any release. Image publication also waits for all
candidate image scans, SBOMs, VEX and signatures. A labelled PR preview cannot
trigger Release because its validation event is `pull_request`, not `push`.

## CI scheduling and dependency reuse

PR Checks excludes the Aspire and `*.Architecture.UnitTests` projects owned by
the two parallel sibling jobs. Those jobs intersect Nx's affected graph with
their project selection before calling `run-many`, so each affected suite runs
once. Platform architecture tests with other names remain in PR Checks.

Project selection errors fail the job rather than being interpreted as an empty
selection. Nx continues to schedule builds and restore dependencies; its inferred
.NET test targets already use `--no-build --no-restore`.

The shared setup action uses `global.json` for every CI job and caches downloaded
NuGet and Bun packages by runner OS/architecture and dependency manifests. Each
job still installs from the frozen Bun lockfile and performs the required .NET
restores. The action does not persist build outputs or test results across runs.

## Validation

For changes to these workflows, validate the YAML and GitHub Actions expressions
with actionlint. Exercise each gate with successful dependencies, every required
dependency failed/cancelled/missing/skipped, and the intentional skip cases above.
Inspect representative Nx affected task graphs to confirm the three job selections
are disjoint and retain all affected test projects. Validate runtime and cache
performance with a pull-request CI run; local workflow checks cannot establish
a new end-to-end timing.
