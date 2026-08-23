---
name: teck-feature-request
description: Guide a Teck product idea from natural-language discovery to a concise, nontechnical feature brief and, only after explicit human approval of the exact draft, publish exactly one GitHub parent issue for Orca planning. Automatically use when a user says they have an idea, asks to brainstorm, shape, refine, explore, define, or scope a feature, asks what Teck should build, or wants a product request turned into a feature request, ticket, or issue—even when they do not know or mention any skill name. Also use to finish discovery from an existing conversation, prototype, research note, or Wayfinder map. Do not use for an already-created GitHub issue assigned for engineering delivery or a direct request to implement/fix known scope.
---

# Teck feature request

Own the natural-language product-discovery entry point through the approved
parent issue. Never require the user to know, choose, or invoke another skill.
Keep the brief readable by a CEO, product manager, domain expert, and engineer.

Read [references/feature-request-format.md](references/feature-request-format.md)
before drafting or publishing.
Read [references/orca-discovery.md](references/orca-discovery.md) completely
before delegating research, codebase investigation, or prototype work.

## Workflow

1. Read the conversation and approved discovery artifacts. Follow links on
   demand; do not duplicate a Wayfinder map, ADR, prototype, or research note.
2. Route automatically; do not ask the user which workflow or skill to use:
   - For an ordinary idea, apply `grilling` and `domain-modeling` together. This
     is the behavior composed by `grill-with-docs`, whose upstream package is
     user-invoked on providers that honor `disable-model-invocation`.
   - For a genuinely multi-session effort whose destination is obscured by
     unresolved product decisions, load and follow the committed `wayfinder`
     skill instructions. Do not select Wayfinder merely because implementation
     will be large.
   - If product intent is already settled, skip further interviewing.
   - When facts or a prototype require delegation, use the native Orca discovery
     Run and worker flow. Never use a hidden provider-native subagent.
3. When the discovery frontier is empty, say so and draft exactly one feature
   brief without requiring a second command or a skill name. Use the reference
   template. Describe the desired outcome and observable behavior, not an
   implementation design.
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
- create executable sub-issues, blocker edges, or engineering Orca Tasks;
- create a Git branch, commit, pull request, or product-code change, or create
  any worktree other than the one disposable Orca prototype exception below;
- turn Wayfinder decision tickets into implementation tickets;
- infer approval from enthusiasm, silence, or approval of an earlier draft; or
- publish into another repository.

Never respond with a menu of internal skill names or tell the user to invoke a
skill as the next step. Describe choices in product language, such as a short
discovery conversation versus a multi-session decision map.

Domain glossary and rare ADR updates made by `domain-modeling`, research notes,
and explicitly requested throwaway prototypes are discovery artifacts, not
implementation. Matt's `handoff` may compress an unfinished discovery session
only; never use it to transfer an active Orca coordinator or worker.

The only permitted branch/worktree exception is an Orca-managed disposable
prototype worktree governed by the discovery reference. Never integrate it into
a product branch or publish a PR from it.
