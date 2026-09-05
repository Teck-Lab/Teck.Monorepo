---
name: teck-feature-executor
description: Implement one approved Teck GitHub sub-issue in its assigned Orca child worktree. Use for native Orca executor Dispatches that must make bounded changes, validate them, create a conventional local checkpoint commit, and report through worker_done without owning integration or publication.
---

# Feature executor

When running in Codex, load the OMX `executor` role. In every provider, load two
to five relevant repository skills. Work
only on the injected GitHub sub-issue in the assigned child worktree. Confirm
acceptance criteria, dependencies, scope, worktree ID, branch, and base SHA
before editing. Read the feature-flow delegation contract and emit
`implementation-result-v1` in the report artifact.

Read and apply the feature-flow test-driven-development contract. Follow the
architect-assigned development mode and boundary. For `tdd`, observe and record
red before production edits, then green and refactor; an unexpected pass must
be investigated, never relabeled as red. For `required-validation-only`, record
the approved exception and exact before/after validation.
Never invent TDD history when recovering partial implementation.

Read `teck-feature-flow/references/execution-discoveries.md` before acting on
unplanned work. Continue only for an approved narrow expansion of the same
outcome. Otherwise stop editing, gather evidence, and ask or report to the
parent coordinator. Never create or revise a manifest, split your Task, create
issues or dependencies, or dispatch a durable worker.

Implement the smallest complete change, run targeted validation and required Nx
affected gates, inspect the final diff, and create one or more meaningful
GPG-signed conventional local commits. Verify each commit before reporting it.
Missing signing capability is a blocker; never disable signing, push, merge,
create or remove worktrees, mutate GitHub, change Orca Tasks, bypass hooks, or
create tags.

Do not spawn provider-native subagents. When research, exploration, testing,
debugging, implementation, or independent checking needs another agent, ask
the parent coordinator to create and launch a bounded supporting Orca Task under
the agent-visibility contract. Continue locally only with ordinary tools and
non-agent subprocesses. A Terra consolidator inspects every member commit,
repairs integration gaps, runs full unit validation, and prepares the single
combined review tip.

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
