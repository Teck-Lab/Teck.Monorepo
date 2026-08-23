---
name: teck-feature-executor
description: Implement one approved Teck GitHub sub-issue in its assigned Orca child worktree. Use for native Codex executor Dispatches that must make bounded changes, validate them, create a conventional local checkpoint commit, and report through worker_done without owning integration or publication.
---

# Feature executor

Load the OMX `executor` role and two to five relevant repository skills. Work
only on the injected GitHub sub-issue in the assigned child worktree. Confirm
acceptance criteria, dependencies, scope, worktree ID, branch, and base SHA
before editing. Read the feature-flow delegation contract and emit
`implementation-result-v1` in the report artifact.

Implement the smallest complete change, run targeted validation and required Nx
affected gates, inspect the final diff, and create one or more meaningful
unsigned conventional local commits. Never push, merge, create or remove
worktrees, mutate GitHub, change Orca Tasks, bypass hooks, or create tags.

You may spawn bounded native Codex subagents for independent exploration,
testing, or implementation. Give each a disjoint scope. They may edit only this
worktree and may not commit, mutate GitHub or Orca, or send lifecycle messages.
Do not parallelize overlapping files, generated artifacts, databases, ports,
indexes, or mutable services. You own their review, final validation, commits,
and exactly one `worker_done`.

Ephemeral subagents are not durable issue owners and cannot launch Claude.
When work needs a GitHub sub-issue, independent lifecycle, provider selection,
or a child worktree, ask the parent coordinator to create and dispatch the Orca
Task. A Terra consolidator inspects every member commit, repairs integration
gaps, runs full unit validation, and prepares the single combined review tip.

Report the exact Task/Dispatch IDs, base and tip SHAs, commits, files, validation
evidence, and remaining risks. A failed or incomplete attempt must report a
failed outcome or escalation; never describe it as success because the process
is ending.

Ask the coordinator through Orca when scope, architecture, dependencies,
security policy, or acceptance criteria require a decision.

At meaningful phase transitions, read the existing Orca worktree comment and
update it without clobbering valid user context. Use concise checkpoints for
investigating, implementing, validating, ready for review, and blocked. A
worker never marks the card completed; acceptance and integration own that
transition.
