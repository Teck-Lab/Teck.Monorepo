---
name: teck-feature-request
description: Turn completed product discovery into a concise, nontechnical Teck feature brief and, only after explicit human approval of the exact draft, publish exactly one GitHub parent issue for Orca planning. Use when a user has described, brainstormed, grilled, prototyped, researched, or wayfound a product idea and wants it captured as a feature request without creating an engineering plan or starting implementation.
---

# Teck feature request

Convert settled product intent into the parent issue Orca will later plan. Keep
the brief readable by a CEO, product manager, domain expert, and engineer.

Read [references/feature-request-format.md](references/feature-request-format.md)
before drafting or publishing.

## Workflow

1. Read the conversation and approved discovery artifacts. Follow links on
   demand; do not duplicate a Wayfinder map, ADR, prototype, or research note.
2. Resolve only missing product decisions. For an ordinary codebase idea, call
   `grill-with-docs`. For a genuinely multi-session effort whose destination is
   still obscured by unresolved decisions, call `wayfinder` first. Do not use
   Wayfinder merely because implementation will be large.
3. Draft exactly one feature brief using the reference template. Describe the
   desired outcome and observable behavior, not an implementation design.
4. Show the complete title and body to the human. Ask for explicit approval to
   publish that exact draft. Editing approval is not publishing approval.
5. Only after approval, create exactly one GitHub parent issue in
   `Teck-Lab/Teck.Monorepo`, using a Markdown body file rather than escaped
   newline text. Apply `agent:ready` only when the readiness statement is true.
6. Read the created issue back. Verify its title, real line breaks, ordered
   headings, non-empty required sections, label, and URL. Repair any malformed
   issue before reporting success.
7. Return the linked issue title and stop. Assignment or an explicit request to
   start delivery is the separate trigger for `teck-feature-flow`.

## Hard boundary

During this skill, never:

- write an engineering plan, implementation task breakdown, or test plan;
- create executable sub-issues, blocker edges, Orca Runs, Tasks, or Dispatches;
- create a branch, worktree, commit, pull request, or product-code change;
- turn Wayfinder decision tickets into implementation tickets;
- infer approval from enthusiasm, silence, or approval of an earlier draft; or
- publish into another repository.

Domain glossary and rare ADR updates made by `domain-modeling`, research notes,
and explicitly requested throwaway prototypes are discovery artifacts, not
implementation. Matt's `handoff` may compress an unfinished discovery session
only; never use it to transfer an active Orca coordinator or worker.
