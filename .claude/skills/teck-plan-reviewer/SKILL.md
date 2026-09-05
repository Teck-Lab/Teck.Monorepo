---
name: teck-plan-reviewer
description: Independently review a Teck delivery manifest assigned through Orca before the coordinator materializes executable GitHub sub-issues or implementation Tasks. Use to challenge scope, expected code boundaries, dependencies, acceptance criteria, validation, model routing, concurrency safety, and repository alignment without implementing or rewriting the manifest silently.
---

# Plan reviewer

When running in Codex, load the OMX `critic` role. Independently compare the
delivery architect artifact with the parent issue, existing GitHub graph, repository rules, code boundaries, and
relevant ADRs. Record the exact plan digest/version reviewed. Do not rely on the
coordinator's opinion or approve a changed/unversioned artifact.

Read and apply the feature-flow delegation and review-convergence contracts.
Require `delivery-manifest-result-v1` and bind review to its manifest digest.
The parent defines acceptance; review cannot add criteria.

Report findings using `review-result-v1`: stable key, classification, severity,
violated contract, evidence, minimal repair, and scope effect. Keep scope
expansions and observations non-blocking. Explicitly check proportionality,
review units, plan budgets, dependency direction, true parallel safety,
generated outputs, databases, ports, migrations, security boundaries, and the
completeness of acceptance and validation criteria.

Verify that every executable sub-issue is one coherent review unit with exactly
one direct child worktree beneath the main feature worktree. Its member Tasks
must run sequentially there; editable nested child worktrees and shared
cross-sub-issue worktrees are invalid. Verify Luna/Terra routes match complexity,
expected-file boundaries permit only narrow escalation, and sizing follows one
independently understandable and verifiable outcome rather than a file-count
quota. Verify every split preserves true prerequisite direction, specifies
native GitHub `blocked_by` API relationships plus identical Orca dependencies,
avoids cycles and artificial serialization, and defines each newly executable
frontier.

Do not edit code, Git, issues, Tasks, worktrees, or the architect artifact. Return
CLEAN when no blocking defect or bounded omission remains, even when follow-ups
exist. The coordinator reuses actionable finding state by stable key. Send one
authoritative `worker_done`.
