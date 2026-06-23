# tools/ — Development Tooling

## Structure

| Directory | Purpose |
|-----------|---------|
| `migrations/` | Consolidated migration runner (dbup) — runs all service migrations in dependency order |

## Rules

- Migration runners reference ALL service `.Infrastructure` projects
- New services must be registered in the migration runner
- Migration scripts live under `tools/migrations/` for add/remove operations
