# Native Orca and Codex feature workflow

Read [state-machine.md](state-machine.md) completely before running commands.
Its identity, recovery, convergence, review-freshness, and exit audits apply to
every section below.

## 1. Intake and initialize

Read the GitHub parent, sub-issues, dependencies, labels, and comments through
GitHub MCP. Add `agent:claimed` while retaining `agent:ready`, then re-read after
the lifecycle workflow runs. Continue only when `agent:claimed` is the sole
lifecycle label.

Initialize or adopt the parent feature branch with `tools/orca-feature init`.
Create or bind exactly one Orca Run for the parent. Do not create a Run per
sub-issue.

## Coordinator supervision loop

Starting a worker begins supervision; it is never feature completion. Native
Orca wakes an idle Codex coordinator by injecting and submitting a short
`You have ... orchestration messages. Run orca orchestration check` pointer.
Use that supported event-driven path for this Codex coordinator:

1. after `worker-start`, record the Run, Task, Dispatch, and terminal identities;
2. do not launch `check --wait` as a Codex background command: an active
   filtered waiter reserves matching mail, so Orca correctly does not inject a
   duplicate pointer for it, while Codex may end the turn before consuming the
   background command's result;
3. when Orca submits a mail pointer, run `orca orchestration check --json`;
4. process every message in the returned FIFO Delivery before acknowledging it;
5. for an accepted worker result, choose terminal reuse with `worker-start
   --terminal` or release it with `worker-release` before acknowledging the
   Delivery;
6. validate the reported artifact, worktree, commits, and evidence for that
   Task type;
7. reconcile the corresponding GitHub sub-issue and blocker edges through MCP;
8. re-read both durable graphs and dispatch every newly eligible Task whose
   dependencies and resources permit execution; and
9. acknowledge and immediately drain already-queued mail with
   `orca orchestration check --ack <delivery-id> --json`; repeat until no
   Delivery is returned, then let Codex idle so the next native pointer can
   re-engage it.

A coordinator Codex turn may end between events while active workers remain;
that is an event-loop checkpoint, not a workflow result. Do not describe the
feature or planning gate as complete. Do not replace native pointers with
terminal scraping, sleeps, a background waiter, a hook, or a launcher.

A valid `worker_done` for the active Task and Dispatch automatically marks that
Orca Task and Dispatch completed. Do not call `task-update --status completed`
after it. The message does not close a GitHub sub-issue, release a GitHub
blocker, accept an artifact, close a worktree, or authorize downstream dispatch
before coordinator validation and graph reconciliation.

Current Orca has a documented upstream quiescence gap when a consumed delivery
makes another Task ready but the coordinator ends instead of dispatching or
waiting again (stablyai/orca#15185). Do not invent a hook or launcher around
it: prevent that state by dispatching newly-ready work before responding and
draining already-queued Deliveries before the coordinator idles.

The coordinator may report the workflow itself complete only when either:

- the parent PR is open after clean final QA and required CI; or
- progress is impossible without one precisely named human decision, missing
  access grant, or external state change, and the coordinator has recorded the
  blocker durably.

The coordinator must not report workflow completion when any of these exist:

- an unacknowledged lifecycle delivery;
- an active or completed-but-unreconciled Dispatch;
- a ready Orca Task;
- an open actionable GitHub sub-issue;
- a GitHub or Orca blocker edge awaiting reconciliation; or
- a review or QA rerun required by a completed repair.

## 2. Dedicated planning and plan review

Create a read-only planning Task whose spec contains:

- `ROLE: feature planner`
- `REQUIRED TECK SKILL: teck-feature-planner`
- `REQUIRED OMX ROLE: planner`
- parent issue and repository identity
- required leaves, dependencies, acceptance criteria, validation, overlap
  risks, and plan-defect output
- explicit prohibition on code, Git, GitHub writes, worktrees, and delegation

Start it natively:

```bash
orca orchestration worker-start --task <planning-task-id> \
  --worktree active --agent codex --model gpt-5.6-sol --effort xhigh --json
```

Wait for authoritative `worker_done`. Then create a separate plan-review Task
using `teck-plan-reviewer`, the OMX `critic` role, Sol/xhigh, and the planner's
artifact. The coordinator does not review the plan itself.

Planner files under ignored runtime directories such as `.omx/` are scratch
artifacts, not durable handoff state. Before settling planning, persist the
approved plan (or a stable link plus digest and full acceptance contract) in
the GitHub parent and Orca Task graph as required by the state-machine guide.

For every actionable plan finding, create a GitHub sub-issue, attach it to the
parent, add a native blocker edge to the affected leaf or parent, and mirror
that dependency in Orca. Dispatch findings to an executor and repeat plan
review until clean.

When a plan-defect executor sends `worker_done`, validate the repaired artifact
and dispatch a fresh independent plan reviewer. Only a clean review permits the
coordinator to record evidence, close that GitHub defect sub-issue, settle its
Orca Task, and release its blocker edges. If review still fails, keep the defect
open and continue the repair/review loop. Never report the defect complete just
because its repair worker exited successfully.

Only after clean plan review may the coordinator create or reconcile executable
GitHub sub-issues and their Orca Tasks. Re-read before and after every mutation;
never duplicate an existing issue or edge.

## 3. Dispatch executable leaves

Start only Tasks reported ready by Orca and unblocked in GitHub. Create the
native Orca child worktree from the current verified parent feature head, then
register that existing checkout with `tools/orca-feature register`.

Before treating overlap as a dispatch blocker, distinguish ordering from active
contention. Apply the approved direction exactly: `A waits for B` blocks A on B,
not B on A. An open but inactive A is not a reason to stop B. Create and verify
the native GitHub blocker edge and matching Orca dependency, record B's exclusive
resource reservation, and dispatch B. Escalate only if A already has live
execution, the directed edge would cycle or contradict another approved plan,
or the required relationship cannot be written and verified.

Each Task spec contains:

- `ROLE: feature executor`
- `REQUIRED TECK SKILL: teck-feature-executor`
- `REQUIRED OMX ROLE: executor`
- exact GitHub sub-issue, worktree, scope, acceptance criteria, and validation
- conventional local checkpoint commit and exactly-one `worker_done` contract
- prohibitions on push, merge, GitHub mutation, worktree creation, and Orca
  lifecycle mutation beyond injected completion and question commands

Start it natively with Terra/high:

```bash
orca orchestration worker-start --task <leaf-task-id> \
  --worktree id:<full-worktree-id> --agent codex \
  --model gpt-5.6-terra --effort high --json
```

An executor may spawn bounded native Codex subagents for independent work.
Mechanical or exploration helpers default to Luna/xhigh; implementation or
debugging helpers use Terra/high. They edit only the leaf worktree and cannot
commit or send lifecycle messages. Never parallelize overlapping files,
generated outputs, databases, ports, indexes, or mutable services.

## 4. Review and finding repair

After executor `worker_done`, require a clean worktree, local commit, and
validation evidence. Create a separate review Task against the same leaf issue
and worktree using `teck-code-reviewer`, OMX `code-reviewer`, and Sol/xhigh.
Record the exact reviewed branch-tip SHA. Any later commit invalidates that
review and requires a fresh independent review.

Every actionable finding becomes a new GitHub sub-issue under the parent and a
native blocker of the affected implementation issue. Create a corresponding
Orca Task dependency. Reuse the affected leaf worktree sequentially for its
finding executor so the unintegrated leaf state remains available; never run
the reviewer and repair executor concurrently. Repeat review until clean.

## 5. Integrate and synchronize

Only the coordinator integrates a reviewed leaf with
`tools/orca-feature integrate`. Run targeted checks, comment on the GitHub leaf
with the integrated SHA and evidence, close it, and re-read GitHub dependencies
before releasing blocked work.

Release the settled Dispatch, remove the native child through Orca, then call
`tools/orca-feature remove`. Never substitute raw `git worktree remove` or
terminal closure for Orca-owned cleanup.

## 6. Final QA and PR

After all leaves are integrated, create a final QA Task in the parent worktree
using `teck-feature-qa`, OMX `qa-tester` plus `verifier`, and Sol/xhigh. QA is
read-only and reviews the entire integrated feature against the parent issue,
approved plan, repository rules, and required validation.
Record the exact parent SHA reviewed by QA. Any later integration, repair,
rebase, or publication change invalidates QA and requires a fresh run.

Every actionable QA finding becomes a GitHub sub-issue blocking the parent and
an Orca Task dependency. Repair it in a new native child worktree through an
executor, integrate it, then run final QA again.

When QA is clean, run proportional feature gates, normally
`nx affected -t build test lint typecheck`. Require `tools/orca-feature pr-info`
to report ready. Publish the parent branch, create one PR with GitHub MCP,
request human review, inspect CI, post evidence, and apply `agent:in-review`
only after required checks are green. Stop with the PR open for a human merge.
