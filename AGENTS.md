# Teck.Monorepo — Agent Instructions

Nx monorepo for the Teck platform — a fresh multi-tenant commerce platform. Contains .NET microservices (Domain + Application + Host) and Next.js frontend applications. Canonical rules live here and in `.github/instructions/`.

## Orca issue routing

When Orca starts the primary agent with a GitHub issue URL matching
`https://github.com/Teck-Lab/Teck.Monorepo/issues/<number>`, treat it as parent
feature intake. Load and follow the `teck-feature-flow` skill and its referenced
workflow, then load the version-matched Orca orchestration guide before running
orchestration commands. Act as the coordinator; do not implement the feature
directly in the parent worktree or replace Orca Dispatches with untracked
subagents.

An explicit Orca worker Dispatch takes precedence over the parent-intake rule.
Remain within the assigned child worktree and follow the role and completion
contract supplied by that Dispatch.

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
