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

The single Orca intake dispatcher must add `agent:claimed` while retaining
`agent:ready`, then re-read the issue after the lifecycle workflow normalizes
the pair. Start this flow only when `agent:claimed` is the sole lifecycle
label. Otherwise stop without creating a Run, container, branch, or sub-issue.
Never remove the current lifecycle label before adding its target because that
erases the transition evidence used by the workflow.

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
  --kind plan-defect --mode planned --depends-on 121 --json
```

Execution modes are durable local routing policy:

- `planned` (default): Prometheus plans and reviews; `/start-work` hands the
  plan to Atlas for execution.
- `quick`: the same Prometheus -> Atlas ownership path with an intentionally
  compact plan and inexpensive `quick` delegation.
- `autonomous`: Hephaestus owns a deep end-to-end implementation.
- `spike`: Hephaestus performs a bounded investigation; production commits are
  required only when the task explicitly requests them.

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
separate Orca workspace/environment. Read `terminalCommand`, `taskSpec`,
`primaryAgent`, and `tmuxSession` from `dispatch-info`; never reconstruct them.
Create a terminal in the active workspace with the emitted command:

```bash
orca-ide terminal create --worktree active --title issue-121-tax \
  --command "<terminalCommand from dispatch-info>" --json
orca-ide terminal wait --terminal <handle> --for tui-idle --timeout-ms 60000 --json
orca-ide orchestration dispatch --task <task_id> --to <handle> --inject --json
tools/orca-feature set-status --issue 121 --status dispatched
```

The launcher creates or attaches one foreground tmux session per sub-issue,
allocates a unique OpenCode server port, fixes the session working directory to
the assigned Git worktree, and starts full OMO. Start all independent ready
workers before waiting.

## 4. Supervise completion

Wait through Orca orchestration for `worker_done`, `question`, or `escalation`.
Process and acknowledge each Delivery according to the live orchestration
guide. A worker must:

1. Work only in its assigned path and branch.
2. Run relevant validation.
3. Create signed conventional commits.
4. Send `worker_done` exactly once with outcome and modified files.

For `planned` and `quick` work, Prometheus writes and reviews the constrained
plan before `/start-work` transfers execution to Atlas in the same OpenCode
session. Atlas remains the primary Orca worker: it reviews delegated output,
validates, signs the conventional commit, and sends `worker_done`. For
`autonomous` and `spike`, Hephaestus is the primary worker. Do not run Atlas and
Hephaestus as concurrent writers in one worktree.

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

`remove` stops the sub-issue's exact tmux session before removing the clean
worktree. To stop a worker without removing its worktree, use
`tools/orca-feature stop --issue 121`.

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
apply `agent:in-review`, and let the human approve and merge it. If the
orchestrator needs a human decision before that point, apply
`agent:needs-input`; return to `agent:claimed` when the same Run resumes, or
`agent:ready` when a fresh intake is required. The issue lifecycle workflow
applies `agent:completed` when the parent issue closes.
