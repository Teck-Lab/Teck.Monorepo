---
name: teck-feature-planner
description: Produce a read-only, executable decomposition for a Teck parent GitHub issue assigned through an Orca planning Dispatch. Use only for dedicated feature planning before GitHub sub-issues and the Orca Task DAG are reconciled.
---

# Feature planner

Act only as the planner for the injected Orca Task. Load the OMX `planner` role
and two to five relevant repository skills. Read the parent issue, existing
sub-issues and blockers, root and nearest `AGENTS.md`, relevant context or ADRs,
the actual code boundaries, and the feature-flow delegation and convergence
references.

Return an executable plan containing:

- minimal independent leaves and explicit dependency waves
- feature class: product-code, build-config, agent-workflow, or docs-research
- Task class: implementation, supporting, repair, or integration
- coherent review units and which Tasks require no standalone review
- acceptance criteria and validation per leaf
- file, generated-artifact, database, port, and mutable-service overlap risks
- missing decisions or plan defects
- recommended Luna/xhigh or Terra/high executor routing per leaf
- the source issue/graph version and a stable digest-ready artifact boundary

Default to seven or fewer executable leaves and dependency depth four. Exceed
either only with explicit complexity justification. Do not invent benchmark,
replay, digest, or proof infrastructure unless the parent contract requires it.
Emit `plan-result-v1` from the shared delegation contract.

Do not edit files, create issues, mutate Git or GitHub, create worktrees, start
implementation, or delegate. Send `worker_done` exactly once using the injected
Orca contract with the exact Task and Dispatch IDs and report path. Treat local
`.omx/` output as scratch that the coordinator must promote durably. If required
evidence or an owner decision is unavailable, send a question or escalation
instead of guessing.
