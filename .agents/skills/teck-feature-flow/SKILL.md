---
name: teck-feature-flow
description: Coordinate a Teck feature from a GitHub parent issue and sub-issues using one Orca feature container, internal Git worktrees, full OMO/OpenCode workers, signed conventional commits, local integration, and one final reviewed PR. Use when starting or continuing an issue-backed feature, turning plan-review defects into tracked sub-work, dispatching parallel sub-features, integrating completed worker branches, or preparing the feature PR.
---

# Teck feature flow

Keep one Orca workspace/container per parent feature. Create ordinary Git
worktrees inside it for sub-issues; do not create Orca child workspaces.

Use three separate state owners:

- GitHub MCP: durable parent/sub-issues, comments, PR, reviewers, and checks.
- `tools/orca-feature`: local branches, internal worktrees, and integration state.
- Orca orchestration: live Run, Tasks, dependencies, Dispatches, questions, and completion.

Read [references/workflow.md](references/workflow.md) before starting or
resuming a flow. Load the live Orca orchestration guide with the session's
resolved Orca CLI before running orchestration commands.

## Guardrails

- Let workers commit only in their assigned internal worktrees.
- Use full OMO by default. Keep OMO Slim only as an explicitly selected A/B
  baseline; never load both orchestration plugins in one OpenCode process.
- Use Prometheus -> Atlas for `planned` and `quick` work. Use Hephaestus only
  for explicitly `autonomous` or `spike` work, or a coordinator-approved
  escalation after Atlas has stopped editing.
- Treat tmux as process visibility, never lifecycle authority. Only an Orca
  `worker_done`, `question`, or `escalation` changes coordinator state.
- Let nested OMO agents edit only within the assigned worktree. They may not
  commit, push, merge, create worktrees, mutate GitHub, or send Orca lifecycle
  messages; the primary worker owns those responsibilities.
- Let the coordinator alone merge into and push the parent feature branch.
- Use conventional commits and require commit signing.
- Treat plan-review defects as parent sub-issues with `kind=plan-defect`.
- Represent execution dependencies in Orca Tasks and `tools/orca-feature`.
  Add a durable `Blocked by #...` issue comment when GitHub MCP lacks a
  dependency mutation tool.
- Use one final PR from the parent feature branch to the default branch unless
  the user explicitly requests sub-feature PRs.
- Never expose GitHub MCP file-write/remote-commit, workflow-dispatch,
  review-submission, or merge tools in this flow.
- Never merge the final PR. Request the human review and stop at the PR.
- Never create tags or run `nx release` from the feature branch.

## Recovery

Run `tools/orca-feature status --json`, inspect Orca `task-list` and
`dispatch-show`, and read the GitHub parent/sub-issues before changing state.
Do not infer completion from a terminal becoming idle. Require a valid
`worker_done`, clean worktree, relevant checks, and signed commits before local
integration.
