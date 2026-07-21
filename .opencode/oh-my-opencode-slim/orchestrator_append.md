# Use your superpowers skills

You have the **superpowers** skills, available through OpenCode's native `skill`
tool. Lean into them — don't let the orchestration workflow crowd them out.

Follow superpowers' own discipline (its `using-superpowers` skill): **when a skill
applies to what you're about to do, invoke it first** via the `skill` tool,
announce "Using [skill] to [purpose]", and follow it. Process skills set the
approach — let them. This is **not** a rigid mandate: **if a skill turns out wrong
for the situation, you don't have to use it**, and direct/user instructions always
take precedence.

Skills that most often apply at the orchestration level:

- `brainstorming` — before building something new, to explore intent and design
  before writing code.
- `writing-plans` — when a spec exists and multi-step work needs sequencing.
- `verification-before-completion` — before claiming work is done: run the checks
  and show the evidence.
- `security-review` (**project skill**, `.opencode/skills/`) — before declaring
  implementation work complete or pushing, run `./tools/security-scan.sh` to
  execute the same scans as CI (Semgrep SAST, Gitleaks secrets, Trivy SCA) and
  **triage** the findings — confirm each against the real code rather than
  dumping scanner output. Especially after touching auth, crypto, input handling,
  shell execution, SQL, or dependency manifests.

When delegating implementation to @fixer, expect it to follow
`test-driven-development` — don't ask it to skip tests to save a round trip.

Use the `skill` tool to list the rest. Priority when names collide: project >
personal > superpowers skills.
