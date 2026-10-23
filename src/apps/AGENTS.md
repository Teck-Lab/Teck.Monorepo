# src/apps/ — TypeScript Frontend Applications

Bun + TypeScript strict mode; lint/format via Biome. Web apps are Next.js 16 App
Router; `mobile/` is the one **Expo + React Native** app (the exception).

## Applications

| App | Package | Stack | Purpose |
|-----|---------|-------|---------|
| `web/` | @teck/web | Next.js 16 + Tailwind v4 + shadcn/ui | Existing minimal public-facing shell; not a functional customer app |
| `mobile/` | @teck/mobile | Expo + NativeWind v5 + react-native-reusables | Expo app; customer role not established |
| `web-dashboard/` | @teck/web-dashboard | Next.js 16 | Existing staff price-list view/edit workflow; broader operational dashboard is planned and is distinct from Payload's editorial Admin |
| `docs/` | @teck/docs | Nextra | Documentation site — *planned* |
| `storybook/` | @teck/storybook | Storybook | Component library docs — *planned* |

Payload CMS is planned and is not currently installed. Its staff Admin is for editorial authoring; published websites are separate renderers of published content. `web-dashboard/` currently implements one staff price-list workflow, not the full operational dashboard. Its Next.js server holds the current browser session and calls the existing public YARP gateway; the intended `web-admin` and `web-customer` gateways are not implemented. `web/` is a minimal shell, not a customer storefront.

> `mobile/` is **Expo + React Native**, not Next.js. It shares **design tokens**
> with web through `@teck/tailwind-config` (so a color/radius change updates both
> platforms) but does **not** share component code — web renders DOM via shadcn
> (`@teck/ui`), native renders RN views via react-native-reusables
> (`@teck/ui-native`). Same component API + token source, two implementations.

## Conventions

- **Server actions**: `next-safe-action` with `zod` schemas
- **API types**: consume generated types from `@teck/api-client`, never hand-write API types
- **Components**: shared **web** components go in `src/packages/ui/` (shadcn), shared **native** components in `src/packages/ui-native/` (react-native-reusables); app-specific in `src/components/`
- **Path aliases**: `@teck/ui` → `src/packages/ui/src/`; `@teck/ui-native` → `src/packages/ui-native/src/`. Mobile also declares `@teck/*` workspace packages as dependencies so Metro (which ignores tsconfig path aliases) can resolve them.
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
