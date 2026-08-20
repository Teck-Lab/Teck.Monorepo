# Feature workflow

## Contents

- Establish and plan the parent feature
- Reconcile sub-issues and the Task DAG
- Create native child worktrees with visible OMO workers
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
sub-issue:

```bash
orca orchestration run-create --objective "Implement GitHub parent #120" --json
```

## 2. Plan and reconcile executable children

When the parent is not already decomposed, create one planning Task and run it
in a fresh OMO planner terminal in the parent feature worktree. The planner may
read GitHub and the repository, but it must not mutate issues, Tasks, branches,
worktrees, or code. After `worker_done`, the coordinator reviews its plan.

Launch the dedicated Prometheus planner, wait for readiness, then create the
tracked Dispatch and deliver Orca's exact returned lifecycle preamble:

```bash
orca orchestration task-create --spec "Plan-only decomposition for GitHub parent #120. Do not edit or invoke /start-work; return leaves, dependencies, acceptance criteria, validation, overlap risks, and plan defects through worker_done." --json
orca terminal create --worktree active --title feature-120-plan \
  --command "teck-omo-planner" --json
orca terminal wait --terminal <planner-handle> --for tui-idle \
  --timeout-ms 60000 --json
tools/orca-dispatch-terminal --task <planning-task-id> \
  --terminal <planner-handle>
orca orchestration dispatch-show --task <planning-task-id> --json
```

The Task spec must explicitly require a plan-only `worker_done` and forbid
`/start-work`; the coordinator, not Prometheus, materializes the approved leaves.
The visible primary must be `Prometheus - Plan Builder`. Seeing Sisyphus means
agent selection fell back and the planning worker is invalid; stop and relaunch
it rather than accepting output from the wrong primary.

The dispatch helper must report `delivered: true`. It sends Orca's returned
capability-bearing preamble verbatim and never reconstructs lifecycle commands.
Require observable task processing after delivery.
Text that merely says `worker_done` in the terminal is not completion and must
never be reconciled as a Delivery.

Use GitHub MCP `issue_write` to create missing executable leaf issues and
`sub_issue_write` to attach them to the parent. Re-read sub-issues before every
mutation and never duplicate an existing child. Create one Orca Task per child;
translate GitHub blockers to Task IDs in `--deps`. If GitHub cannot mutate a
dependency, comment `Blocked by #121` on #122 while keeping Orca's DAG as the
execution authority.

Create implementation Tasks only after the approved GitHub children exist:

```bash
orca orchestration task-create --spec "Implement GitHub sub-issue #121 ..." --json
orca orchestration task-create --spec "Implement GitHub sub-issue #122 ..." \
  --deps '["<task-121-id>"]' --json
```

Use durable execution modes only for OMO routing:

- `planned` (default): Atlas creates or reviews a constrained internal plan,
  then executes it.
- `quick`: Atlas executes a compact bounded plan.

## 3. Create native child worktrees with visible OMO workers

Start only Tasks returned by `orca orchestration task-list --ready`. Create the
native child first, explicitly parented to the active feature worktree:

```bash
orca orchestration task-list --ready --json
orca worktree create --name issue-121-tax-system \
  --parent-worktree active --setup run --json
```

Read the returned full worktree ID, path, and branch. Register that existing
checkout for integration bookkeeping; the helper must never create it:

```bash
tools/orca-feature register --issue 121 --title "Tax system" \
  --path <worktree-path> --branch <branch> \
  --worktree-id <full-worktree-id> --mode planned --json
tools/orca-feature dispatch-info --issue 121
```

Start the dedicated OMO/OpenCode worker through Orca's native supervised
composition. The seeded OpenCode configuration selects Atlas for implementation:

```bash
orca orchestration worker-start --task <task-id> \
  --worktree id:<full-worktree-id> --agent opencode --json
orca orchestration dispatch-show --task <task-id> --json
tools/orca-feature set-status --issue 121 --status dispatched
```

This native composition is intentional: Orca owns the implementation-agent
launch, readiness, Dispatch creation, and lifecycle injection, while
OpenCode's seeded `default_agent` selects Atlas.
The child remains in the parent feature's disposable devcontainer; do not
select another recipe.

Start all independent ready workers before waiting. Never place two writers in
one worktree or parallelize workers that overlap files, generated artifacts,
ports, databases, or mutable services.

Materialize children lazily: do not create #122's child worktree until its Task
is ready. Because `integrate` resets the parent checkout to the exact promoted
App commit, a later child created from `active` starts from the reconciled
parent feature head.

## 4. Supervise completion

Wait through Orca orchestration for `worker_done`, `question`, or `escalation`.
Process and acknowledge each Delivery according to the live orchestration
guide. A worker must:

1. Work only in its assigned path and branch.
2. Run relevant validation.
3. Create conventional local checkpoint commits; never push the worker branch.
4. Send `worker_done` exactly once with outcome and modified files.

A valid `worker_done` automatically completes its Dispatch and Task, which is
what releases dependent Tasks in Orca's DAG. Process the entire Delivery before
acknowledging it; do not follow a valid completion with a redundant manual
`task-update --status completed`.

For implementation Tasks, Atlas is the primary Orca worker: it may create or
review a bounded internal plan, delegates only within the assigned worktree,
validates, creates the unsigned conventional checkpoint commit, and sends
`worker_done`.

After successful `worker_done`:

```bash
tools/orca-feature set-status --issue 121 --status completed
git -C <worktree-path> status --short
git -C <worktree-path> log --oneline \
  "feature/120-billing-overhaul..subfeature/120/121-tax-system"
```

Do not integrate a dirty worktree, missing commit, failed validation, or
review-only finding that has not been assigned to an editor.

## 5. Integrate in dependency order

From the clean parent checkout, let only the coordinator run:

```bash
tools/orca-feature integrate --issue 121
```

The helper requires clean parent/worker trees, satisfied dependencies, and
worker commits. It squash-applies the worker result and creates one
conventional local commit with the exact resulting Git tree. If Git reports a
conflict, resolve or abort it in the parent checkout;
the helper does not mark the issue integrated after a failed integration.

After validation, use GitHub MCP to comment with the integrated commit and
close the sub-issue. Once `worker_done` has been accepted, release the settled
Dispatch terminal, remove the native child through Orca, and only then record
that confirmed removal locally:

```bash
orca orchestration worker-release --dispatch <dispatch-id> --json
orca worktree rm --worktree id:<full-worktree-id> --json
tools/orca-feature remove --issue 121
```

Never substitute terminal closure or raw `git worktree remove` for Orca-owned
cleanup. `remove` only records a
removal already completed by Orca and refuses while the checkout still exists.

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

Every integrated sub-feature has already advanced the remote parent branch as
one verified App commit. Do not run a separate ordinary Git push; `pr-info`
must report the feature ready before opening the PR.
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
