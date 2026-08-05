# Feature workflow

## Contents

- Establish the parent feature
- Register sub-issues and plan defects
- Create Orca Tasks and same-container workers
- Supervise completion
- Integrate in dependency order
- Prepare the final PR

## 1. Establish the parent feature

Read the GitHub parent issue and its sub-issues through the `github` MCP. Keep
the parent open until the final PR merges.

Initialize the local feature state from the parent checkout:

```bash
# Create a new parent branch from the configured/default base.
tools/orca-feature init --issue 120 --slug billing-overhaul \
  --title "Billing overhaul" --create-branch --json

# Or adopt the already-checked-out feature branch.
tools/orca-feature init --issue 120 --slug billing-overhaul \
  --title "Billing overhaul" --branch "$(git branch --show-current)" --json
```

Create or bind one Orca Run for the parent objective. Do not create one Run per
sub-issue.

## 2. Register sub-issues and plan defects

Use GitHub MCP `issue_write` to create missing issues and `sub_issue_write` to
attach them to the parent. Use `issue_read:get_sub_issues` to reconcile before
creating anything; do not duplicate an existing sub-issue.

Register each GitHub sub-issue locally:

```bash
tools/orca-feature add --issue 121 --title "Tax system" --kind feature --json
tools/orca-feature add --issue 122 --title "Rounding policy is undefined" \
  --kind plan-defect --depends-on 121 --json
```

Register dependencies only after the referenced worktree exists. If the active
GitHub MCP version cannot mutate issue dependencies, comment `Blocked by #121`
on #122; Orca and the helper remain the executable dependency authorities.

## 3. Create Orca Tasks and same-container workers

Fetch the local payload:

```bash
tools/orca-feature dispatch-info --issue 121
```

Dispatch only when `ready=true`. If prerequisites are integrated but
`needsSync=true`, fast-forward the still-undispatched branch and read the
payload again:

```bash
tools/orca-feature sync --issue 121
tools/orca-feature dispatch-info --issue 121
```

`sync` refuses active/diverged work; never use it to rewrite a worker branch.

Create an Orca Task using `taskSpec`. Translate `dependsOn` issue numbers to
the corresponding Orca Task IDs and pass them as Task dependencies.

`worker-start --worktree new-*` is wrong for this topology because it creates a
separate Orca workspace/environment. Create a terminal in the active workspace
with a command that changes into the internal worktree:

```bash
orca-ide terminal create --worktree active --title issue-121-tax \
  --command "cd '/workspaces/.orca-worktrees/120/121-tax-system' && codex" --json
orca-ide terminal wait --terminal <handle> --for tui-idle --timeout-ms 60000 --json
orca-ide orchestration dispatch --task <task_id> --to <handle> --inject --json
tools/orca-feature set-status --issue 121 --status dispatched
```

Use `opencode` instead of `codex` when selected. Read the exact worktree path
from `dispatch-info`; never reconstruct it from memory. Start all independent
ready workers before waiting.

## 4. Supervise completion

Wait through Orca orchestration for `worker_done`, `question`, or `escalation`.
Process and acknowledge each Delivery according to the live orchestration
guide. A worker must:

1. Work only in its assigned path and branch.
2. Run relevant validation.
3. Create signed conventional commits.
4. Send `worker_done` exactly once with outcome and modified files.

After successful `worker_done`:

```bash
tools/orca-feature set-status --issue 121 --status completed
git -C <worktree-path> status --short
git -C <worktree-path> log --show-signature \
  "feature/120-billing-overhaul..subfeature/120/121-tax-system"
```

Do not integrate a dirty worktree, unsigned commit, failed validation, or
review-only finding that has not been assigned to an editor.

## 5. Integrate in dependency order

From the clean parent checkout, let only the coordinator run:

```bash
tools/orca-feature integrate --issue 121
```

The helper requires clean parent/worker trees, satisfied dependencies, and
worker commits. It creates a signed `--no-ff` merge commit. If Git reports a
conflict, resolve or abort it in the parent checkout; the helper does not mark
the issue integrated after a failed merge.

After validation, use GitHub MCP to comment with the integrated commit and
close the sub-issue. Then optionally remove the checkout while retaining its
branch:

```bash
tools/orca-feature remove --issue 121
```

## 6. Prepare the final PR

Run the repository's proportional feature validation, normally including:

```bash
nx affected -t build test lint typecheck
```

Check readiness:

```bash
tools/orca-feature status --json
tools/orca-feature pr-info
```

Push the parent branch as the coordinator with a short-lived GitHub App
installation token:

```bash
teck-git-with-github-app write -- git push --set-upstream origin <parent-branch>
```

The wrapper keeps the token out of Git configuration and command arguments.
GitHub MCP does not receive the local working tree and must not create remote
commits for this flow. Use MCP `create_pull_request` with the `head` and `base`
from `pr-info`, reference the parent and sub-issues in the body, then use
`update_pull_request` to request the human reviewer.

Read checks through `actions_get`, `actions_list`, `get_job_logs`, and
`pull_request_read`. Post a concise validation summary. Stop with the PR open;
the human approves and merges it.
