---
name: teck-direct-worktree-flow
description: Complete and publish work performed directly in an ordinary Orca worktree without an orchestration Run. Use when Sisyphus is handling a manually assigned branch, issue, Dependabot PR, CI repair, or one-off change and must validate, create local commits, publish through the GitHub App, or update an existing PR. Do not use for sub-issues dispatched through the orchestrated teck-feature-flow.
---

# Teck direct worktree flow

Own the direct worktree from implementation through publication. No Orca
coordinator or `worker_done` lifecycle exists unless the session explicitly
received an orchestration Dispatch.

## Implement and checkpoint

1. Confirm the current branch and preserve unrelated existing changes.
2. Implement the user's request and run proportional repository validation.
3. Review the final diff and separate it into meaningful conventional commits.
4. Create unsigned local commits. Disabled local signing is intentional and is
   never a blocker:

```bash
git -c commit.gpgsign=false commit -m "fix(scope): concise description"
```

Do not commit unrelated files. Do not use ordinary `git push`, force-push,
merge a PR, create tags, or run `nx release`.

## Publish

Treat “fix this branch,” “fix/update this PR,” and equivalent explicit branch
or PR repair requests as authorization to commit and publish the completed fix.
Also publish when the user asks to commit and push, publish, or finalize.
Otherwise keep changes local. Require a clean worktree, then preview and
publish:

```bash
tools/github-app-publish --dry-run
tools/github-app-publish
```

The publisher preserves each local commit boundary and message while replacing
the unsigned local objects with GitHub App-authored verified commits. It
refuses dirty worktrees, non-conventional commits, detached HEAD, remote
divergence, and unverified results. After success it resets the local branch to
the verified remote head; the existing PR updates automatically.

If publication fails, do not use `git push` as a fallback. Fetch and inspect the
remote branch, reconcile deliberately, rerun validation when the tree changes,
and retry the publisher.

## Report

Report files changed, validation evidence, local commit boundaries, published
verified SHAs, and remaining risks. State clearly when work is only local.
