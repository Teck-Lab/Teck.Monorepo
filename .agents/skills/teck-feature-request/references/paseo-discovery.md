# Paseo discovery delegation

Use Paseo only when discovery needs delegated research, codebase investigation,
or an explicitly requested throwaway prototype. Interactive product choices
stay with the current discovery coordinator and human.

## Placement

- Read-only research or codebase investigation may run in a visible Paseo agent
  in the current workspace with `omniroute/teck-advisor`.
- A prototype that edits files gets one disposable Paseo worktree created with
  `create_workspace`, then a visible `omniroute/teck-executor` agent created
  with `create_agent` in that workspace.
- Workers answer one bounded question. They do not make product decisions,
  mutate GitHub, write an engineering plan, or start nested editable agents.

Record each agent, workspace, question, allowed files, acceptance evidence, and
model in the coordinator notes. Supervise through completion notifications and
with `get_agent_status`, `get_agent_activity`, and `send_agent_prompt`. A
progress message is not completion.

## Result contract

Require each worker to return:

- the exact question and method;
- concise findings with uncertainty;
- named primary-source citations or reproducible code observations;
- artifact paths or links, when any;
- product options and tradeoffs without choosing for the human; and
- unresolved decisions that still require the human.

The coordinator validates the evidence and synthesizes accepted results into
the interview. Keep a prototype workspace until the human has evaluated the
artifact, then archive it without integration. When the discovery frontier is
empty, draft the feature request and obtain explicit approval for its exact
title and body before publishing the one parent issue.
