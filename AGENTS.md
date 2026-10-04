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

## Service image groups

| Group | Path |
|---|---|
| operations | `src/services/operations/*` |
| order | `src/services/commerce/order/*` |
| pricing | `src/services/commerce/pricing/*` |
| basket | `src/services/commerce/basket/*` |
| catalog | `src/services/commerce/catalog/*` |
| customer | `src/services/commerce/customer/*` |
| inventory | `src/services/commerce/inventory/*` |
| gateway-public | `src/services/gateway/public/*` |
| web | `src/apps/*`, `src/packages/*` |

`.github/scripts/discover-services.sh` derives these groups from the service
layout. Nx Version Plans and release groups determine which independently
versioned units are released, including dependency-driven bumps. Stable tags
use `<release-group>@v{version}`; container images use that group's semantic
version.

## Key rules

- Never create Git tags or promote a release locally. Add an Nx Version Plan
  with `bun run release:plan`; CI owns versioning, the release PR, tags,
  changelogs, canary prereleases, and stable GitHub Releases.
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

## Key files

- `nx.json` - Nx plugins, target defaults, and project graph configuration
- `package.json` - Bun workspaces and repository scripts
- `.github/workflows/ci.yml` - pull-request checks
- `.github/workflows/release-images.yml` - immutable release-candidate images
- `.github/workflows/release.yml` - release pipeline
- `.github/release-drafter/` - group-scoped draft-release configurations
- `.github/scripts/prepare-release.mjs` - Nx release manifest generation
- `.github/workflows/security-scans.yml` - security scanning
