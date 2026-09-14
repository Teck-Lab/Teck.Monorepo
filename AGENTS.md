# Teck.Monorepo — Agent Instructions

Nx monorepo for the Teck platform — a fresh multi-tenant commerce platform. Contains .NET microservices (Domain + Application + Host) and Next.js frontend applications. Canonical rules live here and in `.github/instructions/`.

## Product discovery routing

When the user naturally says they have an idea, asks to brainstorm, shape,
refine, explore, define, or scope a feature, asks what Teck should build, or
wants a product request turned into a feature request, ticket, or issue, load
`teck-feature-request` immediately. Do not require the user to know, select, or
invoke a skill by name. This route does not apply to an already-created GitHub
issue assigned for engineering delivery or a direct request to implement or fix
known scope.

The router applies Matt Pocock's `grilling` and `domain-modeling` together for
ordinary codebase-aware discovery. It loads `wayfinder` itself only when
unresolved product decisions genuinely require more than one agent session;
Wayfinder maps and children are decision records, never implementation tasks.
When discovery is complete, the router drafts the brief automatically;
never tell the user to issue another command or choose an internal skill.

When discovery needs delegated research, codebase investigation, or a
prototype, use visible Paseo workspaces and agents as defined by
`teck-feature-request`. Keep human decisions and synthesis in the discovery
coordinator, supervise every worker until reconciled, and archive disposable
workspaces after the approved parent issue is created.

The router may publish exactly one GitHub parent issue only after the human
explicitly approves the exact title and body. It must not create an engineering
plan, executable decomposition, engineering agent, product branch or code,
or PR. Engineering begins only when the approved issue is subsequently assigned
or the user explicitly requests delivery.

Matt's `handoff` skill is limited to compressing an unfinished discovery
conversation. Active coordinator and worker ownership remains visible in Paseo.

## Paseo issue routing

Use OMP `omniroute/teck-orchestrator` as the parent coordinator. Paseo's native
OMP provider supplies its session, approvals, subagent timeline, and scoped
host tools.

Every Paseo agent for this project must use the `omp` provider with an
`omniroute/*` model. Never create direct `codex`, `claude`, `copilot`,
`opencode`, or `pi` agents, including as fallbacks when an OMP worker fails.


When Paseo starts a coordinator with a GitHub issue URL matching
`https://github.com/Teck-Lab/Teck.Monorepo/issues/<number>`, treat it as parent
feature intake. Load and follow `teck-feature-flow`. Act as the coordinator; do
not implement, plan, or review the feature directly in the parent worktree.
Dedicated Paseo agents own planning, plan review, leaf execution, coherent
review-unit review, and whole-feature QA.

Before delegated work may edit files, call Paseo `create_workspace` with
worktree isolation, then call `create_agent` in the returned workspace. Every
editing, testing, debugging, and substantial review worker must have a visible
Paseo session. Supervise it through completion notifications and with
`get_agent_status`, `get_agent_activity`, and `send_agent_prompt` until it
settles.

Each executable GitHub sub-issue owns exactly one canonical Paseo worktree from
the main feature branch. Ordinary tasks run sequentially there. The approved
manifest may place substantial, disjoint tasks in sibling worktrees when the
speedup exceeds integration cost. Integrate those branches into the canonical
sub-issue worktree before one combined review. Never share editable worktrees
across sub-issues.

Persist prerequisite ordering as native GitHub issue dependencies and mirror it
in the coordinator ledger; prose, comments, and labels never count as blockers. Use
GitHub MCP for graph reads and supported issue mutations. Because its current
surface cannot mutate dependencies, add `A waits for B` through the GitHub REST
issue-dependency endpoint for A's `blocked_by` collection using B's numeric
database ID, then re-read `blockedBy` and `blocking` through GraphQL or MCP.
Remove and verify the dependency only after B is accepted and integrated.

After starting a supervised worker, the parent coordinator must keep its current
turn alive until every expected agent settles. A progress checkpoint or
still-running worker is not a completion state.

Assignment of a parent issue gives its coordinator outcome ownership through
the final PR, including every dependency required to unblock that issue.
Ownership means a provably live coordinator and agent now; old sessions,
comments, attempts, branches, worktrees, partial artifacts, and completed or
abandoned agents are evidence to reconcile, not owners. When a required blocker is
unowned—even when it belongs to another parent or is partly implemented—the
assigned coordinator must claim/recover it, finish or repair it, independently
review and integrate it, release the dependency, and immediately continue the
newly unblocked work. Cross-parent placement, partial work, and historical
ownership are never external-state stopping conditions.

An explicit Paseo worker assignment takes precedence over the parent-intake
rule. Remain within the assigned worktree and follow the role and completion
contract supplied by that assignment.

## Shared agent skills

`.agents/skills/` is the canonical cross-agent skill source. Claude Code uses
the committed native mirror in `.claude/skills/`. Never edit the mirror by
hand; after changing or installing a canonical skill, run
`tools/sync-agent-skills --write`, then `tools/sync-agent-skills --check`.
The mirror includes complete skill directories, including references, scripts,
assets, and upstream metadata.

## Repository Layout

```
src/
├── services/          ← .NET microservices, grouped by domain
│   ├── commerce/      ← basket, catalog, customer, order, product
│   ├── operations/    ← billing, device, location, statistic
│   ├── content/       ← image-generator (stateless)
│   └── gateway/       ← YARP gateways (public, admin)
├── shared/            ← SharedKernel building blocks
├── apps/              ← TypeScript Next.js applications
└── packages/          ← TypeScript shared libraries
tests/
tools/
deploy/
specs/
```

## Release Groups (from nx.json)

| Group | Path | Versioning | Tag Pattern |
|-------|------|-----------|-------------|
| commerce | src/services/commerce/* | Fixed (all together) | commerce@{version} |
| operations | src/services/operations/* | Fixed | operations@{version} |
| content | src/services/content/* | Fixed | content@{version} |
| gateway | src/services/gateway/* | Fixed | gateway@{version} |
| web | src/apps/*, src/packages/* | Independent | {projectName}@{version} |

## Key Rules

- **NEVER** create git tags — tags are created by CI pipeline only
- **NEVER** run `nx release` from a feature branch
- All commits MUST use conventional commit format (`type(scope): description`) and carry a valid GPG signature
- Fix/small features: pre-commit gate → QA review → merge. No preview.
- Medium+ features: pre-commit gate → test plan → preview label → QA validate preview → merge
- The `preview` label is the handshake token between TL and QA — TL adds it, QA checks for it
- Agents hand off at the PR. CI pipeline handles everything after merge.

## Build Commands

| Command | Purpose |
|---------|---------|
| `nx affected -t build test lint typecheck` | PR checks |
| `nx graph` | View dependency graph |
| `nx release --dry-run` | Preview release |
| `nx release --yes` | Execute release (CI only) |

## Key Files

- `nx.json` — Plugin config, release groups, target defaults
- `package.json` — Bun workspaces, scripts
- `.github/workflows/ci.yml` — PR checks
- `.github/workflows/release.yml` — Release pipeline
- `.github/workflows/security-scans.yml` — Security scanning
