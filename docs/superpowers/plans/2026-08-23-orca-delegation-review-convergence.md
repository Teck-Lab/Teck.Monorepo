# Orca delegation and review convergence plan

## Goal

Keep Orca as the sole lifecycle orchestrator while making delegated work
machine-checkable, human-readable, proportionate, and guaranteed to converge.
Issue #482 is regression evidence, not implementation scope.

## Invariants

- Orca owns Runs, Tasks, Dispatches, messages, heartbeats, questions, gates,
  retries, and worker release. Repository prompts never duplicate its injected
  lifecycle preamble.
- GitHub issues and approved plans are canonical context. Dispatch contracts
  point to them rather than copying them.
- Clean plan review freezes the acceptance contract. Later review cannot add
  requirements without an explicit native decision gate and fresh plan review.
- Review is organized around coherent review units, not every scheduling Task.
- Whole-feature QA runs on the integrated parent SHA after all required review
  units are accepted.
- Worktree comments/statuses are human checkpoints, never completion authority.

## Deliverables

### 1. Shared contracts

Add progressively disclosed references for:

- a versioned task contract containing two-axis work-kind/workflow-stage
  routing, role, objective, canonical sources,
  scope, acceptance, validation, constraints, permissions, and result type;
- role-specific plan, implementation, review, and QA result contracts;
- a handoff contract that separates verified facts from assumptions and points
  to durable state; and
- review convergence: frozen scope, structured finding classification, stable
  finding keys, bounded repair, and native decision-gate escalation.

The Orca-injected Task/Dispatch identity and `worker_done`, heartbeat, and ask
commands remain native and are not reproduced in templates.

### 2. Planning and review units

The planner classifies work as product code, build/config, agent workflow, or
docs/research. It distinguishes implementation, supporting, repair, and
integration Tasks and groups implementation into coherent review units.

Default plan budgets are seven executable leaves and dependency depth four.
Exceeding either requires explicit justification and coordinator approval.

- Small features normally have one combined code review.
- Medium features have one review per coherent bundle.
- Large or high-risk features independently review security-sensitive or
  independently integratable units.
- Supporting Tasks receive no standalone code review.
- Repairs update and re-review the affected review unit.
- Final QA covers the entire integrated feature branch.

### 3. Findings and convergence

Each finding reports a stable key, classification, severity, exact violated
contract, reproducible evidence, minimal repair, and scope effect. Only a
blocking defect or required bounded omission with all evidence fields blocks.
Scope expansions and observations are non-blocking follow-ups; CLEAN means no
blocking findings, not no recommendations.

Reuse one issue and Orca Task per finding key. Permit at most two automatic
repair/re-review cycles for one key and three findings-present verdicts at one
review stage. At either limit, create a convergence audit and native Orca
decision gate with narrow repair, accept follow-ups, approve scope change, or
owner escalation as explicit outcomes. Do not create another automatic repair.

### 4. Supervision and checkpoints

The coordinator loads `orca skills get orchestration --full` before mutations,
uses JSON receipts, processes an entire FIFO Delivery before acknowledging it,
and remains in the foreground rolling wait loop while work is active.

Workers update the worktree checkpoint after meaningful phase transitions,
reading the existing comment first. Card status progresses through todo,
in-progress, in-review, and completed; completed means accepted and integrated.

Accepted workers are released through Orca; retained workers require explicit
user intent. Archived output is read with Orca worker inspection, not by leaving
settled terminals open. Retries use a new Dispatch and explicit placement.

### 5. Deterministic enforcement

Add validation for task/result contracts and regression tests covering missing
fields, stale identity evidence, contradictory verdicts, duplicate findings,
scope-expansion findings, and convergence limits. Extend the coordinator stop
guard to prevent exiting with unreconciled findings, a required repair, a newly
ready Task, or an unresolved convergence decision.

## Validation

- Existing coordinator-hook tests pass.
- New contract validator tests pass.
- Skill frontmatter and reference links validate.
- Repository searches prove legacy “every Task gets review” and “every finding
  creates a new blocker” rules are removed.
- A fixture modeled on #482 reaches a convergence gate instead of producing a
  fourth automatic review-repair cycle.

## Publication

Commit the workflow hardening on its own branch, push it, create one PR in
Teck-Lab/Teck.Monorepo, and verify the PR head, base, state, and checks. Do not
merge it and do not mutate the example issue.
