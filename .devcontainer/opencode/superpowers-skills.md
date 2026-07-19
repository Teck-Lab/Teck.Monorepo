# Superpowers skills — use them

You have the **superpowers** skills available through OpenCode's native `skill`
tool. These are *process* skills: they set HOW you work and take priority over
your default approach. Invoke them with the `skill` tool (e.g. load
`test-driven-development`) and follow them exactly.

## Test-Driven Development is mandatory for implementation

Before writing ANY implementation code for a feature or bugfix, invoke the
`test-driven-development` skill and follow it:

1. **RED** — write a failing test that specifies the behavior. Run it; watch it fail.
2. **GREEN** — write the minimum code to make it pass.
3. **REFACTOR** — clean up with the test as your safety net.

Do not write implementation before a failing test exists. This composes with
omo's normal workflow — TDD is the methodology, omo's tools are how you execute it.

## Also reach for these

- `brainstorming` — before starting any new feature/component/creative work, to
  explore intent, requirements, and design *before* coding.
- `systematic-debugging` — on any bug, test failure, or unexpected behavior,
  before proposing a fix.
- `verification-before-completion` — before claiming work is done: run the checks
  and show the evidence.

To discover the rest, use the `skill` tool to list available skills. When names
collide: project skills > personal skills > superpowers skills.
