# Frontend Bootstrap + Expo Tooling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bootstrap the repo's first frontend projects (`@teck/web`, `@teck/mobile`, `@teck/ui`, `@teck/ui-native`, `@teck/tailwind-config`) with a shared design system — shadcn/Tailwind-v4 on web, react-native-reusables/NativeWind-v5 on native — plus light Expo dev-container tooling.

**Architecture:** A single design-token package (`@teck/tailwind-config`, CSS-first Tailwind v4 variables) is the source of truth. The web app consumes shadcn components from `@teck/ui` (Tailwind v4); the Expo app consumes react-native-reusables components from `@teck/ui-native` (NativeWind v5). A `Button` seeded on both platforms proves a token change propagates to both. Nx orchestrates; Bun installs; Biome + tsc are the only quality gates.

**Tech Stack:** Nx 23, Bun 1.2.0, Next.js 16 (React 19), Tailwind v4, shadcn/ui, Expo (SDK 54+), React Native, NativeWind v5 (pre-release), react-native-reusables, Biome.

## Global Constraints

- **Package manager: Bun `1.2.0` only.** Never invoke `npm`/`yarn`/`pnpm` to install unless a documented fallback is explicitly invoked. Use `bunx` for one-off CLIs.
- **Nx `23.0.0`.** Pin `@nx/expo` and `@nx/react` to `23.0.0` to match the locked `nx` version — do not use `latest` (plugin/runtime mismatch risk).
- **Quality gates: Biome + tsc only.** After every generator runs, delete any generated ESLint config, Jest/Vitest config, and `test`-runner wiring so `nx lint` = Biome and `nx typecheck` = `tsc --noEmit`. No new test runners.
- **Web:** Next.js `^16`, React `^19`, Tailwind `^4` (via `@tailwindcss/postcss`). shadcn/ui conventions (`cn()` = `clsx` + `tailwind-merge`, `cva`, `@radix-ui/react-slot`).
- **Native:** Expo SDK `>=54`, NativeWind `5` (pre-release, `nativewind@next`), Tailwind `^4`, react-native-reusables conventions (`@rn-primitives/*`).
- **Paths / names:** `src/apps/web` = `@teck/web`; `src/apps/mobile` = `@teck/mobile`; `src/packages/ui` = `@teck/ui`; `src/packages/ui-native` = `@teck/ui-native`; `src/packages/tailwind-config` = `@teck/tailwind-config`.
- **Nx wiring:** register `@nx/expo/plugin` with `include: ["src/apps/mobile"]`. The existing `web` release group (`src/apps/*` + `src/packages/*`, independent) already covers all five — do not edit release config.
- **Proof component:** exactly one component, `Button`, seeded on both platforms, both reading `@teck/tailwind-config`. No other components.
- **Risk protocol:** the three risky integrations (Bun×Expo×Metro, React-version hoisting, NativeWind v5 pre-release) have a **live verification gate** (Task 9). If a step cannot be made green, **stop and surface the fallback to the user** (align React versions / npm-scope the Expo app / drop native to NativeWind v4 + Tailwind v3 with shared tokens) — never silently switch.
- **Branch:** all work on `worktree-frontend-bootstrap` (off `main`). Frequent commits, one per task minimum.

---

### Task 1: Nx plugins, workspace deps, plugin registration

**Files:**
- Modify: `package.json` (root `devDependencies`)
- Modify: `nx.json` (`plugins` array)

**Interfaces:**
- Produces: `@nx/expo` and `@nx/react` generators available to all later tasks; `@nx/expo/plugin` scoped to `src/apps/mobile`.

- [ ] **Step 1: Add the two Nx plugins to root `devDependencies`**

In `package.json`, add to `devDependencies` (keep alphabetical with the existing `@nx/*` entries), pinned to the locked Nx version:

```json
"@nx/expo": "23.0.0",
"@nx/react": "23.0.0",
```

- [ ] **Step 2: Install**

Run: `bun install`
Expected: completes; `bun.lock` updates; `@nx/expo` and `@nx/react` resolve at `23.0.0`.

- [ ] **Step 3: Register the Expo plugin in `nx.json`**

In `nx.json`, append to the `plugins` array (after the `@nx/storybook` entry):

```json
{
  "plugin": "@nx/expo/plugin",
  "include": ["src/apps/mobile"],
  "options": {
    "startTargetName": "start",
    "serveTargetName": "serve",
    "buildTargetName": "build",
    "prebuildTargetName": "prebuild",
    "installTargetName": "install",
    "exportTargetName": "export"
  }
}
```

- [ ] **Step 4: Verify Nx loads the plugins**

Run: `bunx nx report`
Expected: output lists `@nx/expo` and `@nx/react` at `23.0.0`, no plugin-load errors.

- [ ] **Step 5: Commit**

```bash
git add package.json bun.lock nx.json
git commit -m "feat(frontend): add @nx/expo and @nx/react plugins"
```

---

### Task 2: `@teck/tailwind-config` — shared design tokens

**Files:**
- Create: `src/packages/tailwind-config/package.json`
- Create: `src/packages/tailwind-config/project.json`
- Create: `src/packages/tailwind-config/tokens.css`
- Create: `src/packages/tailwind-config/tsconfig.json`
- Modify: `tsconfig.base.json` (path alias) — create if a generator hasn't yet

**Interfaces:**
- Produces: importable stylesheet `@teck/tailwind-config/tokens.css` exposing CSS variables `--background --foreground --primary --primary-foreground --border --radius` (light + `.dark`). Consumed by web `globals.css` and native `global.css`.

- [ ] **Step 1: Create the package manifest**

`src/packages/tailwind-config/package.json`:

```json
{
  "name": "@teck/tailwind-config",
  "version": "0.0.1",
  "private": true,
  "exports": {
    "./tokens.css": "./tokens.css"
  }
}
```

- [ ] **Step 2: Create the token stylesheet (the single source of truth)**

`src/packages/tailwind-config/tokens.css` — framework-agnostic CSS variables (consumed by both Tailwind v4 and NativeWind v5):

```css
/* Shared design tokens — the single source of truth for web + native.
   Values are HSL channels so both Tailwind v4 (@theme) and NativeWind can
   wrap them in hsl(). Change a value here and BOTH platforms update. */
:root {
  --background: 0 0% 100%;
  --foreground: 222 47% 11%;
  --primary: 221 83% 53%;
  --primary-foreground: 210 40% 98%;
  --border: 214 32% 91%;
  --radius: 0.5rem;
}

.dark {
  --background: 222 47% 11%;
  --foreground: 210 40% 98%;
  --primary: 217 91% 60%;
  --primary-foreground: 222 47% 11%;
  --border: 217 33% 17%;
}
```

- [ ] **Step 3: Create `project.json` (no build target; it's a static asset lib)**

`src/packages/tailwind-config/project.json`:

```json
{
  "name": "tailwind-config",
  "$schema": "../../../node_modules/nx/schemas/project-schema.json",
  "projectType": "library",
  "sourceRoot": "src/packages/tailwind-config",
  "tags": ["scope:shared", "type:config"],
  "targets": {}
}
```

- [ ] **Step 4: Create a minimal tsconfig and register the path alias**

`src/packages/tailwind-config/tsconfig.json`:

```json
{ "extends": "../../../tsconfig.base.json", "files": [], "include": [] }
```

If `tsconfig.base.json` does not yet exist at the repo root, create it:

```json
{
  "compilerOptions": {
    "composite": true,
    "declaration": true,
    "strict": true,
    "moduleResolution": "bundler",
    "module": "esnext",
    "target": "es2022",
    "lib": ["es2022", "dom", "dom.iterable"],
    "jsx": "preserve",
    "esModuleInterop": true,
    "skipLibCheck": true,
    "baseUrl": ".",
    "paths": {
      "@teck/tailwind-config/*": ["src/packages/tailwind-config/*"]
    }
  },
  "exclude": ["node_modules", "tmp"]
}
```

If it already exists, add only the `@teck/tailwind-config/*` entry to `compilerOptions.paths`.

- [ ] **Step 5: Verify the package is picked up by Nx**

Run: `bunx nx show project tailwind-config`
Expected: prints project info, no error.

- [ ] **Step 6: Commit**

```bash
git add src/packages/tailwind-config tsconfig.base.json
git commit -m "feat(ui): add @teck/tailwind-config shared design tokens"
```

---

### Task 3: `@teck/ui` — web shadcn library with `Button`

**Files:**
- Create (generator): `src/packages/ui/**`
- Create: `src/packages/ui/src/lib/utils.ts` (`cn`)
- Create: `src/packages/ui/src/components/button.tsx`
- Create: `src/packages/ui/components.json`
- Create: `src/packages/ui/src/index.ts`
- Modify: `tsconfig.base.json` (alias `@teck/ui`)
- Delete: any generated ESLint/Jest files

**Interfaces:**
- Consumes: nothing (leaf).
- Produces: `Button` (React, DOM) and `buttonVariants` exported from `@teck/ui`; `cn` from `@teck/ui/src/lib/utils`. Consumed by `@teck/web`.

- [ ] **Step 1: Generate the React library**

Run:
```bash
bunx nx g @nx/react:library ui \
  --directory=src/packages/ui \
  --bundler=none --unitTestRunner=none --linter=none \
  --importPath=@teck/ui --no-interactive
```
Expected: creates `src/packages/ui` with `project.json`, `tsconfig.*`, `src/`.

- [ ] **Step 2: Remove non-Biome tooling the generator may have added**

Delete if present: `src/packages/ui/.eslintrc.json`, `src/packages/ui/jest.config.ts`, `src/packages/ui/vite.config.ts`, and any `test`/`lint` (eslint) targets in `src/packages/ui/project.json`. Keep only `typecheck` (and a `lint` target only if it invokes Biome).

Run: `git status` — confirm no `.eslintrc*`/`jest*` remain staged.

- [ ] **Step 3: Add shadcn runtime deps to the workspace**

Run:
```bash
bun add class-variance-authority clsx tailwind-merge @radix-ui/react-slot lucide-react react react-dom
```
Expected: resolves React `^19`.

- [ ] **Step 4: Add the `cn` utility**

`src/packages/ui/src/lib/utils.ts`:

```ts
import { type ClassValue, clsx } from "clsx";
import { twMerge } from "tailwind-merge";

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}
```

- [ ] **Step 5: Add the shadcn `Button` (canonical source)**

`src/packages/ui/src/components/button.tsx`:

```tsx
import { Slot } from "@radix-ui/react-slot";
import { type VariantProps, cva } from "class-variance-authority";
import * as React from "react";
import { cn } from "../lib/utils";

const buttonVariants = cva(
  "inline-flex items-center justify-center gap-2 whitespace-nowrap rounded-md text-sm font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:pointer-events-none disabled:opacity-50",
  {
    variants: {
      variant: {
        default: "bg-primary text-primary-foreground hover:bg-primary/90",
        outline: "border border-border bg-background hover:bg-primary/10",
      },
      size: {
        default: "h-10 px-4 py-2",
        sm: "h-9 px-3",
        lg: "h-11 px-8",
      },
    },
    defaultVariants: { variant: "default", size: "default" },
  },
);

export interface ButtonProps
  extends React.ButtonHTMLAttributes<HTMLButtonElement>,
    VariantProps<typeof buttonVariants> {
  asChild?: boolean;
}

const Button = React.forwardRef<HTMLButtonElement, ButtonProps>(
  ({ className, variant, size, asChild = false, ...props }, ref) => {
    const Comp = asChild ? Slot : "button";
    return (
      <Comp
        className={cn(buttonVariants({ variant, size, className }))}
        ref={ref}
        {...props}
      />
    );
  },
);
Button.displayName = "Button";

export { Button, buttonVariants };
```

- [ ] **Step 6: Barrel export + `components.json`**

`src/packages/ui/src/index.ts`:

```ts
export { Button, buttonVariants } from "./components/button";
export type { ButtonProps } from "./components/button";
export { cn } from "./lib/utils";
```

`src/packages/ui/components.json` (so future `bunx shadcn@latest add <x>` lands here):

```json
{
  "$schema": "https://ui.shadcn.com/schema.json",
  "style": "new-york",
  "tailwind": { "config": "", "css": "../../apps/web/src/app/globals.css", "baseColor": "slate", "cssVariables": true },
  "rsc": true,
  "tsx": true,
  "aliases": { "components": "@teck/ui/src/components", "utils": "@teck/ui/src/lib/utils" }
}
```

- [ ] **Step 7: Register the `@teck/ui` path alias**

In `tsconfig.base.json` `compilerOptions.paths`, add:

```json
"@teck/ui": ["src/packages/ui/src/index.ts"],
"@teck/ui/*": ["src/packages/ui/src/*"]
```

- [ ] **Step 8: Verify typecheck**

Run: `bunx nx typecheck ui`
Expected: PASS (no type errors). If `typecheck` target is absent, run `bunx tsc --noEmit -p src/packages/ui/tsconfig.lib.json`.

- [ ] **Step 9: Commit**

```bash
git add src/packages/ui tsconfig.base.json package.json bun.lock
git commit -m "feat(ui): add @teck/ui web shadcn library with Button"
```

---

### Task 4: `@teck/web` — Next.js 16 + Tailwind v4 rendering the shadcn Button

**Files:**
- Create (generator): `src/apps/web/**`
- Create: `src/apps/web/postcss.config.mjs`
- Modify: `src/apps/web/src/app/globals.css`
- Modify: `src/apps/web/src/app/page.tsx`
- Modify: `tsconfig.base.json` (alias `@teck/web` if generator didn't)
- Delete: generated ESLint/Jest files

**Interfaces:**
- Consumes: `Button` from `@teck/ui`; tokens from `@teck/tailwind-config/tokens.css`.
- Produces: a running Next.js app whose home page renders `<Button>`.

- [ ] **Step 1: Generate the Next.js app**

Run:
```bash
bunx nx g @nx/next:application web \
  --directory=src/apps/web --appDir=true --src=true \
  --style=css --unitTestRunner=none --linter=none --e2eTestRunner=none \
  --no-interactive
```
Expected: creates `src/apps/web` (App Router under `src/app`).

- [ ] **Step 2: Pin Next 16 / React 19 and add Tailwind v4**

Run:
```bash
bun add next@^16 react@^19 react-dom@^19
bun add -d tailwindcss@^4 @tailwindcss/postcss@^4
```
Expected: `next` resolves `^16`. If `@nx/next@23` errors against Next 16 during build (Step 7), STOP and surface the fallback (pin `next@^15` for now, note Next-16 follow-up) per the risk protocol.

- [ ] **Step 3: PostCSS config for Tailwind v4**

`src/apps/web/postcss.config.mjs`:

```js
export default {
  plugins: {
    "@tailwindcss/postcss": {},
  },
};
```

- [ ] **Step 4: Wire Tailwind v4 + shared tokens in `globals.css`**

Replace `src/apps/web/src/app/globals.css` with:

```css
@import "tailwindcss";
@import "@teck/tailwind-config/tokens.css";

/* Map shared HSL token channels to Tailwind v4 theme colors. */
@theme inline {
  --color-background: hsl(var(--background));
  --color-foreground: hsl(var(--foreground));
  --color-primary: hsl(var(--primary));
  --color-primary-foreground: hsl(var(--primary-foreground));
  --color-border: hsl(var(--border));
  --color-ring: hsl(var(--primary));
  --radius-md: var(--radius);
}

body {
  background-color: hsl(var(--background));
  color: hsl(var(--foreground));
}
```

- [ ] **Step 5: Render the Button on the home page**

Replace `src/apps/web/src/app/page.tsx` with:

```tsx
import { Button } from "@teck/ui";

export default function Home() {
  return (
    <main style={{ display: "grid", placeItems: "center", minHeight: "100dvh" }}>
      <div style={{ display: "flex", gap: 12 }}>
        <Button>Primary</Button>
        <Button variant="outline">Outline</Button>
      </div>
    </main>
  );
}
```

Ensure `globals.css` is imported by `src/apps/web/src/app/layout.tsx` (the generator adds this import; confirm it is present).

- [ ] **Step 6: Strip non-Biome tooling**

Delete if present: `src/apps/web/.eslintrc.json`, `src/apps/web/jest.config.ts`, eslint `lint` target in `project.json`. Confirm `tsconfig.base.json` has `@teck/web` only if other tasks need it (apps are not imported — alias optional).

- [ ] **Step 7: Verify build + typecheck**

Run: `bunx nx build web`
Expected: production build succeeds; Tailwind processes without error.
Run: `bunx nx typecheck web`
Expected: PASS.

- [ ] **Step 8: Live render check (verification-gate component)**

Run: `bunx nx dev web` (port 3000, forwarded) — load the page.
Expected: two buttons; "Primary" filled with the primary token color, "Outline" bordered. Stop the dev server after confirming.

- [ ] **Step 9: Commit**

```bash
git add src/apps/web tsconfig.base.json package.json bun.lock
git commit -m "feat(web): add @teck/web Next.js 16 app with Tailwind v4 + shadcn Button"
```

---

### Task 5: `@teck/mobile` — Expo app with NativeWind v5

**Files:**
- Create (generator): `src/apps/mobile/**`
- Create: `src/apps/mobile/global.css`
- Create: `src/apps/mobile/metro.config.js` (or modify generated)
- Create: `src/apps/mobile/nativewind-env.d.ts`
- Modify: `src/apps/mobile/babel.config.js`
- Modify: `src/apps/mobile/tailwind.config.js`
- Modify: `src/apps/mobile/app/` entry to import `global.css`
- Delete: generated ESLint/Jest files

**Interfaces:**
- Consumes: tokens from `@teck/tailwind-config/tokens.css`.
- Produces: a running Expo app with NativeWind v5 classes working; consumed by Task 6.

- [ ] **Step 1: Generate the Expo app**

Run:
```bash
bunx nx g @nx/expo:application mobile \
  --directory=src/apps/mobile \
  --unitTestRunner=none --linter=none --e2eTestRunner=none \
  --no-interactive
```
Expected: creates `src/apps/mobile` with Expo config.

- [ ] **Step 2: Align Expo SDK to >=54**

Run: `cd src/apps/mobile && bunx expo install expo@^54 && cd -`
Then run `bunx expo install --fix` from `src/apps/mobile` to align native deps.
Expected: Expo SDK 54+. If the Nx generator scaffolds an Expo version that cannot upgrade cleanly, STOP and surface the fallback (use the latest Expo the generator supports + NativeWind v4/Tailwind v3 with shared tokens).

- [ ] **Step 3: Install NativeWind v5 (pre-release) + peers**

Run (from `src/apps/mobile`):
```bash
bunx expo install nativewind@next react-native-reanimated react-native-safe-area-context
bun add -d tailwindcss@^4
```
Expected: `nativewind@5.x` (next tag) installed. NativeWind v5 setup follows its current docs — apply Steps 4-7 exactly, and if v5 cannot build under Metro (Step 9), STOP and surface the NativeWind-v4 fallback.

- [ ] **Step 4: `tailwind.config.js` (NativeWind preset, content globs, shared tokens)**

`src/apps/mobile/tailwind.config.js`:

```js
/** @type {import('tailwindcss').Config} */
module.exports = {
  content: ["./app/**/*.{js,jsx,ts,tsx}", "../../packages/ui-native/src/**/*.{js,jsx,ts,tsx}"],
  presets: [require("nativewind/preset")],
  theme: {
    extend: {
      colors: {
        background: "hsl(var(--background))",
        foreground: "hsl(var(--foreground))",
        primary: "hsl(var(--primary))",
        "primary-foreground": "hsl(var(--primary-foreground))",
        border: "hsl(var(--border))",
      },
      borderRadius: { md: "var(--radius)" },
    },
  },
};
```

- [ ] **Step 5: `global.css` importing Tailwind + shared tokens**

`src/apps/mobile/global.css`:

```css
@import "tailwindcss";
@import "@teck/tailwind-config/tokens.css";
```

- [ ] **Step 6: Metro + Babel for NativeWind v5**

`src/apps/mobile/metro.config.js`:

```js
const { getDefaultConfig } = require("expo/metro-config");
const { withNativeWind } = require("nativewind/metro");

const config = getDefaultConfig(__dirname);
module.exports = withNativeWind(config, { input: "./global.css" });
```

Ensure `src/apps/mobile/babel.config.js` includes the NativeWind jsxImportSource preset:

```js
module.exports = (api) => {
  api.cache(true);
  return {
    presets: [
      ["babel-preset-expo", { jsxImportSource: "nativewind" }],
      "nativewind/babel",
    ],
  };
};
```

- [ ] **Step 7: NativeWind types**

`src/apps/mobile/nativewind-env.d.ts`:

```ts
/// <reference types="nativewind/types" />
```

- [ ] **Step 8: Import `global.css` + apply a token class at the entry**

In the app entry (`src/apps/mobile/app/_layout.tsx` for Expo Router, or `App.tsx`), add at the top:

```tsx
import "../global.css";
```
and set the root view `className="flex-1 bg-background"` to prove a token class renders.

- [ ] **Step 9: Verify typecheck + web bundle (verification-gate)**

Run: `bunx nx typecheck mobile`
Expected: PASS.
Run (from `src/apps/mobile`): `bunx expo start --web` — load it.
Expected: app renders; background uses the `--background` token. Stop the server after confirming. If Metro/NativeWind v5 fails to bundle, STOP and surface the fallback.

- [ ] **Step 10: Strip non-Biome tooling + commit**

Delete any generated `.eslintrc*`/`jest*` under `src/apps/mobile`. Then:

```bash
git add src/apps/mobile package.json bun.lock
git commit -m "feat(mobile): add @teck/mobile Expo app with NativeWind v5"
```

---

### Task 6: `@teck/ui-native` — react-native-reusables `Button` consumed by mobile

**Files:**
- Create (generator): `src/packages/ui-native/**`
- Create: `src/packages/ui-native/src/lib/utils.ts` (native `cn`)
- Create: `src/packages/ui-native/src/components/button.tsx` (RNR)
- Create: `src/packages/ui-native/src/index.ts`
- Modify: `tsconfig.base.json` (alias `@teck/ui-native`)
- Modify: `src/apps/mobile` entry to render the native `Button`

**Interfaces:**
- Consumes: NativeWind classes; tokens via `@teck/tailwind-config` (through the mobile app's Tailwind content glob, which already includes `ui-native`).
- Produces: native `Button` exported from `@teck/ui-native`, rendered by `@teck/mobile`.

- [ ] **Step 1: Generate the library**

Run:
```bash
bunx nx g @nx/react:library ui-native \
  --directory=src/packages/ui-native \
  --bundler=none --unitTestRunner=none --linter=none \
  --importPath=@teck/ui-native --no-interactive
```

- [ ] **Step 2: Strip non-Biome tooling** (delete generated `.eslintrc*`/`jest*`/`vite.config.ts`; remove eslint/test targets from `project.json`).

- [ ] **Step 3: Add RNR primitive dep**

Run: `bun add @rn-primitives/slot`
Expected: resolves.

- [ ] **Step 4: Native `cn` utility**

`src/packages/ui-native/src/lib/utils.ts`:

```ts
import { type ClassValue, clsx } from "clsx";
import { twMerge } from "tailwind-merge";

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}
```

- [ ] **Step 5: Add the react-native-reusables `Button` (canonical RN source)**

`src/packages/ui-native/src/components/button.tsx`:

```tsx
import * as Slot from "@rn-primitives/slot";
import { type VariantProps, cva } from "class-variance-authority";
import * as React from "react";
import { Pressable, Text } from "react-native";
import { cn } from "../lib/utils";

const buttonVariants = cva(
  "flex flex-row items-center justify-center rounded-md",
  {
    variants: {
      variant: {
        default: "bg-primary",
        outline: "border border-border bg-background",
      },
      size: { default: "h-10 px-4", sm: "h-9 px-3", lg: "h-11 px-8" },
    },
    defaultVariants: { variant: "default", size: "default" },
  },
);

const buttonTextVariants = cva("text-sm font-medium", {
  variants: {
    variant: {
      default: "text-primary-foreground",
      outline: "text-foreground",
    },
  },
  defaultVariants: { variant: "default" },
});

type ButtonProps = React.ComponentPropsWithoutRef<typeof Pressable> &
  VariantProps<typeof buttonVariants> & { asChild?: boolean };

const Button = React.forwardRef<React.ElementRef<typeof Pressable>, ButtonProps>(
  ({ className, variant, size, asChild = false, children, ...props }, ref) => {
    const Comp = asChild ? Slot.Pressable : Pressable;
    return (
      <Comp
        ref={ref}
        className={cn(buttonVariants({ variant, size, className }))}
        {...props}
      >
        {typeof children === "string" ? (
          <Text className={cn(buttonTextVariants({ variant }))}>{children}</Text>
        ) : (
          children
        )}
      </Comp>
    );
  },
);
Button.displayName = "Button";

export { Button, buttonVariants, buttonTextVariants };
```

- [ ] **Step 6: Barrel export**

`src/packages/ui-native/src/index.ts`:

```ts
export { Button, buttonVariants, buttonTextVariants } from "./components/button";
export { cn } from "./lib/utils";
```

- [ ] **Step 7: Register the alias**

In `tsconfig.base.json` `compilerOptions.paths` add:

```json
"@teck/ui-native": ["src/packages/ui-native/src/index.ts"],
"@teck/ui-native/*": ["src/packages/ui-native/src/*"]
```

- [ ] **Step 8: Render it in the mobile app**

In the mobile entry screen, import and render:

```tsx
import { Button } from "@teck/ui-native";
// ...inside the root view:
<Button onPress={() => {}}>Primary</Button>
<Button variant="outline" onPress={() => {}}>Outline</Button>
```

- [ ] **Step 9: Verify typecheck + render (verification-gate)**

Run: `bunx nx typecheck mobile` and `bunx nx typecheck ui-native`
Expected: PASS.
Run (from `src/apps/mobile`): `bunx expo start --web` — confirm both RNR buttons render, "Primary" using the primary token. Stop the server.

- [ ] **Step 10: Commit**

```bash
git add src/packages/ui-native src/apps/mobile tsconfig.base.json package.json bun.lock
git commit -m "feat(ui-native): add @teck/ui-native RNR Button rendered by mobile"
```

---

### Task 7: Dev container — light Expo tooling + documented heavy opt-in

**Files:**
- Modify: `.devcontainer/devcontainer.json`
- Modify: `.devcontainer/README.md`

**Interfaces:**
- Produces: forwarded Expo ports + Expo Tools extension; opt-in heavy docs.

- [ ] **Step 1: Forward Expo ports**

In `.devcontainer/devcontainer.json`, extend `forwardPorts` to include `8081, 19000, 19006`, and add to `portsAttributes`:

```json
"8081": { "label": "Metro / Expo web" },
"19000": { "label": "Expo Go (LAN)" },
"19006": { "label": "Expo web (legacy)" }
```

- [ ] **Step 2: Add the Expo Tools VS Code extension**

In `.devcontainer/devcontainer.json` `customizations.vscode.extensions`, add `"expo.vscode-expo-tools"`.

- [ ] **Step 3: Validate JSON**

Run: `python3 -c "import json; json.load(open('.devcontainer/devcontainer.json')); print('ok')"`
Expected: `ok`.

- [ ] **Step 4: Document the heavy opt-in in the README**

Append a section to `.devcontainer/README.md`:

```markdown
## Mobile (Expo) — light by default

Expo tooling is light: no Android SDK in the image. Develop via `bunx expo start --web` (port 8081, forwarded) or Expo Go on a device with `bunx expo start --tunnel`; native builds run in the cloud via `bunx eas-cli`. The **Expo Tools** VS Code extension is preinstalled.

### Opt-in: local Android builds (heavy)

Not installed by default (adds gigabytes; needs `/dev/kvm` for emulation; iOS cannot build on Linux). To enable, add to `.devcontainer/devcontainer.json` `features`:

```jsonc
"ghcr.io/devcontainers/features/java:1": { "version": "17" },
"ghcr.io/devcontainers/features/android-sdk:1": {}
```

and install `watchman`. Then `bunx expo run:android` builds locally.
```

- [ ] **Step 5: Commit**

```bash
git add .devcontainer/devcontainer.json .devcontainer/README.md
git commit -m "feat(devcontainer): light Expo tooling + documented heavy opt-in"
```

---

### Task 8: Documentation — update AGENTS.md trees

**Files:**
- Modify: `src/apps/AGENTS.md`
- Modify: `src/packages/AGENTS.md`

- [ ] **Step 1: Add the mobile app category to `src/apps/AGENTS.md`**

Under the Applications table, add a row and a short note that `mobile/` (`@teck/mobile`) is an **Expo + React Native** app (NativeWind v5 + react-native-reusables) — the one non-Next.js app — and that it shares design tokens with web via `@teck/tailwind-config` but does not share component code.

- [ ] **Step 2: Mark `ui-native` and `tailwind-config` real in `src/packages/AGENTS.md`**

Update the Packages table: `ui/` = web shadcn (Tailwind v4); add `ui-native/` = react-native-reusables (NativeWind v5); `tailwind-config/` = shared design tokens consumed by both. Note the web/native split and the shared-token mechanism.

- [ ] **Step 3: Commit**

```bash
git add src/apps/AGENTS.md src/packages/AGENTS.md
git commit -m "docs(frontend): document Expo app + ui-native/tailwind-config packages"
```

---

### Task 9: Verification gate + open PR

**Files:** none (verification + PR).

- [ ] **Step 1: Whole-workspace gate**

Run: `bunx nx run-many -t build lint typecheck`
Expected: GREEN for `web`, `mobile`, `ui`, `ui-native`, `tailwind-config`. Fix any failure in the owning task before proceeding.

- [ ] **Step 2: Shared-token propagation proof (the acceptance demo)**

Edit `src/packages/tailwind-config/tokens.css` — change `--primary` to a visibly different hue (e.g. `142 71% 45%`). Then:
- `bunx nx dev web` → the web Button color changes.
- `bunx expo start --web` (from `src/apps/mobile`) → the native Button color changes too.
Confirm BOTH change, then **revert the token edit** (`git checkout src/packages/tailwind-config/tokens.css`).

- [ ] **Step 3: If any risk gate failed**

If Bun×Expo, React hoisting, or NativeWind v5 could not be made green, STOP and surface the documented fallback to the user with the exact error — do not silently switch package managers or versions.

- [ ] **Step 4: Push and open the PR**

```bash
git push -u origin worktree-frontend-bootstrap
gh pr create --base main --title "feat(frontend): bootstrap web/mobile + shared shadcn/RNR design system" \
  --body "Bootstraps @teck/web (Next 16 + Tailwind v4 + shadcn), @teck/mobile (Expo + NativeWind v5 + react-native-reusables), @teck/ui, @teck/ui-native, and @teck/tailwind-config (shared tokens). Adds @nx/expo + light Expo dev-container tooling. Verified live: nx build/lint/typecheck green; a token change propagates to web and native Button. See docs/superpowers/specs/2026-06-26-frontend-bootstrap-design.md."
```

- [ ] **Step 5: Report** the PR URL, which risk gates passed live, and any fallback that had to be invoked.

---

## Self-Review

**Spec coverage:** every spec section maps to a task — tokens (T2), web shadcn (T3/T4), Expo+NativeWind v5 (T5), RNR ui-native (T6), Nx/deps (T1), devcontainer light+opt-in (T7), AGENTS.md (T8), verification gate + fallbacks + PR (T9). Deferred/YAGNI items are not scaffolded. ✓

**Placeholder scan:** concrete commands, file contents, and expected outputs throughout. The pre-release NativeWind v5 setup (T5 Steps 3-6) gives the known-good config but is explicitly gated by a live check with a named fallback — intentional given the documented risk, not a vague placeholder. ✓

**Type consistency:** `cn` signature identical in `@teck/ui` and `@teck/ui-native`; `Button`/`buttonVariants` names consistent; token variable names (`--primary` etc.) identical across `tokens.css`, web `globals.css`, and native `tailwind.config.js`; path aliases match the Global Constraints. ✓
