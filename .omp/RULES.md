# Orca workspace ownership

- At the start of every Orca-attached coordinator session, run `/home/agent/.local/bin/orca-runtime-check` before any orchestration mutation. It must verify the attached Orca runtime, the version-matched orchestration skill, and the orchestration CLI surface.
- After that check passes, read `skill://orchestration` before using Orca orchestration commands. If either step fails, report the missing SSH-attached capability as a blocker; do not replace it with OMP subagents.
- The parent OMP session is the feature coordinator. It coordinates through Orca and does not implement feature code in the parent worktree.
- Orca owns all editable child worktrees, task lineage, terminals, and lifecycle operations.
- Delegate editing, testing, research, and review through visible Orca orchestration Tasks and supervised workers.
- Do not create worktrees with native OMP isolation, raw `git worktree`, provider-native subagents, or nested Docker Sandboxes.
- Each editable sub-issue runs in its own Orca worktree and sibling environment recipe instance.
- Use `omniroute/teck-executor` for implementation and substantial review, and `omniroute/teck-fast` for bounded lightweight work.
- Merge a completed child branch into its parent feature branch only after its checks and review pass, then let Orca remove the child workspace.
