# src/apps/ — TypeScript Next.js Applications

Bun + Next.js 16 App Router + TypeScript strict mode. Lint/format via Biome.

## Applications

| App | Package | Purpose |
|-----|---------|---------|
| `web/` | @teck/web | Public-facing shell |
| `web-dashboard/` | @teck/web-dashboard | Admin dashboard (primary app) |
| `docs/` | @teck/docs | Documentation site (Nextra) |
| `storybook/` | @teck/storybook | Component library docs |

## Conventions

- **Server actions**: `next-safe-action` with `zod` schemas
- **API types**: consume generated types from `@teck/api-client`, never hand-write API types
- **Components**: shared components go in `src/packages/ui/`, app-specific in `src/components/`
- **Path aliases**: `@teck/ui` → `src/packages/ui/src/`
- **No backend coupling**: apps never reference .NET projects directly. Types flow through `specs/ → @teck/api-client`.

## Quality Gates

| Gate | Command |
|------|---------|
| Lint | `bun run lint` (Biome) |
| Format | `bun run format` (Biome) |
| Typecheck | `bun run typecheck` (tsc --noEmit) |
| Build | `bun run build` |

## Codegen

Before implementing new API integrations, run:
```bash
bun run generate   # regenerates @teck/api-client from specs/
```
