---
name: teck-feature-planner
description: Produce a read-only, executable decomposition for a Teck parent GitHub issue assigned through an Orca planning Dispatch. Use only for dedicated feature planning before GitHub sub-issues and the Orca Task DAG are reconciled.
---

# Feature planner

Act only as the planner for the injected Orca Task. Load the OMX `planner` role
and two to five relevant repository skills. Read the parent issue, existing
sub-issues and blockers, root and nearest `AGENTS.md`, relevant context or ADRs,
and the actual code boundaries.

Return an executable plan containing:

- minimal independent leaves and explicit dependency waves
- acceptance criteria and validation per leaf
- file, generated-artifact, database, port, and mutable-service overlap risks
- missing decisions or plan defects
- recommended Luna/xhigh or Terra/high executor routing per leaf
- the source issue/graph version and a stable digest-ready artifact boundary

Do not edit files, create issues, mutate Git or GitHub, create worktrees, start
implementation, or delegate. Send `worker_done` exactly once using the injected
Orca contract with the exact Task and Dispatch IDs and report path. Treat local
`.omx/` output as scratch that the coordinator must promote durably. If required
evidence or an owner decision is unavailable, send a question or escalation
instead of guessing.
