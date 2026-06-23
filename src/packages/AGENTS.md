# src/packages/ — TypeScript Shared Libraries

Internal packages consumed by `src/apps/`. Published as workspace dependencies only — not to npm.

## Packages

| Package | Purpose | Consumer |
|---------|---------|----------|
| `api-client/` | Generated OpenAPI types + typed HTTP client | web-dashboard |
| `ui/` | Shared React component library (shadcn-style) | all apps |
| `tailwind-config/` | Shared Tailwind CSS v4 configuration | all apps |
| `tsconfig/` | Shared TypeScript configs (base.json, next.json) | all apps + packages |

## Rules

- **Never import from apps** — packages are leaves, apps are consumers
- **@teck/api-client is auto-generated** — never hand-edit files in `src/generated/`
- **@teck/ui uses direct source imports** — apps reference `@teck/ui/src/` via path aliases
- **Keep packages focused** — if a package does too many things, split it

## Build

Libraries are built via `@nx/js` with `tsc` (declaration files) or `swc` (faster). Configured per-package in `project.json`.
