# Orca agent visibility and lineage contract

Apply this contract to every delegated engineering activity. A delegated agent
must be an Orca Task/Dispatch with its own Orca-created terminal. Provider-native
Claude or Codex subagents are prohibited because they do not own an independent
PTY or pane and their child-row visibility is not guaranteed.

## Task lineage and labels

Create the first architecture Task as the Run's root worker Task. Create every
later Task with `--parent <logical-parent-task-id>` in the same Run:

- plan review is a child of the architecture Task it reviews;
- sub-issue member Tasks are children of the accepted plan-review Task;
- supporting Tasks are children of the member Task that requested them;
- consolidation and code review are children of the sub-issue's final member
  or consolidator Task;
- repair Tasks are children of the review or QA Task that found the defect;
- final QA is a child of the last accepted review/integration Task.

Task parentage expresses UI/provenance hierarchy, not scheduling. Keep real
ordering in `--deps`; never replace dependencies with `--parent`.

Always pass both `--task-title` and `--display-name` to `task-create`. Use short
human-readable values such as `Execute: notification fallback (#562)`. Never
allow the XML contract's first line, a Task ID, or a generic repository name to
become the visible label. Verify the returned Task has the expected `run_id`,
`parent_id`, `task_title`, and `display_name` before dispatch.

## Worktree lineage

Create exactly one canonical editable worktree for every executable GitHub
sub-issue as an explicit direct child of the main feature worktree. Start the
first member with `worker-start --worktree new-child` from the bound feature
worktree, or use the explicit parent-worktree selector supported by the
version-matched guide. Run ordinary later members, consolidation, review, and
repair in that same worktree by its full ID. Never use `new-top-level` or
`--no-parent` for feature execution.

The approved manifest may place substantial, resource-disjoint members in
parallel worktrees one additional level beneath the canonical sub-issue
worktree. Create each with `worker-start --worktree new-child` from that
sub-issue worktree and integrate its accepted commit back before combined
review. Never create a grandchild beneath a parallel-member worktree.

After creation, read `worktree list --json`. Require the canonical worktree's
`parentWorktreeId` to equal the main feature worktree's full ID and each
parallel-member worktree's parent to equal the canonical sub-issue worktree ID.
Persist the GitHub sub-issue-to-canonical-worktree mapping and nested Task
mapping in the claim record. Two sub-issues must never share an editable
worktree. Read-only supporting Tasks may use an explicitly selected existing
worktree; same-worktree workers do not create worktree lineage, so their
Task/Dispatch relationship remains visible in the Agent Map.

## Launch and presentation gate

Launch only with `orca orchestration worker-start`. Use `--agent`, `--model`,
`--effort`, and a readable `--display-name` for a fresh terminal, or
`--terminal` only when transferring the same visible terminal to an immediate
follow-up Dispatch. Do not use generic spawn APIs, provider background Tasks,
manual agent binaries, or low-level `dispatch --inject` when `worker-start` can
express the placement.

Research, testing, debugging, prototyping, and independent checking are
delegation when another agent performs them. Create a bounded supporting Orca
Task for such work. Ordinary tools and non-agent subprocesses remain allowed.

Before treating a worker as successfully started:

1. Record the Run, Task, Dispatch, terminal handle, worktree, and requested and
   effective agent/model/effort from the receipt.
2. Verify provenance with `task-list` and `dispatch-show`.
3. Resolve the exact handle with `terminal list --include-visual-layouts
   --json`. Require a connected, writable terminal represented by a tab/leaf in
   the intended worktree's visual layout.
4. If the worker's terminal title does not match its readable display name,
   correct it with `terminal rename`, then re-read it.
5. Record the Task-parent, worktree-parent, and terminal-layout evidence in the
   coordinator checkpoint before entering the wait loop.

For a federated worker, additionally verify `worker-show` by Dispatch ID. If a
valid Dispatch is absent from the expected lineage or visual layout, do not
claim it is attached and do not launch an overlapping replacement. Preserve
the worker, record exact evidence, and escalate the presentation defect before
launching more delegation.

## Operator surface and cleanup

Enable **Settings → Experimental → Agent Dashboard** to use its kanban and
Agent Map. Enable **Show idle agents** in the dashboard's own gear menu when
completed reusable sessions should remain listed. These settings improve
navigation but do not replace the receipt/lineage/layout gates.

After accepted `worker_done`, immediately reuse the exact terminal for an
approved follow-up or call `worker-release`. Retention requires an explicit
user request and `worker-retain`; released output remains available through
`worker-read`.
