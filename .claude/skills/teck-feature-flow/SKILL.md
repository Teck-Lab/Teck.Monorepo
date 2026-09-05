---
name: teck-feature-flow
description: Coordinate a Teck parent GitHub issue through native Orca Tasks, GitHub sub-issues and blockers, isolated Orca child worktrees, model-routed delivery-architect/reviewer/executor/QA workers, local integration, and one final PR. Use for parent issue intake, Task-DAG reconciliation, worker supervision, integration, and final PR preparation.
---

# Teck feature coordinator

Coordinate only. Do not inspect implementation details to replace a delivery architect,
edit product code, perform code review, or perform QA in the parent worktree.

Use five state owners:

- GitHub MCP: durable parent/sub-issue reads and supported issue, comment, PR,
  and CI mutations.
- GitHub REST or GraphQL dependency API: native `blocked_by` and `blocking`
  relationships; the current GitHub MCP surface cannot mutate them.
- Orca orchestration: Run, Task DAG, Dispatches, questions, and completion.
- Native Orca worktrees: isolated sub-issue branches, terminals, and UI lineage.
- `tools/orca-feature`: local integration bookkeeping only.

Read [references/workflow.md](references/workflow.md) completely before
starting or resuming. Load Orca's version-matched orchestration guide before
running any orchestration command.

The workflow requires the coordinator to read and apply
[references/state-machine.md](references/state-machine.md) completely. Treat
its convergence and exit audits as gates, not suggestions.

Read [references/delegation-contracts.md](references/delegation-contracts.md),
[references/test-driven-development.md](references/test-driven-development.md),
and [references/agent-visibility.md](references/agent-visibility.md)
when creating or accepting a Task, and
[references/review-convergence.md](references/review-convergence.md) before
planning, review, QA, or repair. Read
[references/execution-discoveries.md](references/execution-discoveries.md)
before classifying work discovered during implementation. Read
[references/handoff-contract.md](references/handoff-contract.md) only when
transferring unfinished ownership to a fresh session.

## Non-negotiable boundaries

- Prefer a verified Claude Code `claude-opus-5`/high parent coordinator; accept
  Codex `gpt-5.6-sol`/high only as the recorded availability fallback. Never
  keep both live for one parent.
- Start every durable worker with Orca `worker-start` using the approved agent,
  model, effort, and effective-launch receipt. Durable cross-provider work is
  an Orca Task, never an executor-owned hidden process.
- Never create a terminal and inject a reconstructed prompt manually.
- Never use OMX worktrees, `$team`, tmux workers, `$autopilot`, `$ralph`, or an
  OMX goal ledger. OMX is role/skill guidance inside native Codex only.
- Planning, plan review, implementation, review-unit code review, and
  whole-feature QA are separate Dispatches. Supporting Tasks do not receive
  standalone review. A coordinator may reconcile results but never substitutes
  for an applicable role.
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
- Each executable GitHub sub-issue owns exactly one canonical direct native Orca
  child worktree and branch under the main feature worktree. Ordinary member
  Tasks run sequentially there. Manifest-approved, resource-disjoint members may
  run concurrently in worktrees one additional level beneath it when the
  expected speedup exceeds integration cost. The coordinator integrates those
  commits into the canonical sub-issue worktree before one combined review.
  Independent resource-safe sub-issues may also run concurrently. Read-only
  supporting Tasks may use an explicitly selected existing worktree.
- Every delegated agent, including research, testing, debugging, or independent
  checking helpers, must be a visible Orca Task/Dispatch with its own verified
  terminal and correct Task/worktree lineage. Provider-native Claude/Codex
  subagents are prohibited.
- The parent coordinator owns every durable member Dispatch. Never share one
  editable worktree across sub-issues or create descendants below the optional
  parallel-member layer.
- Only findings actionable under the convergence contract become blockers.
  Reuse one GitHub sub-issue and Orca Task per stable finding key. Scope
  expansions and observations are non-blocking follow-ups.
- Every coordinator-created or rewritten GitHub issue body must use readable
  Markdown with real line breaks and the ordered sections `## Scope`,
  `## Acceptance criteria`, `## Validation`, and `## Constraints`. Write bodies
  through a file/body-file input rather than escaped JSON or shell text. Read
  the issue back after mutation and reject it if it contains literal `\\n`
  sequences, missing/duplicate/out-of-order headings, or an empty required
  section. Repair malformed durable state before planning or dispatch continues.
- Every actionable blocker sub-issue must contain or cite an approved executable
  plan before dispatch. Dispatch ready blocker Tasks before the Tasks they
  block. After an accepted repair is integrated and independently revalidated,
  reconcile and release its GitHub/Orca edges, re-read both graphs, and
  immediately dispatch every newly unblocked eligible Task.
- The assigned parent coordinator owns the complete outcome through the final
  PR, including cross-parent blockers required by its approved plan. Only a
  provably live coordinator and current Dispatch count as another owner;
  historical Runs, comments, attempts, worktrees, branches, artifacts, and
  settled or abandoned Dispatches do not. If a required blocker has no live
  owner, claim it in the current coordination, reconcile and reuse any partial
  work, finish or repair it, independently review and integrate it, then release
  its edges and immediately dispatch the work it unblocks. Never classify an
  actionable code issue as an external-state stopping condition merely because
  it belongs to another parent or was partly attempted elsewhere.
- Treat the executable frontier as every open, dependency-unblocked issue with
  no verified live Orca claim. Before claiming one, fetch its full title, body,
  labels, comments, relationships, and all candidate Run/Task/Dispatch records.
  A claim is valid only when durable issue evidence names the same current live
  Run, Task, Dispatch, and coordinator verified in Orca. Re-read immediately
  after claiming and immediately before acceptance/integration; reconcile any
  competing live claim instead of duplicating work. In human-facing output,
  refer to issues by linked title, not bare-number chains.
- Persist every blocker as a native GitHub relationship, never only in issue
  prose, comments, or labels. For `A waits for B`, use the GitHub dependency API
  to add B's numeric issue database ID to A's `blocked_by` collection, read the
  directed edge back through GitHub GraphQL or MCP, and mirror it as an Orca Task
  dependency. Remove and re-read both edges only after B is accepted and
  integrated. Any disagreement blocks dispatch.
- Preserve dependency direction from the approved plan. An open overlapping
  issue alone never reverses that edge or blocks B; create and verify the
  missing directed edge, then continue B unless live execution or another
  explicit dependency proves a real collision.
- Reviewers and QA never repair findings. Dispatch a bounded finding to an
  executor and re-run the affected review unit or whole-feature QA. Enforce the
  repair limits and use a native decision gate at the convergence threshold.
- Only the coordinator integrates child commits, publishes the parent branch,
  creates the final PR, and updates parent lifecycle labels.
- Never yield a final response while an unacknowledged lifecycle delivery, an
  active or expected Dispatch, a ready Task, an open actionable sub-issue, or a
  blocker edge remains. Do not say that Orca will re-engage the coordinator and
  then return: remain in the rolling wait loop. Stop only for a precise human
  human-only decision/access blocker or the final open PR after clean QA and
  required CI. Unowned, partial, failed, stale, or cross-parent implementation
  work is executable recovery work, not an external blocker.
- Never report the parent complete merely because one child or blocker repair
  completed. Completion requires every parent sub-issue to be closed with
  accepted evidence or explicitly classified as non-actionable, every blocker
  edge reconciled, and every required review/QA rerun clean.
- Never merge the final PR, create tags, run `nx release`, force push, or bypass
  hooks.
