---
name: orchestration
description: >-
  Use Orca orchestration for structured multi-agent coordination: threaded
  messages, blocking ask/reply flows, task dispatch, worker_done/escalation
  waits, task DAGs, decision gates, coordinator loops, or decomposing work
  across agents. Use `orca-cli` instead for full ownership handoffs, including
  requests phrased as "hand off", "handoff", "handover", "give this to another
  agent", or "another worktree" when the user did not explicitly ask to
  supervise, monitor, wait for results, or coordinate a DAG. Use `orca-cli` for
  terminal control, lightweight terminal prompts, shell commands, Orca
  worktree management, reading or waiting on terminals, and automation of the
  browser embedded inside Orca. Use Computer Use for external browser windows,
  webviews, Orca app UI, or desktop UI outside Orca's embedded browser only when
  the task requires OS/window-level control such as focus, menus, dialogs,
  coordinates, or screenshots. Use `orca-cli` for Orca's embedded pages and a
  page-automation tool such as Playwright or CDP for external pages.
---

# Orca Orchestration

This file is a discovery stub, not the usage guide. The full, version-matched Orca
orchestration reference is served by the `orca` binary itself so it cannot drift from
the binary that handles orchestration commands.

Engage Orca orchestration for structured multi-agent coordination: threaded messages,
blocking ask/reply flows, task dispatch, worker completion waits, task DAGs, decision
gates, coordinator loops, or decomposition across agents. Coordination requires real
Orca runtime state; never substitute a non-Orca subagent tool.

## Resolve the CLI for this session

Choose the executable once and reuse it:

- If `ORCA_CLI_COMMAND` is set, use its value.
- Otherwise, in a development checkout with `ORCA_DEV_REPO_ROOT`, use `orca-dev`.
- Otherwise, on Linux outside an Orca-managed terminal, use `orca-ide`.
- Otherwise, use `orca`.

If the selected executable cannot run, report its exact error and stop. Do not fall
through to another executable, which could silently target another Orca build.

## Load the version-matched guide

```text
ORCA skills get orchestration
```

Read that output before orchestration mutations. Confirm the runtime first with
`ORCA status --json`, and prefer JSON receipts.

If `skills get` is explicitly reported as unknown by an older Orca, use only this
bounded read-only bootstrap:

```text
ORCA status --json
ORCA orchestration task-list --json
ORCA terminal list --json
```

Then report that updating Orca restores the version-matched guide. Do not guess the
older command surface.
