---
name: teck-feature-flow
description: Coordinate a Teck parent GitHub issue through Paseo workspaces and agents, GitHub sub-issues and blockers, model-routed architecture, implementation, review, QA, integration, and one final PR. Use for parent issue intake, dependency reconciliation, worker supervision, integration, and final PR preparation.
---

# Teck feature coordinator

Coordinate the feature from the parent Paseo workspace. Do not implement product
code, replace the delivery architect, review your own work, or perform whole
feature QA in the parent workspace.

## State owners

- GitHub owns durable parent and sub-issues, dependency relationships, comments,
  pull requests, and CI results.
- Paseo owns workspaces, branches, agent sessions, terminals, activity, and
  archival.
- OMP owns the coordinator and worker conversations. Paseo's native OMP adapter
  injects the caller-scoped host tools and records native OMP subagents.
- The Git repository owns commits and the exact trees under review.

Reconstruct all four sources before resuming existing work. Treat stale
sessions, branches, comments, or partial artifacts as evidence to reconcile,
not current ownership.

## Model routing

- `omniroute/teck-orchestrator`: parent coordinator and dependency decisions.
- `omniroute/teck-advisor`: delivery architecture and plan review.
- `omniroute/teck-executor`: implementation, repair, code review, and QA.
- `omniroute/teck-fast-tool`: bounded mechanical work with an exact contract.
- `omniroute/teck-fast-text`: bounded summaries and formatting.

## Workspace and agent lifecycle

Before a delegated task may edit files:

1. Call Paseo `create_workspace` with worktree isolation and branch it from the
   verified parent feature branch.
2. Call `create_agent` in the returned workspace with the required OMP model.
3. Record the GitHub issue, workspace, branch, base SHA, agent, role, and
   acceptance criteria in the coordinator ledger.
4. Supervise through Paseo completion notifications and with
   `get_agent_status`, `get_agent_activity`, and `send_agent_prompt` until the
   agent settles.
5. Validate the reported commit, clean worktree, tests, and evidence before
   accepting it.
6. Archive the workspace only after its accepted commits are reachable from the
   parent branch and no repair or review remains. The committed `paseo.json`
   teardown removes its matching Docker Sandbox.

Never create editable worktrees with raw Git commands, native OMP isolation,
provider-native subagents, tmux, or a nested Docker Sandbox. Native OMP
subagents may perform read-only research or inspection in the current
workspace; their timelines remain visible through Paseo.

Each executable GitHub sub-issue owns one canonical Paseo worktree from the
parent feature branch. Run its ordinary implementation, repair, and review
steps sequentially in that worktree. Use sibling worktrees only for substantial
resource-disjoint tasks whose speedup exceeds integration cost, and integrate
their accepted commits into the canonical sub-issue branch before combined
review. Never share an editable worktree across sub-issues.

## Delivery flow

1. Fetch the complete parent issue, comments, labels, dependency relationships,
   current branch, and any existing Paseo workspaces and agents.
2. Dispatch a read-only delivery architect using `teck-advisor`. Require an
   implementation-ready manifest with review units, expected files,
   dependencies, acceptance criteria, validation, and model routing.
3. Dispatch an independent plan reviewer using `teck-advisor`. Repair the
   manifest until the reviewer accepts it before creating executable work.
4. Materialize one GitHub sub-issue for each coherent review unit. Use the
   ordered sections `## Scope`, `## Acceptance criteria`, `## Validation`, and
   `## Constraints`, with real line breaks. Persist every prerequisite as a
   native GitHub dependency and read it back after mutation.
5. For every dependency-unblocked sub-issue without a live owner, create its
   canonical Paseo workspace and implementation agent. Independent sub-issues
   may run concurrently.
6. Require conventional, GPG-signed commits, a clean worktree, and the planned
   validation. Missing signing capability is a blocker and must not be bypassed.
7. After implementation, start an independent `teck-executor` review agent for
   the exact branch tip. Reviewers report findings and never repair them.
   Dispatch bounded repair agents, then repeat review against the new SHA.
8. Integrate an accepted sub-issue only after its checks and review pass. Close
   and release dependencies, read the graph back, then immediately start newly
   unblocked work.
9. After every sub-issue is integrated, dispatch independent whole-feature QA
   against the final parent tip. Repair and rerun QA until clean.
10. Push the parent branch and create one final pull request. CI handles release
    and deployment after merge.

## Completion gates

- Bind plans, reviews, tests, and QA to immutable artifact digests and Git SHAs.
  A changed artifact or tip invalidates earlier acceptance.
- Reuse one blocker sub-issue per stable finding key. Scope expansions and
  observations that are not required by the approved contract are follow-ups.
- For `A waits for B`, add B to A's native `blocked_by` relationship and verify
  both directions through GitHub. Prose, labels, and comments are not blockers.
- Keep the coordinator turn alive while an expected agent is running. A timeout
  or progress checkpoint is not completion.
- Only the coordinator integrates accepted commits, publishes the parent
  branch, creates the final PR, and changes parent lifecycle labels.
- Do not finish while a live agent, actionable open sub-issue, unreconciled
  dependency, dirty workspace, unreviewed SHA, or failed required check remains.
- Never merge the final PR, create Git tags, run `nx release`, force push, or
  bypass hooks.
