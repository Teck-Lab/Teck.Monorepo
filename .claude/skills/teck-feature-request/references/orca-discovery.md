# Orca discovery orchestration

Use native Orca orchestration only when discovery needs delegated research,
codebase investigation, or a throwaway prototype. Interactive product choices
stay with the current discovery coordinator and human.

## Runtime gate

Resolve the Orca executable for the current session and load its complete,
version-matched `orchestration` skill immediately before any orchestration
mutation. The installed guide owns current command names and flags; this
reference owns the workflow invariants.

If Orca is unavailable, continue inline only when no delegation is required. If
the next required discovery fact or prototype needs a worker, report the exact
Orca availability failure and pause. Never replace a visible Orca worker with a
Claude/Codex-native hidden subagent.

## One discovery Run

Create or adopt exactly one Orca Run for the discovery effort. Anchor it to the
current conversation, named product idea, or existing Wayfinder map; a GitHub
feature issue does not exist yet and is not required. The current Claude or
Codex session is the coordinator.

Create one Task per independently answerable question. Use a shallow DAG only
when one finding gates another. Every Task spec uses the versioned discovery
task contract and requires the `teck-discovery-worker` skill. Do not create a
GitHub issue for a discovery Task.

Default routing:

- research or read-only codebase investigation: Codex Terra/high in the active
  worktree, serialized against any writer;
- explicitly requested prototype: Codex Terra/high in one disposable native
  Orca child worktree;
- cross-question product synthesis and every human decision: the current
  Claude Opus 5/high coordinator, or Codex Sol/high fallback already governing
  the session.

Use another supported Claude or Codex worker only when the Task records a
specific capability reason and permitted fallback. Never run both parent
coordinators, and never permit a discovery worker to spawn nested agents.

## Discovery task contract

```xml
<task-contract version="1">
  <routing>
    <work-kind>research|feature</work-kind>
    <workflow-stage>discovery</workflow-stage>
    <route>research:discovery|feature:discovery</route>
  </routing>
  <role>discovery-researcher|discovery-prototyper</role>
  <objective>One bounded discovery question.</objective>
  <sources>
    <discovery-anchor>Conversation, named idea, or Wayfinder URL.</discovery-anchor>
  </sources>
  <scope>Sources or prototype surface this worker owns.</scope>
  <acceptance>Evidence that answers the question without making the decision.</acceptance>
  <validation>Required source checks or prototype run evidence.</validation>
  <constraints>No engineering planning, GitHub mutation, or nested delegation.</constraints>
  <execution-mode>shared-durable|parallel-child</execution-mode>
  <model-route>Requested agent/model/effort and permitted fallback.</model-route>
  <permissions>Read-only, or bounded disposable-prototype edits.</permissions>
  <result-contract>discovery-result-v1</result-contract>
</task-contract>
```

Orca's injected worker preamble exclusively owns Task/Dispatch identity,
heartbeats, questions, and the exact `worker_done` command. Never copy those
values or commands into the Task spec.

## Discovery result

```xml
<discovery-result version="1">
  <routing>Same routing block; workflow-stage is discovery.</routing>
  <outcome>succeeded|failed</outcome>
  <question>The exact bounded question.</question>
  <method>Research, investigation, or prototype method.</method>
  <findings>Concise factual answer, including uncertainty.</findings>
  <evidence>Named primary-source citations and reproducible observations.</evidence>
  <artifacts>Paths or links, or none.</artifacts>
  <product-implications>Options and tradeoffs; never a decision for the human.</product-implications>
  <unresolved-decisions>Questions still requiring the human, or none.</unresolved-decisions>
</discovery-result>
```

Validate the artifact with `tools/teck-agent-contract` before accepting it.

## Foreground supervision

Starting a worker begins supervision. Record its Run, Task, Dispatch, terminal,
and placement receipt, then keep the coordinator turn alive in the same
foreground rolling `check --wait` loop used by `teck-feature-flow`:

1. wait for `worker_done`, escalation, or question Deliveries;
2. treat a timeout, empty Delivery, or yielded command as a checkpoint, never a
   terminal response;
3. process the complete FIFO Delivery before acknowledgement;
4. accept messages only for the exact current Task and Dispatch;
5. validate the result artifact and its cited evidence;
6. release or explicitly reuse the worker through Orca; retain a prototype
   worker and worktree until the human has evaluated the artifact, then discard
   it without integration;
7. acknowledge, dispatch every newly ready discovery Task, and immediately
   continue waiting while any expected Dispatch remains.

Never tell the user Orca will wake the coordinator later and then end the turn.
Never finalize discovery while an active or unreconciled Dispatch, ready Task,
pending question, or required result remains.

## Synthesis and closure

The coordinator synthesizes accepted worker results into the ongoing interview;
workers never decide product intent. When the discovery frontier is empty,
draft the feature request automatically and obtain explicit approval for its
exact title and body. After creating and reading back the one parent issue,
settle the discovery Run and release every worker. Do not create engineering
Tasks in that Run. Later assignment of the approved issue starts a separate
`teck-feature-flow` engineering Run.
