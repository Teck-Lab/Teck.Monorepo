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

`worker_done --outcome succeeded` means “validate this attempt.” Failed outcome,
escalation, question, missing heartbeat, terminal death, timeout, and idle TUI
are distinct states. Never convert any of them into success. Inspect the live
Dispatch and recorded evidence before retrying. Settle the failed attempt using
the version-matched Orca guide, then create a new attempt with a new Dispatch
ID; never blindly inject or reuse a stale completion. Repeated identical
failure becomes a durable blocker instead of an infinite retry loop.

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

Use one finding issue per stable fingerprint: review kind, reviewed issue,
artifact/SHA, file or component, and normalized finding. Reuse or reopen it
until independent review accepts the repair. Do not create a fresh issue for
each retry, and do not accept an externally closed issue without evidence.

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
