# Orca workspace ownership

- At the start of every Orca-attached coordinator session, run `/home/agent/.local/bin/orca-runtime-check` before any orchestration mutation. It must verify the attached Orca runtime, the version-matched orchestration skill, and the orchestration CLI surface.
- After that check passes, read `skill://orchestration` before using Orca orchestration commands. If either step fails, report the missing SSH-attached capability as a blocker; do not replace it with OMP subagents.
- The parent OMP session is the feature coordinator. It coordinates through Orca and does not implement feature code in the parent worktree.
- Orca owns all editable child worktrees, task lineage, terminals, and lifecycle operations.
- Delegate editing, testing, research, and review through visible Orca orchestration Tasks and supervised workers.
- Do not create worktrees with native OMP isolation, raw `git worktree`, provider-native subagents, or nested Docker Sandboxes.
- Each executable GitHub sub-issue runs in exactly one direct Orca child worktree beneath the main feature worktree and in its own sibling environment recipe instance. Run that sub-issue's editing, review, and repair Tasks sequentially there; never share or nest editable worktrees.
- Mirror approved prerequisite order in both Orca Task `--deps` and native GitHub issue dependencies. Prose, comments, and labels never count as blockers. Use GitHub MCP for graph reads and supported issue writes; use the GitHub REST or GraphQL dependency API for `blocked_by` mutations because the current MCP surface cannot perform them, then read both graphs back before dispatch.
- Use `omniroute/teck-executor` for implementation and substantial review, and `omniroute/teck-fast` for bounded lightweight work.
- Merge a completed child branch into its parent feature branch only after its checks and review pass, then let Orca remove the child workspace.
