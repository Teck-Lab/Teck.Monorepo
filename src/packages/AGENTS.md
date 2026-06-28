# src/packages/ — TypeScript Shared Libraries

Internal packages consumed by `src/apps/`. Published as workspace dependencies only — not to npm.

## Packages

| Package | Purpose | Consumer |
|---------|---------|----------|
| `ui/` | Web component library — **shadcn/ui** (Tailwind v4, renders DOM) | web apps |
| `ui-native/` | Native component library — **react-native-reusables** (NativeWind v5, renders RN views) | `mobile` |
| `tailwind-config/` | Shared **design tokens** (Tailwind-v4 CSS variables) consumed by both web and native | `ui`, `ui-native`, `web`, `mobile` |
| `api-client/` | Generated OpenAPI types + typed HTTP client — *planned* | web-dashboard |
| `tsconfig/` | Shared TypeScript configs — *planned* (root `tsconfig.base.json` suffices for now) | all apps + packages |

## Web ↔ native shared design system

`@teck/ui` (shadcn) and `@teck/ui-native` (react-native-reusables) are **two
implementations of the same component API** (`<Button variant="outline">` exists
on both with matching names/variants). They **cannot share component code** — web
renders DOM (Radix/`div`), native renders React Native views. What they share is
**design tokens**: `@teck/tailwind-config/tokens.css` defines HSL channel
variables (`--primary`, `--background`, `--radius`, …); both platforms map them to
Tailwind v4 theme colors (web via `@theme` in the app's global CSS, native via
`@source` + `@theme inline` since NativeWind v5 is CSS-first). Change a token once
and **both** platforms update.

## Rules

- **Never import from apps** — packages are leaves, apps are consumers
- **@teck/api-client is auto-generated** — never hand-edit files in `src/generated/`
- **@teck/ui and @teck/ui-native use direct source imports** — apps reference `@teck/ui/src/` / `@teck/ui-native/src/` via path aliases (mobile additionally lists them as workspace deps for Metro)
- **Keep packages focused** — if a package does too many things, split it

## Build

Libraries are built via `@nx/js` with `tsc` (declaration files) or `swc` (faster). Configured per-package in `project.json`.
