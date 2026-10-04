# Teck.Monorepo - Agent Instructions

Nx monorepo for the Teck platform, a multi-tenant commerce platform containing
.NET microservices and TypeScript applications. Canonical repository rules live
in this file and the nearest nested `AGENTS.md` for the files being changed.

## Instruction hierarchy

- Read this file before making repository changes.
- Read the nearest nested `AGENTS.md` before editing files in that subtree.
- More specific nested instructions override broader instructions when they
  conflict.
- Keep mandatory architecture and coding constraints in `AGENTS.md`, tests,
  analyzers, or CI so they apply independently of the agent runtime.
- Paperclip owns agent roles, work assignment, and skill distribution. Do not
  vendor or mirror Paperclip-managed skills under `.agents/skills`,
  `.claude/skills`, or `.omp/skills`.
- `.omp/` contains project configuration for the OMP runtime. It is not the
  source of truth for Paperclip agent skills or repository architecture.

## Repository layout

```text
src/
|-- services/          # .NET microservices grouped by domain
|   |-- commerce/      # basket, catalog, customer, inventory, order, pricing
|   |-- operations/    # billing, device, location, statistic
|   |-- content/       # image generation
|   `-- gateway/       # YARP gateways
|-- shared/            # SharedKernel building blocks
|-- apps/              # TypeScript applications
`-- packages/          # TypeScript shared libraries
tests/
tools/
deploy/
specs/
```

## Release groups

| Group | Path | Versioning | Tag pattern |
|---|---|---|---|
| commerce | `src/services/commerce/*` | Fixed | `commerce@{version}` |
| operations | `src/services/operations/*` | Fixed | `operations@{version}` |
| content | `src/services/content/*` | Fixed | `content@{version}` |
| gateway | `src/services/gateway/*` | Fixed | `gateway@{version}` |
| web | `src/apps/*`, `src/packages/*` | Independent | `{projectName}@{version}` |

The canonical release configuration is in `nx.json`.

## Key rules

- Never create Git tags; CI creates release tags.
- Never run `nx release` from a feature branch.
- Commits must use Conventional Commit format (`type(scope): description`) and
  carry a valid GPG signature.
- Fixes and small features use the pre-commit gate, QA review, and merge without
  a preview.
- Medium and larger features require a documented test plan and the `preview`
  label before QA validates the preview.
- The `preview` label is the handoff token between technical leadership and QA.
- Agents hand off at the pull request. CI owns post-merge release activity.

## Build commands

| Command | Purpose |
|---|---|
| `nx affected -t build test lint typecheck` | Run pull-request checks for affected projects |
| `nx graph` | View the dependency graph |
| `nx release --dry-run` | Preview a release |
| `nx release --yes` | Execute a release; CI only |

## Key files

- `nx.json` - Nx plugins, target defaults, and release groups
- `package.json` - Bun workspaces and repository scripts
- `.github/workflows/ci.yml` - pull-request checks
- `.github/workflows/release.yml` - release pipeline
- `.github/workflows/security-scans.yml` - security scanning
