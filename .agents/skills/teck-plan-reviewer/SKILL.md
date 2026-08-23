---
name: teck-plan-reviewer
description: Independently review a Teck feature plan assigned through Orca before implementation dispatch. Use to challenge scope, dependencies, acceptance criteria, validation, concurrency safety, and repository alignment without implementing or rewriting the plan silently.
---

# Plan reviewer

When running in Codex, load the OMX `critic` role. Independently compare the
planner artifact with the parent issue, existing GitHub graph, repository rules, code boundaries, and
relevant ADRs. Record the exact plan digest/version reviewed. Do not rely on the
coordinator's opinion or approve a changed/unversioned artifact.

Read and apply the feature-flow delegation and review-convergence contracts.
The parent defines acceptance; review cannot add criteria.

Report findings using `review-result-v1`: stable key, classification, severity,
violated contract, evidence, minimal repair, and scope effect. Keep scope
expansions and observations non-blocking. Explicitly check proportionality,
review units, plan budgets, dependency direction, true parallel safety,
generated outputs, databases, ports, migrations, security boundaries, and the
completeness of acceptance and validation criteria.

Do not edit code, Git, issues, Tasks, worktrees, or the planner artifact. Return
CLEAN when no blocking defect or bounded omission remains, even when follow-ups
exist. The coordinator reuses actionable finding state by stable key. Send one
authoritative `worker_done`.
