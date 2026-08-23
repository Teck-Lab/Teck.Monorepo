# Teck.Monorepo — Agent Instructions

Nx monorepo for the Teck platform — a fresh multi-tenant commerce platform. Contains .NET microservices (Domain + Application + Host) and Next.js frontend applications. Canonical rules live here and in `.github/instructions/`.

## Orca issue routing

Prefer Claude Code `claude-opus-5`/high as the parent coordinator. Fall back to
Codex `gpt-5.6-sol`/high only when Claude/model availability,
authentication/capacity, effective-model verification, or startup fails.
Orca's default-agent setting owns the initial launch; this repository owns the
acceptance and fallback contract. Never allow both coordinators to remain live.

When Orca starts either coordinator with a GitHub issue URL matching
`https://github.com/Teck-Lab/Teck.Monorepo/issues/<number>`, treat it as parent
feature intake. Load and follow the `teck-feature-flow` skill and its referenced
workflow, then load the version-matched Orca orchestration guide before running
orchestration commands. Act as the coordinator; do not implement, plan, or
review the feature directly in the parent worktree and do not replace Orca
Dispatches with untracked subagents. Orca owns durable coordination and
worktrees. Dedicated native workers own planning, plan review, leaf
execution, coherent review-unit review, and whole-feature QA. Supporting Tasks
do not receive standalone review. Oh My Codex may supply role and skill
guidance inside a Codex worker, but must not create a second worktree, tmux,
team, or lifecycle system.

After dispatching any supervised worker, the parent coordinator must keep its
current turn alive in Orca's foreground rolling `check --wait` loop until every
expected Dispatch settles. A timeout, empty Delivery, progress checkpoint, or
still-running worker is never permission to return a final response or rely on
a later idle-pointer wake-up.

Assignment of a parent issue gives its coordinator outcome ownership through
the final PR, including every dependency required to unblock that issue.
Ownership means a provably live coordinator/Dispatch now; old Runs, comments,
attempts, branches, worktrees, partial artifacts, and completed or abandoned
Dispatches are evidence to reconcile, not owners. When a required blocker is
unowned—even when it belongs to another parent or is partly implemented—the
assigned coordinator must claim/recover it, finish or repair it, independently
review and integrate it, release the dependency, and immediately continue the
newly unblocked work. Cross-parent placement, partial work, and historical
ownership are never external-state stopping conditions.

An explicit Orca worker Dispatch takes precedence over the parent-intake rule.
Remain within the assigned child worktree and follow the role and completion
contract supplied by that Dispatch.

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
- All commits MUST use conventional commit format: `type(scope): description`
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
