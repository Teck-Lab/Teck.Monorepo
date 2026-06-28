# Frontend Bootstrap + Expo Tooling — Design Spec

**Date:** 2026-06-26
**Status:** Approved (brainstorming) — pending implementation plan
**Branch:** `worktree-frontend-bootstrap` (off `main`; separate from devcontainer PR #2)

## Goal

Stand up the repository's first frontend projects and add Expo/React Native
tooling to the Nx workspace and dev container. Establish a **shared design
system** so styling and components stay consistent across web and native:

- **Web:** Next.js 16 + Tailwind v4 + **shadcn/ui**.
- **Native:** Expo + **NativeWind v5** + **react-native-reusables** (RNR).
- **Shared:** a single design-token source consumed by both.

This is a *bootstrap* — enough to prove the toolchain and the shared-style
pipeline end to end, not a complete component library or app suite.

## Context (starting state)

- No JS/TS projects exist yet: `src/apps/` and `src/packages/` contain only
  `AGENTS.md` placeholders; there are zero `project.json` files.
- Nx is partly wired for web: `nx.json` registers `@nx/dotnet`, `@nx/docker`,
  `@nx/next` (`include: src/apps/**`), and `@nx/storybook`. A `web` release
  group already covers `src/apps/*` + `src/packages/*` (independent versioning).
- `@nx/expo` and `@nx/react` are **not** present.
- Package manager is **Bun** (`bun@1.2.0`); quality gates are **Biome + tsc**.
- `src/apps/AGENTS.md` currently describes apps as **web-only Next.js**; this
  spec deliberately adds a mobile (Expo) category and updates that doc.

## Decisions (resolved during brainstorming)

| Decision | Choice |
|---|---|
| Expo toolchain weight | **Light** in-image (CLIs on demand, port forwards, extension); **heavy** Android SDK/JDK/watchman documented as opt-in only |
| Scaffold depth | **Minimal runnable** projects, but with the full **shadcn / Tailwind-v4 / NativeWind-v5 / RNR** styling pipeline wired and one proof component |
| Native components location | Separate **`@teck/ui-native`** package (parallel to web `@teck/ui`) |
| Tailwind v4 on native | **NativeWind v5 (pre-release)** for true Tailwind-v4 parity; shared token values bridge web and native |
| Where this lands | **New branch + PR off `main`**, separate from devcontainer PR #2 |

## Projects

| Package | Path | Generator | Stack / role |
|---|---|---|---|
| `@teck/web` | `src/apps/web` | `@nx/next:app` | Next.js 16 App Router + Tailwind v4 + shadcn/ui |
| `@teck/mobile` | `src/apps/mobile` | `@nx/expo:app` | Expo + NativeWind v5 + RNR |
| `@teck/ui` | `src/packages/ui` | `@nx/react:lib` | Web shadcn components (seed: `Button`), consumed by `web` |
| `@teck/ui-native` | `src/packages/ui-native` | `@nx/react:lib` (RN-targeted) | RNR native components (seed: `Button`), consumed by `mobile` |
| `@teck/tailwind-config` | `src/packages/tailwind-config` | `@nx/js:lib` | Shared design tokens (Tailwind v4 CSS-first variables) consumed by both web and native |

## Architecture: the shared design system

Web and native **cannot share component code** — web shadcn renders DOM
(Radix primitives, `div`), native renders React Native views (`@rn-primitives`).
What is shared:

1. **Design tokens** (`@teck/tailwind-config`) — the single source of truth for
   colors, radius, spacing, expressed the Tailwind-v4 CSS-first way (CSS
   variables / `@theme`). Web's Tailwind config and native's NativeWind config
   both import these token values, so a change to the primary color updates both
   platforms.
2. **A mirrored component API** — `react-native-reusables` is the React Native
   port of shadcn/ui, so `<Button variant="outline">` exists with matching
   names/variants on both sides: two implementations, one token source, one
   mental model.

```
@teck/tailwind-config  (tokens: CSS variables / theme)
        │                         │
        ▼                         ▼
   @teck/ui (shadcn)        @teck/ui-native (RNR)
        │                         │
        ▼                         ▼
   @teck/web (Next.js)      @teck/mobile (Expo)
```

### Tailwind v4 / NativeWind v5 status (verified 2026-06)

- **Web:** shadcn/ui supports Tailwind **v4** in stable — no risk there.
- **Native:** Tailwind v4 is supported only by **NativeWind v5**, which is a
  **pre-release** ("not recommended for production; API/tooling still
  evolving"). `react-native-reusables`' NativeWind-v5 / Tailwind-v4 path is
  expected post-Expo-54 and is similarly new. We accept the pre-release tooling
  on the Expo side to get Tailwind-v4 parity. This is the single riskiest item
  (see Verification Gate).
- Sources:
  - <https://www.nativewind.dev/v5/core-concepts/tailwindcss>
  - <https://www.nativewind.dev/v5/guides/migrate-from-v4>
  - <https://github.com/founded-labs/react-native-reusables/discussions/457>

## Nx & dependency wiring

- Add `@nx/expo` and `@nx/react` to root `devDependencies`.
- Register `@nx/expo/plugin` in `nx.json` with `include: ["src/apps/mobile"]`
  (scoped so it does not scan the Next.js apps). The existing `@nx/next` glob
  already ignores non-Next projects (it only infers targets where it finds Next
  config), so no change needed there.
- The existing `web` release group (`src/apps/*` + `src/packages/*`,
  independent) already covers all five new projects — **no release-config
  changes**.
- App/framework dependencies (`next`, `react`, `react-dom`, `expo`,
  `react-native`, `nativewind`, RNR, Radix, `clsx`/`tailwind-merge`) are added
  by the generators / `shadcn` / RNR CLIs at the project level.

## Dev container changes (in PR for this branch)

**Light (default, in-image):**
- Forward Metro / Expo ports: `8081` (Metro + web), `19000`, `19006`.
- Add the **Expo Tools** VS Code extension (`expo.vscode-expo-tools`).
- Expo and EAS CLIs run on demand via `bunx expo` / `bunx eas-cli` — no global
  installs. Dev loop: `expo start --web` (browser, port-forwarded) or Expo Go on
  a device via `expo start --tunnel`. Native builds via **EAS cloud**.

**Heavy (opt-in, documented only — NOT installed):**
- A README section showing how to add the Android SDK + JDK dev-container
  features and `watchman` for local Android builds/emulation (notes the KVM
  requirement and that iOS cannot build on Linux).

> Note: the **base `.devcontainer/` is already merged to `main`** (`README.md`,
> `devcontainer.json`, `postCreate.sh`), so this branch edits those files
> directly. The open devcontainer **PR #2** carries *unmerged* credential-
> persistence edits to the same files, but in **different regions**
> (PR #2: `features`/`mounts` in `devcontainer.json` + git/gh/signing blocks in
> `postCreate.sh`; this branch: `forwardPorts`/`portsAttributes`/`extensions`
> arrays + an Expo README section). Conflict risk is low and trivially
> resolvable whichever merges first.

## Conventions

- Run the Nx generators, then **strip generator-added ESLint and Jest** so the
  repo's **Biome + tsc** gates remain the only quality tooling. `lint` targets
  resolve to Biome; `typecheck` to `tsc --noEmit`.
- Let the generators create the root `tsconfig.base.json`; apps/libs extend it.
- Path aliases: `@teck/ui`, `@teck/ui-native`, `@teck/tailwind-config` resolve
  to package `src/` per existing `@teck/*` conventions.

## Proof of the pipeline (acceptance demo)

Seed exactly one component, `Button`, on each platform:
- shadcn `Button` in `@teck/ui`, rendered by `@teck/web`.
- RNR `Button` in `@teck/ui-native`, rendered by `@teck/mobile`.

Both read their variants/colors from `@teck/tailwind-config`. Changing the
primary token there visibly updates the Button on **both** web and native. That
demonstrates "consistent style + components across the stack" without building a
full library.

## Risks & verification gate

Risks:
1. **Bun × Expo × Metro** — Bun has historically been rough with Expo/Metro.
2. **React-version hoisting** — Next 16 (React 19) vs Expo's pinned
   React/React-Native in one Bun workspace can conflict on hoist.
3. **NativeWind v5 pre-release** — newest, least stable piece.

**Verification gate (must pass live before opening the PR):**
- Dev container builds; `bun install` resolves the workspace.
- `nx run-many -t build lint typecheck` (or `nx affected`) is **green** across
  all five projects.
- `@teck/web` dev server renders the shadcn Button with Tailwind v4.
- `@teck/mobile` (`expo start --web`) renders the RNR Button with NativeWind v5.
- A primary-token change in `@teck/tailwind-config` is reflected on both.

**Fallbacks (surface to the user, do not switch silently):** if Bun conflicts
prove intractable — align React versions across web/native, or scope the Expo
app to npm; if NativeWind v5 cannot build under Expo + Metro in the container —
fall back to NativeWind v4 / Tailwind v3 on native with shared token values
(web stays Tailwind v4), per the earlier "share tokens" decision.

## Documentation updates

- `src/apps/AGENTS.md` — add the Expo/mobile app category (currently web-only).
- `src/packages/AGENTS.md` — mark `ui-native` and `tailwind-config` as real;
  note the web (`@teck/ui`/shadcn) vs native (`@teck/ui-native`/RNR) split and
  the shared-token mechanism.

## Scope boundaries (YAGNI — explicitly deferred)

- Storybook, `@teck/api-client`, `docs`, `web-dashboard`.
- Full shadcn/RNR component sets (only `Button` is seeded).
- Any web↔native code sharing beyond design tokens (`react-native-web` etc.).
- `@teck/tsconfig` as a standalone package (root `tsconfig.base.json` suffices).

## Done criteria

- Five projects scaffolded and wired to Biome + tsc; Nx plugins/deps added.
- Dev container Expo tooling (light) added; heavy opt-in documented.
- Verification gate passes live in the container.
- AGENTS.md docs updated.
- PR opened off `main`.
