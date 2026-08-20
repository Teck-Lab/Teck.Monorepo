---
name: teck-feature-flow
description: Coordinate a Teck feature from a GitHub parent issue and sub-issues using one Orca feature environment, native Orca child worktrees, supervised orchestration Tasks, visible tmux-hosted OMO/OpenCode workers, conventional commits, local integration, and one final reviewed PR. Use when planning or decomposing parent work, dispatching dependency-aware sub-issues, supervising OMO workers, integrating child branches, or preparing the feature PR.
---

# Teck feature flow

The parent feature coordinator is native Codex. Codex owns the durable Orca
Run, Task DAG, GitHub reconciliation, worker supervision, integration, and final
PR preparation. It must not implement the feature directly. OMO/OpenCode is
reserved for the plan-only worker and dispatched child implementation workers.

Keep one recipe-backed Orca workspace/container per parent feature. Create one
native Orca child worktree in that environment for each executable GitHub
sub-issue. Do not provision another recipe environment for a child.

Use three separate state owners:

- GitHub MCP: durable parent/sub-issues, comments, PR, reviewers, and checks.
- Native Orca worktrees: child checkout, branch, terminal, and UI lineage.
- `tools/orca-feature`: parent integration bookkeeping and local squash commits.
- Orca orchestration: live Run, Tasks, dependencies, Dispatches, questions, and completion.

Read [references/workflow.md](references/workflow.md) before starting or
resuming a flow. Load the live Orca orchestration guide with the session's
resolved Orca CLI before running orchestration commands.

## Guardrails

- Let workers commit only in their assigned Orca child worktrees.
- Use full OMO as the worker harness.
- Run feature-level planning before materializing missing executable children.
  Let the coordinator review the plan and alone reconcile GitHub sub-issues and
  the Orca DAG. Use Prometheus -> Atlas for planned/quick implementation and
  Hephaestus only for explicitly autonomous/spike work.
- Keep each primary OMO worker in its dedicated tmux session so planner,
  executor, deep-worker, and background-agent activity remains visible. Treat
  tmux as process visibility, never lifecycle authority. Only an Orca
  `worker_done`, `question`, or `escalation` changes coordinator state.
- Let nested OMO agents edit only within the assigned worktree. They may not
  commit, push, merge, create worktrees, mutate GitHub, or send Orca lifecycle
  messages; the primary worker owns those responsibilities.
- Let the coordinator alone merge into and push the parent feature branch.
- Workers may create unsigned conventional checkpoint commits locally. Never
  push a worker branch. The coordinator integrates each completed sub-feature
  as one conventional commit and alone pushes the parent branch.
- Treat plan-review defects as parent sub-issues with `kind=plan-defect`.
- Represent execution dependencies in Orca Tasks and `tools/orca-feature`.
  Add a durable `Blocked by #...` issue comment when GitHub MCP lacks a
  dependency mutation tool.
- Use one final PR from the parent feature branch to the default branch unless
  the user explicitly requests sub-feature PRs.
- Never expose GitHub MCP file-write/remote-commit, workflow-dispatch,
  review-submission, or merge tools in this flow.
- Require the parent lifecycle to be normalized to the single
  `agent:claimed` label before creating the feature Run. Request transitions
  by adding the target label without first removing the current label; the
  lifecycle workflow validates the pair and removes the old label. Apply
  `agent:needs-input` while blocked on a human decision and `agent:in-review`
  after opening the final PR. The workflow applies `agent:completed` when the
  parent closes.
- Never merge the final PR. Request the human review and stop at the PR.
- Never create tags or run `nx release` from the feature branch.

## Recovery

Run `tools/orca-feature status --json`, inspect Orca `task-list` and
`dispatch-show`, and read the GitHub parent/sub-issues before changing state.
Do not infer completion from a terminal becoming idle. Require a valid
`worker_done`, clean worktree, relevant checks, and local commits before App
promotion.
