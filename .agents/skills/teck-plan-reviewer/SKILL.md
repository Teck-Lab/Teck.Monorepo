---
name: teck-plan-reviewer
description: Independently review a Teck delivery manifest in a read-only Paseo agent before the coordinator creates executable GitHub sub-issues or implementation agents. Challenge scope, boundaries, dependencies, acceptance criteria, validation, model routing, and concurrency safety without implementing or silently rewriting the manifest.
---

# Teck plan reviewer

Read the parent issue, GitHub dependency graph, repository rules, relevant code
and tests, ADRs, the proposed manifest, and `teck-feature-flow`.

Bind the verdict to the manifest digest. Verify coherent review units, exact
dependency direction, resource ownership, expected file boundaries, TDD or
validation-only choices, model selection, and proportional validation.

Each executable sub-issue must own one canonical Paseo worktree. Permit sibling
worktrees only for substantial resource-disjoint work whose speedup exceeds its
integration cost. Reject shared editable worktrees and dependency cycles.

Return stable, classified findings with contract evidence. Keep scope expansion
ideas non-blocking. Do not edit code, Git, GitHub, Paseo, or the manifest.
Return CLEAN when no blocking defect or bounded omission remains.
