# Native Orca and Codex feature workflow

Read [state-machine.md](state-machine.md) completely before running commands.
Its identity, recovery, convergence, review-freshness, and exit audits apply to
every section below.

Resolve the CLI for the current runtime and load `orca skills get orchestration
--full` immediately before orchestration mutations. Prefer JSON receipts and
the live guide's flags; repository examples never override the installed Orca
version.

## 1. Intake and initialize

The preferred parent coordinator is Claude Code `claude-opus-5`/high. Verify
the effective model from the live session/launch evidence. If Claude Code, the
model, authentication, capacity, or startup is unavailable, persist an Orca
handoff when a Claude session exists; otherwise let the Orca launcher/operator
record the failed launch. Settle that attempt and relaunch the same parent with
Codex `gpt-5.6-sol`/high. Reconcile the existing Run before continuing and prove
that only one parent coordinator is live. Model quality preference alone is
not a fallback trigger.

Read the GitHub parent, sub-issues, dependencies, labels, and comments through
GitHub MCP. Add `agent:claimed` while retaining `agent:ready`, then re-read after
the lifecycle workflow runs. Continue only when `agent:claimed` is the sole
lifecycle label.

Initialize or adopt the parent feature branch with `tools/orca-feature init`.
Create or bind exactly one Orca Run for the parent. Do not create a Run per
sub-issue.

## Coordinator supervision loop

Starting a worker begins supervision; it is never feature completion. Current
Orca's coordinator contract is an explicit foreground rolling wait. Use it for
this Codex coordinator:

1. after `worker-start`, record the Run, Task, Dispatch, and terminal identities;
2. run `orca orchestration check --wait --types
   worker_done,escalation,question --timeout-ms 900000 --json` in the foreground;
3. treat a timeout or `{count:0}` as a checkpoint and immediately continue a
   rolling wait while any expected Dispatch remains active; before re-arming,
   run one ordinary `orca orchestration check --json` to recover mail that a
   typed waiter may have missed, then re-read active Dispatches;
4. process every message in the returned FIFO Delivery before acknowledging it;
5. for an accepted worker result, choose terminal reuse with `worker-start
   --terminal` or release it with `worker-release` before acknowledging the
   Delivery;
6. validate the reported artifact with `tools/teck-agent-contract`, then
   validate the worktree, commits, and evidence for that Task type;
7. reconcile the corresponding GitHub sub-issue and blocker edges through MCP;
8. re-read both durable graphs and dispatch every newly eligible Task whose
   dependencies and resources permit execution; and
9. acknowledge and atomically continue waiting with:

   `orca orchestration check --ack <delivery-id> --wait --types worker_done,escalation,question --timeout-ms 900000 --json`

   Repeat until every expected Dispatch settles.

Do not end the coordinator Codex turn while active workers remain. Never claim
that Orca will re-engage the coordinator and then return a final response. Do
not replace the foreground wait with terminal scraping, sleeps, a
detached/background waiter, a hook, or a launcher.

When Codex's command runner yields a still-running `check --wait` process or
session identifier, continue that exact process with the command runner's wait
mechanism. A yielded command is still active supervision; it is not a completed
tool call and must never be followed by a final response.

A valid `worker_done` for the active Task and Dispatch automatically marks that
Orca Task and Dispatch completed. Do not call `task-update --status completed`
after it. The message does not close a GitHub sub-issue, release a GitHub
blocker, accept an artifact, close a worktree, or authorize downstream dispatch
before coordinator validation and graph reconciliation.

Orca returns one bounded Delivery rather than every future completion. After
processing it, dispatch newly-ready work before waiting again, then continue
the rolling wait until all expected Dispatches settle. This prevents a
completed worker or newly-ready dependent from being stranded by a coordinator
final response.

The repository Codex hooks provide a deterministic backstop for the commonly
missed review-to-repair edge. `PostToolUse` records a rejected/actionable
review delivery, and `Stop` continues the coordinator turn until a successful
repair `worker-start` occurs. Do not bypass or satisfy this guard with prose:
perform the missing graph transition and resume the foreground rolling wait.
Dispatched worker sessions are excluded because `worker_done` is their required
terminal transition.

This explicit loop is required even on Orca versions with idle mail-pointer
delivery. Upstream issue stablyai/orca#11787 documents Run-mailbox push gaps and
their later partial repairs; #10663 reports typed waits missing queued mail;
#9228 tracks durable coordinator wake/resume; and #15190 records the still-open
#15185 condition where ready work can fail to wake an idle coordinator. The
foreground wait plus timeout-time unfiltered check is the documented path with
a bounded recovery for those known gaps; never make pointer delivery the sole
owner of progress.

The coordinator may report the workflow itself complete only when either:

- the parent PR is open after clean final QA and required CI; or
- progress is impossible without one precisely named human decision, missing
  access grant, or external state change, and the coordinator has recorded the
  blocker durably.

The external-state clause is subordinate to blocker-first progression. It
applies only to state the coordinator cannot produce through repository,
GitHub, Orca, worker, review, integration, or validation actions. An open code
issue, cross-parent dependency, previous failed attempt, partial implementation,
missing review, or unintegrated commit is actionable work and never qualifies.

The coordinator must not report workflow completion when any of these exist:

- an unacknowledged lifecycle delivery;
- an active or completed-but-unreconciled Dispatch;
- a ready Orca Task;
- an open actionable GitHub sub-issue;
- a GitHub or Orca blocker edge awaiting reconciliation; or
- a review or QA rerun required by a completed repair.

## 2. Dedicated planning and plan review

Apply `delegation-contracts.md` and `review-convergence.md` throughout this
workflow. The approved plan freezes the acceptance contract. Classify the
work kind and every Task, group implementation into coherent review units, and
use proportional evidence for its validation profile.

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
  --worktree active --agent codex --model gpt-5.6-sol --effort high --json
```

Wait for authoritative `worker_done`. Then create a separate plan-review Task
using `teck-plan-reviewer`, the OMX `critic` role, Sol/high, and the planner's
artifact. The coordinator does not review the plan itself.

Planner files under ignored runtime directories such as `.omx/` are scratch
artifacts, not durable handoff state. Before settling planning, persist the
approved plan (or a stable link plus digest and full acceptance contract) in
the GitHub parent and Orca Task graph as required by the state-machine guide.

Only a `blocking-defect` or `bounded-omission` satisfying the convergence
contract is actionable. Create or reuse its stable-key GitHub sub-issue,
attach it to the parent, add a native blocker edge to the affected leaf or
parent, and mirror that dependency in Orca. Scope expansions and observations
become non-blocking follow-ups. Repeat repair and review only within the cycle
limits.

Before dispatching that executor, ensure the finding sub-issue and Task cite an
approved executable plan with scope, acceptance criteria, dependencies,
validation, and resource ownership. If the finding changes the plan, run a
dedicated planner and independent plan reviewer first. A blocker is the next
work item in the DAG, not a terminal status update.

When a plan-defect executor sends `worker_done`, validate the repaired artifact
and dispatch a fresh independent plan reviewer. Only a clean review permits the
coordinator to record evidence, close that GitHub defect sub-issue, settle its
Orca Task, and release its blocker edges. If review still fails, keep the defect
open and apply the cycle limits. At the limit create a convergence audit and
native decision gate; do not automatically dispatch another repair. Never
report the defect complete just because its repair worker exited successfully.

Immediately after releasing accepted blocker edges, re-read GitHub and Orca,
compute the newly-ready wave, and dispatch every resource-safe eligible Task.
Do not idle or report the parent complete between blocker reconciliation and
dispatch of work that the blocker just released.

Only after clean plan review may the coordinator create or reconcile executable
GitHub sub-issues and their Orca Tasks. Re-read before and after every mutation;
never duplicate an existing issue or edge.

Every created or rewritten issue must follow the canonical readable structure
used by executable leaves:

```markdown
## Scope

...

## Acceptance criteria

- ...

## Validation

- ...

## Constraints

...
```

Write the body through a temporary Markdown file and the GitHub client's
body-file input. Never construct issue Markdown with literal `\\n` escapes.
Immediately read the body back from GitHub and verify real line breaks, exactly
one of each required heading in the order above, and non-empty content beneath
every heading. A malformed or unreadable issue is an unreconciled mutation:
repair and re-read it before creating its Orca Task, dependency, or Dispatch.

## 3. Dispatch executable leaves

### Required blockers outside the current parent

The assigned parent coordinator owns all work necessary to deliver its parent,
not merely issues already attached beneath it. For every required blocker,
resolve current ownership from live Orca state before deciding what to do:

1. A current owner exists only when a live coordinator and current active
   Dispatch can be identified and verified. Old issue comments, Run records,
   Tasks, Dispatch attempts, branches, worktrees, commits, or artifacts do not
   establish current ownership.
2. If a live owner exists, establish an explicit supervised handoff or tracked
   dependency relationship and keep the assigned parent coordination active
   until the blocker is accepted and integrated. Another live owner prevents
   duplicate editing; it does not permit the assigned coordinator to finish.
3. If no live owner exists, claim the existing blocker for the current
   coordination. Reuse the existing GitHub issue; do not create a duplicate.
   Reconcile any recoverable Task, worktree, branch, commits, artifacts, plan,
   reviews, and validation by immutable identity before creating new state.
   First read its complete title, body, comments, relationships, and current
   state. If its body is malformed, repair it to the canonical issue structure
   and verify the GitHub read-back before treating it as executable.
4. Partial work is a recovery input. Validate its scope, cleanliness, commit
   reachability, plan/review freshness, and base. Dispatch only the missing or
   defective remainder; never discard sound work or accept stale evidence.
5. Finish or repair the blocker through the normal planner/executor/independent
   review/integration gates. Then reconcile and release its GitHub and Orca
   dependency edges, re-read both graphs, and immediately dispatch every newly
   unblocked resource-safe Task.

This ownership resolution takes precedence over every generic external-wait or
stop clause. Only a specifically identified human decision, missing access
grant, or authority that cannot be obtained through the supported workflow may
stop the coordinator before the final PR.

### Executable frontier and claims

Use the tracker as the canonical index and each issue as the single source for
its executable contract. Do not duplicate a child issue's details into another
issue; link it by its readable title and keep the authoritative scope,
acceptance criteria, validation, and constraints on that child.

The executable frontier is the set of issues that are open, dependency-
unblocked in both GitHub and Orca, resource-safe, and without a verified live
claim. Determine it from current tracker and Orca state, never from remembered
issue numbers or old comments.

Before claiming a frontier issue:

1. fetch its full title, body, labels, comments, parent/sub-issue relationships,
   blocker relationships, and linked development state;
2. search candidate Orca Runs, Tasks, Dispatches, terminals, and worktrees;
3. classify partial artifacts and settled attempts as recovery evidence, never
   as ownership; and
4. verify immediately before mutation that it remains open, unblocked, and
   without a competing live Dispatch.

Create or reconcile its Task and start the worker through Orca, then record a
durable claim containing the exact Run, Task, Dispatch, coordinator, worktree,
and timestamp. Read both GitHub and Orca back immediately. A comment, assignee,
branch, worktree, or Task without the matching current live Dispatch is not a
claim. If the two systems disagree, stop duplicate dispatch only, repair the
claim record, and continue the blocker-first workflow.

Immediately before accepting, integrating, or closing the issue, re-fetch its
body, relationships, claims, and live Dispatch state. If a competing live claim
appeared, reconcile it before mutation. Otherwise finish the current issue,
advance the frontier, and take the next newly-unblocked issue; do not return a
final response between frontier transitions.

In narration and durable summaries, use `[Issue title](URL)` so humans can scan
the workflow. Bare numbers remain machine identity fields, never the primary
human-readable description.

Start only Tasks reported ready by Orca and unblocked in GitHub. Create one
native Orca child worktree from the current verified parent feature head for
each review unit, then register that existing checkout with
`tools/orca-feature register`. Run member Tasks sequentially in that worktree;
never give concurrent workers the same review-unit branch. Resource-safe review
units may use separate child worktrees concurrently.

The planner selects one execution mode for each member:

- ephemeral helper: an executor-owned Codex subagent with no durable lifecycle;
- shared durable Task: a coordinator-dispatched Orca Task in the review-unit
  worktree, executed sequentially; or
- parallel child Task: a substantial, independently integratable Orca Task in
  one child worktree beneath the review unit.

Use Luna/xhigh for explicit mechanical durable members, Terra/high for coherent
implementation and required consolidation, and a native Orca Claude/Sonnet
worker only when the plan justifies cross-provider work. The parent coordinator
owns all durable dispatches; an executor never becomes a nested orchestrator.
Allow at most one child-worktree layer beneath a review unit. Parallel child
Tasks require disjoint files, generated outputs, databases, ports, and mutable
services. The coordinator integrates their accepted commits into the review-unit
worktree, then dispatches Terra/high consolidation and one combined Sol/high
review. Nested members do not receive independent planning, review, or QA loops.

Before treating overlap as a dispatch blocker, distinguish ordering from active
contention. Apply the approved direction exactly: `A waits for B` blocks A on B,
not B on A. An open but inactive A is not a reason to stop B. Create and verify
the native GitHub blocker edge and matching Orca dependency, record B's exclusive
resource reservation, and dispatch B. Escalate only if A already has live
execution, the directed edge would cycle or contradict another approved plan,
or the required relationship cannot be written and verified.

Each Task spec uses `<task-contract version="1">` and contains:

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

An executor may spawn bounded native Codex subagents for ephemeral independent
work. Mechanical or exploration helpers default to Luna/xhigh; implementation
or debugging helpers use Terra/high. They edit only the leaf worktree and cannot
commit or send lifecycle messages. Never parallelize overlapping files,
generated outputs, databases, ports, indexes, or mutable services.

## 4. Review and finding repair

After executor `worker_done`, validate `implementation-result-v1`, require a
clean worktree, local commit, and validation evidence, and update the worktree
checkpoint. Supporting Tasks are accepted by their consuming review unit and
do not receive standalone code review.

When every member of a coherent review unit is complete, create one separate
review Task using `teck-code-reviewer`, OMX `code-reviewer`, and Sol/high.
Review the combined unit worktree against its exact tip SHA and plan digest. Any later
commit invalidates that review and requires fresh independent review.

Only findings actionable under `review-convergence.md` block integration.
Reuse one GitHub sub-issue and Orca Task per `finding-key`. Repair the affected
unit sequentially; never run its reviewer and repair executor concurrently.
Re-review the unit's new SHA within the cycle limits, then require a convergence
audit and native decision gate.

## 5. Integrate and synchronize

Only the coordinator integrates a CLEAN review unit with
`tools/orca-feature integrate`. Run targeted checks, comment on every member
issue with the integrated SHA and evidence, close accepted members, and re-read
GitHub dependencies before releasing blocked work.

Release the settled Dispatch, remove the native child through Orca, then call
`tools/orca-feature remove`. Never substitute raw `git worktree remove` or
terminal closure for Orca-owned cleanup.

## 6. Final QA and PR

After all leaves are integrated, create a final QA Task in the parent worktree
using `teck-feature-qa`, OMX `qa-tester` plus `verifier`, and Sol/high. QA is
read-only and reviews the entire integrated feature against the parent issue,
approved plan, repository rules, and required validation.
Record the exact parent SHA reviewed by QA. Any later integration, repair,
rebase, or publication change invalidates QA and requires a fresh run.

Only QA findings actionable under `review-convergence.md` become blockers. QA
cannot expand the frozen parent contract or reopen an implementation preference
without new reproducible evidence. Reuse finding state by stable key, repair in
a native child worktree, review the affected repair unit when applicable,
integrate it, and rerun whole-feature QA within the cycle limits. At the limit,
use a convergence audit and native decision gate.

When QA is clean, run proportional feature gates, normally
`nx affected -t build test lint typecheck`. Require `tools/orca-feature pr-info`
to report ready. Publish the parent branch, create one PR with GitHub MCP,
request human review, inspect CI, post evidence, and apply `agent:in-review`
only after required checks are green. Stop with the PR open for a human merge.
