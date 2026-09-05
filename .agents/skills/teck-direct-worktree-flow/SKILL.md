---
name: teck-direct-worktree-flow
description: Complete and publish work performed directly by a native coding agent in an ordinary Orca worktree without an orchestration Run. Use for a manually assigned branch, issue, Dependabot PR, CI repair, or one-off change that must be validated, committed, pushed through the authenticated GitHub CLI, or attached to an existing PR. Do not use for sub-issues dispatched through the orchestrated teck-feature-flow.
---

# Teck direct worktree flow

Own the direct worktree from implementation through publication. No Orca
coordinator or `worker_done` lifecycle exists unless the session explicitly
received an orchestration Dispatch.

## Implement and checkpoint

1. Confirm the current branch and preserve unrelated existing changes.
2. Implement the user's request and run proportional repository validation.
3. Review the final diff and separate it into meaningful conventional commits.
4. Create GPG-signed conventional commits. Signing failure is a blocker; never
   disable or bypass signing:

```bash
git commit -S -m "fix(scope): concise description"
git verify-commit HEAD
```

Do not commit unrelated files. Never force-push, merge a PR, create tags, or
run `nx release`.

## Publish

Treat “fix this branch,” “fix/update this PR,” and equivalent explicit branch
or PR repair requests as authorization to commit and publish the completed fix.
Also publish when the user asks to commit and push, publish, or finalize.
Otherwise keep changes local. Require a clean worktree, fetch the remote, and
publish without force:

```bash
git fetch origin
git push --set-upstream origin HEAD
```

If publication fails, fetch and inspect the remote branch, reconcile
deliberately, rerun validation when the tree changes, and retry. Never force.

## Report

Report files changed, validation evidence, local commit boundaries, published
verified SHAs, and remaining risks. State clearly when work is only local.
