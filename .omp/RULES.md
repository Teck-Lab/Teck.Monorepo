# Paseo workspace ownership

- Paseo's native OMP adapter supplies approvals, session history, provider-managed subagent timelines, and caller-scoped Paseo host tools. Use those tools for workspace and agent lifecycle operations.
- Every Paseo agent for this project must use the `omp` provider with an `omniroute/*` model. Never create direct `codex`, `claude`, `copilot`, `opencode`, or `pi` agents, including as fallbacks when an OMP worker fails.
- The parent OMP session coordinates the feature. It does not implement feature code in the parent workspace.
- Before delegating work that may edit files, call Paseo `create_workspace` with worktree isolation. Branch from the parent feature branch, then launch the worker in the returned workspace with `create_agent` and `omp/omniroute/teck-executor`.
- Every editing, testing, debugging, and substantial review worker must have a visible Paseo session. Supervise it through completion notifications and with `get_agent_status`, `get_agent_activity`, and `send_agent_prompt` until it settles.
- Native OMP subagents may perform read-only research, inspection, or review in a shared workspace. Do not use native OMP subagents, raw Git worktrees, or nested Docker Sandboxes for editable work.
- Paseo owns worktree creation, session lineage, terminals, and archival. The committed `paseo.json` creates one Docker Sandbox for each worktree; archiving the final reference removes it.
- Give each executable GitHub sub-issue one canonical worktree branched from the parent feature branch. Run ordinary tasks sequentially there. Use sibling worktrees only for substantial, resource-disjoint tasks, and integrate their accepted commits before combined review.
- Keep GitHub blocker edges aligned with execution dependencies. Re-read both states after dependency changes and before dispatching newly eligible work.
- Use `teck-orchestrator` for coordination, `teck-advisor` for architecture and plan review, `teck-executor` for implementation and QA, and the fast models only for bounded low-risk work.
- Require signed commits. Import the dedicated sandbox signing key through the supported host path when a worker must commit.
- Merge only after the required checks and independent review pass, then archive the completed Paseo workspace.
