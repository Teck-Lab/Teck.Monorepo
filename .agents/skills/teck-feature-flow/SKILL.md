---
name: teck-feature-flow
description: Coordinate a Teck parent GitHub issue through native Orca Tasks, GitHub sub-issues and blockers, isolated Orca child worktrees, dedicated Codex planner/reviewer/executor/QA workers, local integration, and one final PR. Use for parent issue intake, Task-DAG reconciliation, worker supervision, integration, and final PR preparation.
---

# Teck feature coordinator

Coordinate only. Do not inspect implementation details to replace a planner,
edit product code, perform code review, or perform QA in the parent worktree.

Use four state owners:

- GitHub MCP: durable parent/sub-issues, native blockers, comments, PR, and CI.
- Orca orchestration: Run, Task DAG, Dispatches, questions, and completion.
- Native Orca worktrees: isolated leaf branches, terminals, and UI lineage.
- `tools/orca-feature`: local integration bookkeeping only.

Read [references/workflow.md](references/workflow.md) completely before
starting or resuming. Load Orca's version-matched orchestration guide before
running any orchestration command.

The workflow requires the coordinator to read and apply
[references/state-machine.md](references/state-machine.md) completely. Treat
its convergence and exit audits as gates, not suggestions.

## Non-negotiable boundaries

- Start every durable worker with Orca `worker-start --agent codex`.
- Never create a terminal and inject a reconstructed prompt manually.
- Never use OMX worktrees, `$team`, tmux workers, `$autopilot`, `$ralph`, or an
  OMX goal ledger. OMX is role/skill guidance inside native Codex only.
- Planning, plan review, implementation, code review, and QA are separate
  Dispatches. A coordinator may reconcile their results but never substitutes
  for them.
- After `worker-start` succeeds, supervision remains owned by this coordinator
  Run until the Dispatch is settled and reconciled. Keep the coordinator turn
  alive with Orca's foreground rolling `check --wait` loop until every expected
  Dispatch settles; starting a worker is never a terminal response condition.
- A valid `worker_done` for the active Task and Dispatch automatically completes
  that Orca Task and Dispatch. Never follow it with
  `task-update --status completed`. It does not close a GitHub sub-issue,
  release a GitHub blocker, prove the artifact acceptable, or authorize the
  next wave before coordinator validation and reconciliation.
- Run `check --wait` in the foreground, never as a detached shell job. Treat a
  timeout or empty Delivery as a supervision checkpoint, not permission to
  answer or worker failure. Process and acknowledge one bounded Delivery, then
  atomically acknowledge-and-wait again until every expected Dispatch settles.
- Accept lifecycle messages only for the exact current Task and Dispatch IDs.
  A stale, duplicate, failed, or superseded attempt cannot advance state.
- On start or resume, reconstruct state from GitHub, Orca, the feature ledger,
  and native worktrees before creating or dispatching anything.
- Bind plans, reviews, validation, and QA to immutable artifact digests and Git
  SHAs. Any changed artifact or branch tip invalidates the earlier approval.
- Each executable implementation leaf maps to one GitHub sub-issue, one Orca
  Task, and one native Orca child worktree.
- Ephemeral native Codex subagents may exist only inside an executor Dispatch.
  They inherit that leaf's scope and never own GitHub, Git, Orca, or lifecycle.
- Every actionable review finding becomes a GitHub sub-issue and native blocker.
  Informational observations remain evidence on the reviewed issue.
- Every actionable blocker sub-issue must contain or cite an approved executable
  plan before dispatch. Dispatch ready blocker Tasks before the Tasks they
  block. After an accepted repair is integrated and independently revalidated,
  reconcile and release its GitHub/Orca edges, re-read both graphs, and
  immediately dispatch every newly unblocked eligible Task.
- Mirror GitHub blockers in Orca Task dependencies. Re-read both graphs after
  mutation; disagreement blocks dispatch.
- Preserve dependency direction from the approved plan. If it says `A waits
  for B`, then A is blocked by B and B is the eligible predecessor. An open
  overlapping issue alone never reverses that edge or blocks B; create and
  verify the missing directed edge, then continue B unless live execution or
  another explicit dependency proves a real collision.
- Reviewers and QA never repair findings. Dispatch a finding to an executor and
  re-run the affected review after integration.
- Only the coordinator integrates child commits, publishes the parent branch,
  creates the final PR, and updates parent lifecycle labels.
- Never yield a final response while an unacknowledged lifecycle delivery, an
  active or expected Dispatch, a ready Task, an open actionable sub-issue, or a
  blocker edge remains. Do not say that Orca will re-engage the coordinator and
  then return: remain in the rolling wait loop. Stop only for a precise human
  decision/access blocker or the final open PR after clean QA and required CI.
- Never report the parent complete merely because one child or blocker repair
  completed. Completion requires every parent sub-issue to be closed with
  accepted evidence or explicitly classified as non-actionable, every blocker
  edge reconciled, and every required review/QA rerun clean.
- Never merge the final PR, create tags, run `nx release`, force push, or bypass
  hooks.
