# Plan — Repo-wide analyzer enforcement + `order` compile-fix

**Date:** 2026-06-25
**Branch:** `worktree-catalog-service` (extends the open catalog PR)
**Driver:** Reviewer feedback on PR #1 — analyzers are globally silenced (`dotnet_analyzer_diagnostic.severity = none`, `AnalysisMode=None`, `EnforceCodeStyleInBuild=false`); the user wants a curated rule set **enforced as build errors** and all violations **fixed**, repo-wide. Plus: the `order` reference service must be made to compile and then cleaned.

## Decisions (locked with the user)

1. **Ruleset:** curated, convention-aligned (not kitchen-sink). Enforce the sensible rules; disable the ones that contradict the repo's chosen modern style.
2. **Scope:** whole repo, including fixing `order`'s real compile errors first.
3. **Namespaces:** keep **file-scoped** namespaces. (`SA1200` "usings inside namespace" is therefore disabled; `IDE0161` enforces file-scoped.)

## Unit of Work (the other review point) — no change

The DbContext-as-unit-of-work pattern is already mandated by `CLAUDE.md:51` and `src/services/AGENTS.md:90-112` ("Never create a separate IUnitOfWork abstraction"). Catalog write handlers already use it (one `SaveChangesAsync()` per handler). No code or doc change — this was a documented, intentional design, not an omission.

## Current build reality (measured 2026-06-25)

- Building **just `Catalog.Application`** with analyzers on → ~1,400 diagnostics across 50+ rules from 5 suites. Repo-wide will be several thousand, the large majority auto-fixable.
- **Only `Order.Application` + `Order.Host` fail to compile** (40 errors each). Everything else — all `SharedKernel.*`, `Order.Domain`, all `Catalog.*`, tests — compiles today.
- Root cause of the `order` break: root namespace `Order` collides with the aggregate type `Order` (`Order.Domain.Entities.Order`) → `CS0118` everywhere `Order` is used unqualified, which cascades into Mapperly failures (`RMG013/RMG006/CS8795/CS0759`).

---

## Phase 0 — Make `order` compile

**0.1 Resolve the `Order` namespace/type collision.** Rename the service root namespace `Order.*` → `Orders.*` (matches a plural convention and removes the clash; the aggregate stays `Order`). Set `<RootNamespace>` per project and update `namespace` declarations + `using` lines across `Order.Domain`, `Order.Application`, `Order.Host`, and order tests. (Assembly/project names stay `Order.*`; only the C# namespace root changes.)
  - Alternative considered & rejected: per-file `global::Order.Domain.Entities.Order` aliases — ugly, viral, and leaves the latent trap for the next file.

**0.2 Fix `OrderMapper` `Status.Name` (`RMG006`).** Inspect `OrderStatus`; map `OrderDto.Status` from the correct member (smart-enum `.Name` or `.ToString()`). Most of `RMG013/CS8795/CS0759` should evaporate once 0.1 lands (they are downstream of the `CS0118` cascade); verify and clean up any residue.

**0.3** `Order.Domain` + `Order.Application` + `Order.Host` build green (analyzers still off). Order tests (if any) compile & pass.

**Gate:** `dotnet build Teck.Platform.slnx` succeeds with analyzers still silenced.

---

## Phase 1 — Author the curated ruleset (policy)

Replace the blanket `dotnet_analyzer_diagnostic.severity = none` with a curated `.editorconfig` rule set, add a `stylecop.json`, and flip the global switches.

**1.1 `src/Directory.Build.props`:** `AnalysisMode=Recommended` (from `None`), `AnalysisLevel=latest`, `EnforceCodeStyleInBuild=true`. `TreatWarningsAsErrors` stays `true` (already), so enabled rules fail the build.

**1.2 Root `.editorconfig`:** remove the master `severity = none`; set a curated baseline (see table). Enforced rules → `warning` (which `TreatWarningsAsErrors` promotes to error); disabled → `none`.

**1.3 `stylecop.json`** at repo root: `documentExposedElements: true`, `documentInternalElements: false`, `documentPrivateElements: false` — so `SA1600` ("elements should be documented") applies to **public API only**, not every private member.

### Curated rule decisions

**ENFORCE (mostly `dotnet format`-auto-fixable):**
- Using ordering: `SA1208`, `SA1210`, `SA1211` (System-first, alphabetical) — *the reviewer's explicit ask*.
- Layout/braces/blank-lines: `SA1503`, `SA1505`-`SA1508`, `SA1516`, `SA1518`-`SA1520`, `SA1028`, `SA1137`; `IDE0011` (add braces).
- Member ordering: `SA1201`, `SA1202`, `SA1203`, `SA1204`.
- File hygiene: `SA1649` (file name = type), `SA1402` (one type per file), `SA1413` (trailing comma).
- Documentation (public API only via stylecop.json): `SA1600`, `SA1611`, `SA1614`-`SA1616`, `SA1618`, `SA1622` — *the reviewer's explicit ask ("elements should be documented")*.
- Namespaces: `csharp_style_namespace_declarations = file_scoped:warning` (`IDE0161`), keeping the repo's file-scoped style.
- Curated picks from Sonar (`S*`), Meziantou (`MA*`), Roslynator (`RCS*`) — bug-smell + auto-fixable formatting (e.g. `MA0048`, `RCS1142`, `S2325`). Final per-rule trims happen against the real violation set in Phase 3.

**DISABLE (contradict the chosen conventions / architectural / too noisy):**
- `SA1101` (`this.` prefix — 336 hits, contradicts modern style).
- `SA1200` (usings inside namespace — incompatible with file-scoped).
- `SA1633` + `SA1636` (copyright file headers — no such convention).
- `SA1309` (no `_` field prefix — keep the repo's `_camelCase`; **verify** prevailing convention during exec).
- **CSharpGuidelines `AV*` suite** — architectural/opinionated (method length, param counts, bool params); would force signature redesigns, not styling. Drop `CSharpGuidelinesAnalyzer` from the analyzer set (track re-introduction as a future tightening).
- `IDE0060` (unused parameter — Wolverine handler signatures legitimately carry fixed params like `CancellationToken`/`DbContext`).
- `IDE0008` (var vs explicit) / `IDE0022` (expression-bodied) — leave as preference, not enforced.

**Gate:** ruleset compiles/loads; a trial build of one clean project (e.g. `Catalog.Domain`) reports only the intended rules.

---

## Phase 2 — Mechanical auto-fix pass

Run `dotnet format Teck.Platform.slnx --severity info` (whitespace, using-ordering, braces, trailing commas, file-scoped namespaces, member ordering where the fixer supports it) to clear the bulk automatically. Commit per concern (`style: auto-fix using ordering & layout`) for a reviewable diff.

**Gate:** auto-fixable rule counts drop to ~0; remaining violations are the manual ones (mostly `SA1600` docs + a few semantic rules).

---

## Phase 3 — Manual cleanup, per project

For each compiling project (order × 3, shared × 4, catalog × 3, tests), with analyzers enforced as errors:
- Add XML doc comments to public APIs (`SA1600` + content rules).
- Resolve residual Sonar/Meziantou/Roslynator findings, or — if a rule proves more noise than value — disable it in `.editorconfig` **with a one-line rationale** (no silent blanket suppression; that's the very thing we're removing).
- Keep `TreatWarningsAsErrors=true`; the project is done when it builds clean.

Order suggestion (low-risk → high): `SharedKernel.*` → `Order.Domain` → `Catalog.Domain` → `Catalog.Application` → `Order.Application` → Hosts → tests.

**Gate per project:** builds with 0 warnings / 0 errors under the enforced ruleset.

---

## Phase 4 — Verify & enforce

- `dotnet build Teck.Platform.slnx` → 0 warnings / 0 errors (analyzers enforced).
- `nx affected -t build test lint` / full `dotnet test` → all green (catalog 63 + domain 28 + order tests).
- Confirm the master `severity = none` and `AnalysisMode=None` are gone; the only suppressions left are the curated, individually-justified `none` entries.
- Update `src/services/AGENTS.md` (and root `CLAUDE.md` analyzer note) to document the enforced ruleset as the platform standard, so future services inherit it.

---

## Risks / watch-items

- **`order` namespace rename (0.1)** is the riskiest step — wide but mechanical; do it as its own commit and build-gate before touching analyzers.
- **`SA1600` on public API** across the whole repo is the bulk of manual effort; the stylecop.json public-only scope keeps it bounded.
- **PR size:** this is large. Land on the existing branch (PR #1 is open/unmerged and this also fixes catalog's own violations), committed in coherent phases, OR split Phase 0 (`order` fix) into its own commit series for reviewability. Default: same branch, phase-by-phase commits.
- Rule tuning is iterative — the Phase 1 table is the policy; exact per-rule severities finalize against the measured violation set in Phase 3, each documented.

## Execution

Subagent-driven (matching the catalog Plan 1/2 workflow): per-phase implementer + review; durable ledger at `.superpowers/sdd/progress.md`; final whole-branch review before handing back at the PR.
