# Product discovery and feature intake

## Goal

Give a nontechnical stakeholder a conversational path from a loose product idea
to one readable, approved GitHub parent issue, without allowing product
discovery to become a second engineering planner or orchestrator.

## Flow

```text
ordinary idea
  -> grill-with-docs
  -> teck-feature-request draft
  -> explicit human publication approval
  -> one agent:ready GitHub parent issue
  -> later assignment
  -> teck-feature-flow / Orca planning and delivery

huge, decision-fogged idea
  -> wayfinder decision map
  -> teck-feature-request draft linking the completed map
  -> the same approval and Orca boundary
```

`grill-with-docs` is the normal codebase-aware entry. Wayfinder is selected by
decision uncertainty and session size, not implementation size. Its map and
child tickets record product decisions; they never become executable Orca
sub-issues or Tasks.

## Skill packages

The canonical `.agents/skills/` tree vendors complete, unchanged packages from
`mattpocock/skills` commit
`5b15a47f2d7150f545fbcacbfe381787fc0230dc`:

- `grill-with-docs` and its `grilling` and `domain-modeling` dependencies;
- `wayfinder`, plus `research` and `prototype`; and
- `handoff`, limited by repository policy to unfinished discovery-session
  compression.

`ask-matt`, `to-spec`, `to-tickets`, and `implement` are deliberately absent.
Their downstream lifecycle overlaps with Orca, which remains the sole owner of
engineering planning, decomposition, worktrees, Dispatches, integration,
review, QA, and the final PR.

The Teck-authored `teck-feature-request` skill is the only bridge. It drafts the
product brief, requires approval of the exact title and body before mutation,
creates at most one parent issue, verifies GitHub read-back, and stops.

## Cross-agent support

`.agents/skills/` remains canonical. `tools/sync-agent-skills --write` creates a
complete native `.claude/skills/` copy, including every upstream reference and
`agents/openai.yaml`. Neither Claude nor Codex relies on a symlink or a
provider-specific plugin for this flow.

## Acceptance checks

- Every selected upstream skill and bundled resource exists in both trees.
- The mirrors compare byte-for-byte through `tools/sync-agent-skills --check`.
- The Teck skill passes the system skill validator.
- Tests enforce explicit approval, single-parent publication, discovery/Orca
  separation, and the two distinct handoff meanings.
