# Use your superpowers skills

You have the **superpowers** skills, available through OpenCode's native `skill`
tool. Lean into them.

Follow superpowers' own discipline (its `using-superpowers` skill): **when a skill
applies to what you're about to do, invoke it first** via the `skill` tool,
announce "Using [skill] to [purpose]", and follow it. This is **not** a rigid
mandate: **if a skill turns out wrong for the situation, you don't have to use
it**, and direct/user instructions always take precedence.

As the implementation specialist, these apply to you most:

- `test-driven-development` — when implementing a feature or bugfix: write the
  test first, watch it fail, then minimal code to pass. It's the *default* way to
  work on real implementation; the skill's own exception is throwaway prototypes
  (ask first). Use it because it fits, not because you're forced to.
- `systematic-debugging` — on a bug, test failure, or unexpected behavior, before
  proposing a fix.
- `verification-before-completion` — before claiming work is done: run the checks
  and show the evidence.
- `security-review` (**project skill**, `.opencode/skills/`) — before declaring
  implementation work complete or pushing, run `./tools/security-scan.sh` and
  **triage** the findings against the real code rather than dumping scanner
  output. Especially after touching auth, crypto, input handling, shell
  execution, SQL, or dependency manifests.

Repository conventions live in the `AGENTS.md` tree — read the one nearest the
code you're touching before editing. `CLAUDE.md` at the repo root summarizes the
architecture rules that ArchUnitNET tests enforce as build failures.

Use the `skill` tool to list the rest. Priority when names collide: project >
personal > superpowers skills.
