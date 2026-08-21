# Coordinator state machine and failure contract

This contract prevents a worker exit, terminal state, partial mutation, or
stale review from being mistaken for feature progress. Apply it at intake,
after every message or mutation, after interruption, and before yielding.

## Attempt identity and durable evidence

For every Dispatch record the parent issue, child issue, Orca Run and Task,
Dispatch ID, terminal handle, attempt number, role, worktree ID/path, branch,
base SHA, expected branch-tip SHA, artifact path and digest, and reservations.
Persist durable identities in the GitHub issue evidence and Orca Task/Dispatch;
local runtime files are caches only.

Accept a lifecycle message only when its Task ID and Dispatch ID exactly match
the current attempt. Treat messages from superseded attempts as stale evidence:
do not complete the current Task, release dependencies, or repeat their side
effects. Process a valid message completely before acknowledging its delivery.
If durable persistence or reconciliation fails, leave it unacknowledged so the
coordinator can safely resume.

A valid `worker_done --outcome succeeded` automatically completes its matching
Orca Task and Dispatch and means “validate this attempt” to the coordinator.
Never redundantly call `task-update --status completed`. Artifact acceptance,
GitHub issue closure, blocker release, integration, and downstream dispatch
still require coordinator validation and reconciliation. Failed outcome,
escalation, question, missing heartbeat, terminal death, timeout, and idle TUI
are distinct states. Never convert any of them into success. Inspect the live
Dispatch and recorded evidence before retrying. Settle a failed attempt using
the version-matched Orca guide, then create the appropriate recovery Task or
new attempt with a new Dispatch ID; never blindly inject or reuse a stale
completion. Repeated identical failure becomes a durable blocker instead of an
infinite retry loop.

## Resume and orphan recovery

At the beginning of every coordinator session, and after any interruption:

1. re-read the GitHub parent, sub-issues, blocker edges, labels, comments, PR,
   checks, and relevant security alert state;
2. re-read the Orca Run, Task DAG, ready Tasks, Dispatches, deliveries,
   questions, escalations, and worker leases;
3. inspect the feature ledger and every native worktree, branch, base, tip,
   cleanliness state, commit reachability, and resource reservation;
4. match records by durable IDs and classify each as active, delivered but
   unreconciled, settled, failed, stale, missing, or orphaned; and
5. reconcile existing records idempotently before creating any issue, edge,
   Task, Dispatch, worktree, branch, or comment.

Do not infer completion from a closed terminal, missing pane, process exit,
GitHub issue state, or an artifact on disk. Do not recreate an object merely
because it is absent from one view. A progress/status request or direct human
interruption pauses the loop only after the current identities and blocker are
recorded; it never settles work.

## GitHub and Orca convergence

GitHub owns the durable issue and dependency graph. Orca owns execution state.
Mutate one relationship at a time, re-read it, mirror it, and then re-read both
graphs. Dispatch eligibility requires agreement in both systems plus the
resource-safety check below; a `ready` label or ready Task alone is insufficient.

If one half of a mutation succeeds and the other fails, stop affected dispatch,
create or reuse one synchronization-defect sub-issue with a deterministic
fingerprint, and repair the missing half idempotently. Never erase the
successful half to hide drift. Before adding an edge, reject self-dependencies,
duplicates, reversed producer/consumer direction, and cycles.

Dependency direction is semantic, not inferred from issue state. The approved
plan statement `A waits for B` means A has a `blocked_by` edge to B and the Orca
Task for A depends on B. An open A does not block B merely because their file or
resource scopes overlap. If neither issue has active execution and the approved
direction is safe, create and verify the missing GitHub edge and Orca dependency,
reserve the shared resource for B, and dispatch B. Stop only when a live worker,
worktree, conflicting immutable plan, cycle, or missing mutation authority makes
the approved ordering unsafe or impossible.

Use one finding issue per stable fingerprint: review kind, reviewed issue,
artifact/SHA, file or component, and normalized finding. Reuse or reopen it
until independent review accepts the repair. Do not create a fresh issue for
each retry, and do not accept an externally closed issue without evidence.

GitHub issue readability is part of durable convergence. Every coordinator
issue body uses real Markdown line breaks and exactly one ordered `## Scope`,
`## Acceptance criteria`, `## Validation`, and `## Constraints` section with
non-empty content. Use body-file input, read the stored body back, and treat
literal `\\n` sequences or malformed sections as a failed half-mutation. Repair
that issue idempotently before mirroring it into Orca or dispatching any worker.

## Blocker-first DAG progression

An actionable blocker is executable work, not a reason for the coordinator to
stop. Before dispatch, its GitHub sub-issue and Orca Task must cite the approved
plan version, exact scope, acceptance criteria, dependencies, validation, and
owned resources. If that contract is absent or changed, dispatch planning and
independent plan review for the blocker first; do not send an executor an
unreviewed finding description.

Parent assignment is outcome ownership. It includes required blockers even
when they are attached to another parent, have records from older Runs, or are
partly implemented. Treat ownership as a live lease: only a verified live
coordinator plus its current active Dispatch owns the work. Historical or
settled state never owns anything.

If no live owner exists, the assigned coordinator must adopt the existing
blocker without duplicating its GitHub issue, reconcile recoverable partial
state by immutable IDs and SHAs, and continue from the first unproven gate.
Finish or repair implementation, repeat independent review when evidence is
stale, integrate the accepted SHA into the required base, release the blocker,
and dispatch the newly-ready dependent wave. “External,” “cross-parent,”
“previously owned,” “partly done,” and “another Run touched it” are forbidden
terminal classifications for actionable implementation work.

If a verified live owner exists, prevent duplicate editing but keep outcome
supervision with the assigned parent coordinator through an explicit tracked
handoff/dependency. The parent coordinator remains active until accepted
integration or a genuinely human-only authority/access blocker; it cannot turn
another agent's live ownership into permission to return a final response.

Model selection like a tracker frontier: eligible work is open, unblocked,
resource-safe, and unclaimed. A valid claim is two-sided durable evidence—the
GitHub issue records exact Run/Task/Dispatch/coordinator identity and Orca proves
that same Dispatch is current and live. Assignees, comments, branches,
worktrees, artifacts, and historical Tasks are insufficient alone. Fetch the
complete issue and candidate Orca state immediately before claim, read both
sides back after claim, and repeat the read immediately before acceptance,
integration, or closure to close the claim race.

Use linked issue titles in human-facing maps and summaries. Bare IDs remain
stable machine keys but never replace readable names. Each executable contract
lives on exactly one canonical issue; parents and dependencies link and gist it
instead of copying a second version that can drift.

Dispatch ready blocker Tasks before their blocked dependents. A successful
worker report does not release the edge: validate, independently review,
integrate, and reconcile the blocker in GitHub and Orca first. Then re-read both
graphs, identify every Task made ready by that transition, and dispatch all
resource-safe eligible Tasks in the next wave before the coordinator idles.
If a blocker repair fails review, keep its issue and edges open, create or reuse
the repair Task, and repeat the repair/review cycle.

Parent completion is a graph-wide condition. Enumerate all current sub-issues
recursively and prove each is either closed with accepted evidence or durably
classified non-actionable. The parent remains incomplete while any actionable
child is open, any dependency or synchronization edge is unresolved, any Task
is ready/dispatched/blocked by remediable work, or any required review or QA
rerun remains.

## Plans and review freshness

Ignored `.omx/` plans and other worker-local files are scratch space. Before a
planning Dispatch is settled, promote the approved plan contract to durable
GitHub evidence and the Orca Task graph, recording its digest and version.
Executable Tasks cite that immutable version. A plan correction changes the
digest, invalidates its prior approval, and requires a fresh plan-review
Dispatch before leaves become eligible.

Plan review, code review, and QA are independent Dispatches. Bind code review
to the exact leaf tip SHA and QA to the exact integrated parent SHA. Any commit,
integration, conflict resolution, rebase, forceful remote movement, generated
artifact change, or plan digest change invalidates affected approval. Never
reuse a review merely because its issue or Task previously said clean.

## Worker-result acceptance

Reject an implementation result unless all are true:

- Task/Dispatch identity and assigned GitHub issue match;
- worktree ID, path, branch, base, and reported tip match live state;
- the worktree is clean and all intended changes are committed;
- commits are reachable from the reported tip and not already integrated;
- changed files and generated outputs remain within assigned scope;
- required validation is fresh, reproducible, and bound to the reported SHA;
- no prohibited GitHub, Orca, worktree, publication, or lifecycle mutation was
  performed by the worker; and
- no dependency, reservation, security, or owner-decision blocker remains.

Rejection keeps the GitHub issue and dependency open. Record the reason and
redispatch a bounded repair or escalate; do not silently fix it as coordinator.

## Concurrency and resources

Before each wave compare scopes, file sets, generated outputs, migrations,
databases, ports, indexes, caches, and mutable external services. Record owners
and cleanup contracts for exclusive resources. Workers may run concurrently
only when both dependency graphs permit it and all writes/resources are
disjoint. Conflicting generated outputs or a shared fixed resource makes Tasks
sequential even if their graph labels claim independence.

## Integration, CI, security, and cleanup

Immediately before integration re-read issue state, blockers, reviewed leaf
tip, parent tip, remote refs, and reservations. Integrate one reviewed leaf at
a time. Conflict, parent drift, remote drift, or failed validation leaves the
issue open and the worktree intact and creates/reuses an integration-defect
blocker; never use wholesale ours/theirs, force, or hook bypass.

Bind final QA and required CI evidence to the exact published PR head and base.
Any new commit makes previous evidence stale. A green workflow name is not a
substitute for required concrete security-alert evidence. Security-managed
issues remain open until the authoritative post-merge alert is resolved or
dismissed, even when code and PR work are otherwise complete.

Remove a child worktree only after its accepted commits are reachable from the
parent, its issue and Task are reconciled, its Dispatch is settled/released,
and reservations are released. Cleanup failure is tracked and retried; it does
not roll back accepted code, but it prevents claiming a fully clean run. Audit
orphaned terminals, leases, worktrees, branches, and reservations on resume.

## Mandatory exit audit

The coordinator may stop normally only when all applicable answers are no:

- unacknowledged delivery, question, escalation, or failed attempt;
- active, stale, orphaned, or delivered-but-unreconciled Dispatch;
- ready/eligible Task not dispatched and no recorded blocker;
- open actionable, synchronization, plan, review, QA, or integration issue;
- disagreement between GitHub edges and Orca dependencies;
- unpromoted plan artifact or stale plan/review/QA/validation evidence;
- dirty, divergent, unintegrated, or unaccounted worktree/branch;
- unreleased lease, terminal, resource reservation, or failed cleanup;
- changed PR head/base or missing/failing required CI/security evidence; or
- lifecycle label/state inconsistent with the actual run.

If any answer is yes, continue reconciliation or record the exact durable human
or external blocker. “Worker finished,” “terminal idle,” “progress reported,”
and “sub-issue exists” are never valid terminal conditions.

For Codex coordinators, `.codex/hooks.json` enforces the most frequently missed
edge: an actionable review delivery sets a session-local
`repairDispatchRequired` latch. Only a successful `orca orchestration
worker-start` clears it. A `Stop` attempt while latched returns `decision:
block`, turning the missing repair dispatch into the next model turn instead of
allowing a progress-only response to strand the Run.
