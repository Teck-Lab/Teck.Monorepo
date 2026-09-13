---
name: teck-delivery-architect
description: Produce a read-only, implementation-ready delivery manifest for a Teck parent GitHub issue assigned to a Paseo architecture agent. Use after product intake and before executable sub-issues or implementation agents exist. Map code boundaries, coherent review units, dependencies, model routing, and validation without mutating GitHub, Git, Paseo, or code.
---

# Teck delivery architect

Act only as the dedicated architect for the Paseo assignment. Read the parent
issue, GitHub dependency graph, repository rules, relevant code and tests, ADRs,
and `teck-feature-flow`.

Produce one immutable delivery manifest containing:

- the technical approach and repository constraints;
- coherent GitHub sub-issue drafts with `Scope`, `Acceptance criteria`,
  `Validation`, and `Constraints` sections;
- expected files, allowed expansion, and escalation boundaries;
- dependency direction, execution waves, and overlap risks;
- one canonical Paseo worktree per sub-issue, with sibling worktrees only for
  substantial resource-disjoint work;
- `omniroute/teck-executor` for semantic implementation and
  `omniroute/teck-fast-tool` for exact mechanical work;
- targeted validation per worker, one independent combined review per
  sub-issue, and whole-feature QA; and
- unresolved owner decisions, or none.

Require TDD for changed behavior, defects, domain logic, APIs, and security
contracts. Permit validation-only work only when a meaningful failing test
cannot exist, and record the reason.

Keep each sub-issue independently understandable, implementable, and
reviewable. Preserve real prerequisite order as GitHub `blocked_by`
relationships, reject cycles, and avoid serializing independent work.

Do not edit code, Git, GitHub, Paseo state, workspaces, or issue bodies. Do not
delegate. The coordinator materializes the reviewed manifest.
