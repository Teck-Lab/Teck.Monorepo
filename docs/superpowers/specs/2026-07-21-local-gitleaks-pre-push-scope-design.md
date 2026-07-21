# Local Gitleaks Pre-Push Scope — Design

**Date:** 2026-07-21  
**Status:** Approved  
**Scope:** Make the local pre-push Gitleaks scan evaluate only refs being introduced by a push. Preserve CI's repository-wide history scan.

## Context

`tools/security-scan.sh` runs Gitleaks `v8.18.4` with `detect --source=<repo>`. For a Git source, that command scans the full history reachable from all local refs. In a linked worktree, the script deliberately mounts both the worktree Git directory and the shared Git common directory so Gitleaks can read Git metadata. As a result, the pre-push hook can find commits belonging only to another local worktree.

The GHAS migration branch is blocked by two such customer-service commits, `0b84af1b...` and `515eda8b...`. Neither is an ancestor of the branch being pushed and neither must be added to the repository allowlist.

CI is intentionally different: GitHub Actions checks out the PR ref with `fetch-depth: 0`, then runs unrestricted `gitleaks detect`. It is the authoritative full-history gate and remains so.

## Decisions

1. The local pre-push Gitleaks check scans commits introduced relative to `origin/main`, not every ref in the shared local repository.
2. The hook uses the Git pre-push ref-update input so it handles pushes of refs other than the currently checked-out `HEAD`.
3. The local scan fails closed if `origin/main` cannot be resolved. Developers must fetch the base ref rather than receive an accidentally broad or narrow scan.
4. CI remains byte-identical: `fetch-depth: 0` plus unrestricted `gitleaks detect` continues to protect the complete repository history.
5. `.gitleaksignore` records exactly the two known customer-test fixture findings in squash commit `8e0d42ff950f0c5acc07b64e8d6589bced5fdc68`. The same fixtures were reviewed as non-credentials previously. No rule- or path-wide suppression is added, and the unrelated local-only commits are not ignored.

## Design

### Pre-push mode

`tools/security-scan.sh` gains a dedicated `--pre-push` mode. `.husky/pre-push` invokes this mode and passes its standard input through unchanged.

Each pre-push input line has this form:

```text
<local-ref> <local-sha> <remote-ref> <remote-sha>
```

The script resolves `origin/main` before scanning. For every non-deletion update (`local-sha` is not forty zeroes), it builds this revision range:

```text
origin/main..<local-sha>
```

It supplies the resulting ranges to Gitleaks as `--log-opts`, without `--all`. Gitleaks then evaluates commits reachable from each pushed local SHA but not from `origin/main`; sibling worktree refs are outside that set. Multiple pushed refs produce multiple revision arguments in one Gitleaks invocation. A deletion-only push introduces no commits and therefore skips the scan.

The existing linked-worktree mounts, pinned Gitleaks image, report format, redaction, and failure behavior remain unchanged. For a non-deletion push, Semgrep and Trivy continue to run once through the existing script flow. Pre-commit behavior (`--staged`) and manual modes retain their current semantics.

### CI and historical findings

`.github/workflows/security-scans.yml` stays unchanged. Its full-history CI scan will still encounter the known non-secret `keycloak-sub-1` fixtures in the merged customer squash commit. Two exact `commit:path:rule:line` fingerprints for `8e0d42f...` are added to `.gitleaksignore`, one per test file. This documents that narrow, reviewed historical exception without disabling the `generic-api-key` rule elsewhere.

The two reported commits that exist only under `worktree-customer-service` are intentionally not added. The range-scoped local pre-push scan eliminates that environmental false positive rather than preserving it as shared configuration.

## Error Handling

- If `origin/main` cannot be resolved, `--pre-push` exits nonzero with an instruction to fetch it.
- A malformed pre-push update line or unresolved local SHA exits nonzero rather than falling back to an unrestricted scan.
- Gitleaks findings in the selected ranges remain a hard failure. No hook bypass, force-push, or weakened CI setting is permitted.
- Deletion-only updates exit cleanly because they introduce no new history to inspect.

## Verification

1. Extend `tools/security-scan.test.sh` to assert the exact Gitleaks `--log-opts` argument for one pushed ref, multiple refs, deletions, and a missing `origin/main`.
2. Confirm the existing linked-worktree and staged-mode tests remain green.
3. Run the pre-push mode with a GHAS-branch ref update and confirm it does not report the unrelated customer-worktree commits.
4. In an isolated clone containing the GHAS branch and its full ancestry, run the full-history Gitleaks scan and confirm the two precise `8e0d42f...` fingerprints clear the reviewed fixture findings.
5. Run the relevant security scan and preserve CI's unchanged full-history Gitleaks job as the server-side backstop.

## Out of Scope

- Changing CI Gitleaks scope or reducing its full-history coverage.
- Rewriting `main` history to remove the reviewed test fixture strings.
- Adding suppressions for the two local-only customer-worktree commits.
- Changing GitHub secret-scanning settings, CodeQL, Semgrep, Trivy, or dependency policy.
