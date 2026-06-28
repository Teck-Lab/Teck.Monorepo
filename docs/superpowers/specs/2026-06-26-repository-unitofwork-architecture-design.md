# Repository + UnitOfWork architecture — design

**Date:** 2026-06-26
**Status:** Approved design, pending implementation plan
**Scope:** `order` and `catalog` services + `SharedKernel.Core` / `SharedKernel.Infrastructure`; architecture tests; `AGENTS.md`/`CLAUDE.md` docs.

## Problem & context

The persistence story drifted from the documented conventions, and the docs themselves no longer match reality. Ground truth (verified against the code, 2026-06-26):

- **`IUnitOfWork` / `UnitOfWork<TContext>` exist** in SharedKernel but are **never used** — command handlers call `db.SaveChangesAsync()` directly on a concrete `DbContext`.
- **A full pair of repository interfaces exists and is dormant:** `IGenericReadRepository<T,TId>` and `IGenericWriteRepository<T,TId>` (+ EF implementations). Handlers don't use them.
- **Reads** currently inject the third-party `Ardalis.IRepositoryBase<T>` directly; **writes** inject a concrete `DbContext`. The pattern is asymmetric.
- **Read/write context split is half-built:** `OrderReadDbContext` (NoTracking) exists but no handler uses it; `CatalogReadDbContext` does not exist.
- **Docs are wrong:** `CLAUDE.md` and `src/services/AGENTS.md` claim "the DbContext **is** the unit of work — no `IUnitOfWork` abstraction" and describe a generic-repository/read-write-context split that isn't actually wired up. They argue *against* the very pattern we now want.

The platform is early scaffolding (only `order` + `catalog` + SharedKernel are real), so the cost of standardizing now is low and the payoff — a single pattern every future service mirrors — is high.

## Goals

The owner explicitly wants all four of these (they drive the design):

1. **Consistency / clean layering** — Application handlers depend only on abstractions, never on a concrete `DbContext`. One symmetric pattern for reads *and* writes.
2. **Testability** — repositories + UoW are mockable in handler unit tests without EF or a database.
3. **Explicit transaction control** — coordinate multiple aggregates/repositories in one transaction via `Begin`/`Commit`/`Rollback`, beyond a single `SaveChanges`.
4. **Persistence-swappability** — keep EF Core behind the abstraction so handlers don't change if the implementation does.

Non-goal: rewriting the query DSL. The existing Ardalis-specification pattern stays — SharedKernel's repo interfaces already accept `ISpecification<T>`, so existing specs (`OrderByIdSpec`, `ProductByIdSpec`, …) work unchanged.

## Key decisions (locked)

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | Adopt SharedKernel's dormant `IGenericReadRepository<T,TId>`, `IGenericWriteRepository<T,TId>`, and `IUnitOfWork` everywhere. | Already built; first-party contracts in `SharedKernel.Core`; satisfies all four goals with minimal new code. |
| 2 | **`IUnitOfWork` owns `SaveChangesAsync` + transactions.** Remove `SaveChangesAsync` from `IGenericWriteRepository`. | Single, unambiguous commit point; the only clean home for multi-aggregate transactions. Repos *track*, UoW *commits*. |
| 3 | `IBaseEntity<TId>` additionally implements `IReadModel<TId>`. | The read repo constrains `T : IReadModel<TId>`; entities currently only implement `IBaseEntity<TId>`. `IReadModel` needs only `Id`, which `BaseEntity` already has → one-line interface-inheritance fix, zero per-entity edits, no strongly-typed-id issues (all ids are `Guid`). |
| 4 | Context shape = **shared abstract base + sibling read/write contexts.** | Model defined once in the abstract base; each leaf declares its own tracking intent; neither context derives from the other, so there's no "wrong direction" for a future agent to get confused about. Write context remains the migration/schema owner. |
| 5 | Fix the misspelled methods `ExcecutSoftDeleteAsync` / `ExcecutHardDeleteAsync` → `Execute…` while adopting the code. | We're blessing this as the standard; don't enshrine typos in the public contract. |
| 6 | Enforce with an ArchUnit test + rewrite the contradicting docs. | The pattern only sticks if the build fails on regression and the canonical `AGENTS.md` stops arguing the opposite. |

## Target architecture

### Layer contract
Application handlers inject **only** these `SharedKernel.Core` abstractions:

- `IGenericReadRepository<T,TId>` — queries (`AsNoTracking`, specifications, projections, pagination, id lookups).
- `IGenericWriteRepository<T,TId>` — mutations (`AddAsync`, `Update`, `Delete`, `DeleteRange`, execute-soft/hard-delete). **No `SaveChanges`.**
- `IUnitOfWork` — `SaveChangesAsync`, `BeginTransactionAsync`, `CommitTransactionAsync`, `RollbackTransactionAsync`.

No handler references a concrete `DbContext` or `Ardalis.IRepositoryBase<T>` again.

### DbContext shape (per service, e.g. Catalog)
```
BaseDbContext                       (SharedKernel: multitenancy, soft-delete, auditing, SaveChanges, interceptors)
  └─ CatalogDbContextBase  (abstract)   ← model + DbSets + ApplyConfigurationsFromAssembly (defined ONCE)
       ├─ CatalogDbContext            ← WRITE: tracked (default), owns EF migrations
       └─ CatalogReadDbContext        ← READ: ctor sets QueryTrackingBehavior.NoTracking
```
Mirror the same trio for `order` (refactor existing `OrderDbContext`/`OrderReadDbContext` into the base+sibling shape).

### DI wiring (per service)
- `GenericReadRepository<T, TId, {Service}ReadDbContext>` → `IGenericReadRepository<T,TId>`
- `GenericWriteRepository<T, TId, {Service}DbContext>` → `IGenericWriteRepository<T,TId>`
- `UnitOfWork<{Service}DbContext>` → `IUnitOfWork`

Read repo binds to the **read** context; write repo and UoW bind to the **write** context.

### Command handler shape (after)
```csharp
// load-mutate-save
var product = await writeRepo.FindByIdAsync(id, ct);   // see tracking note below
product.UpdateSellPrice(newPrice);
writeRepo.Update(product);                              // explicit; safe regardless of tracking
await unitOfWork.SaveChangesAsync(ct);

// create
await writeRepo.AddAsync(product, ct);
await unitOfWork.SaveChangesAsync(ct);
```

### Query handler shape (after)
```csharp
var dto = await readRepo.FirstOrDefaultAsync(new ProductByIdSpec(id), ct);  // existing spec, unchanged
```

## Changes by area

1. **`SharedKernel.Core`**
   - `IBaseEntity<TId> : IReadModel<TId>` (decision 3).
   - Remove `SaveChangesAsync` from `IGenericWriteRepository<T,TId>` (decision 2).
   - Rename `Excecut*` → `Execute*` on `IGenericWriteRepository` (decision 5).
2. **`SharedKernel.Infrastructure`**
   - Drop `SaveChangesAsync` from `GenericWriteRepository`; rename the `Excecut*` impls.
   - Confirm `UnitOfWork<TContext>` is the commit/transaction owner.
   - DI extension helpers to register the read repo / write repo / UoW with the correct context per service.
3. **`order` service**
   - Refactor into `OrderDbContextBase` (abstract, model) + `OrderDbContext` (write) + `OrderReadDbContext` (read, NoTracking) sibling shape.
   - Rewrite command handlers (`CreateOrderHandler`, …) to use `IGenericWriteRepository` + `IUnitOfWork`.
   - Switch query handlers from `Ardalis.IRepositoryBase<T>` to `IGenericReadRepository<T,TId>`.
4. **`catalog` service**
   - Create `CatalogDbContextBase` + `CatalogDbContext` (write) + **new** `CatalogReadDbContext` (read).
   - Rewrite command handlers (`CreateProductHandler`, `UpdateSellPriceHandler`, `CreateCategoryHandler`, …) and query handlers as above.
5. **Architecture tests (`tests/architecture/`)**
   - New ArchUnit rule: types in `*.Application` must not depend on any concrete `DbContext` nor on `Ardalis.IRepositoryBase` — only the three SharedKernel contracts. Fails the build on regression.
6. **Docs**
   - `src/services/AGENTS.md` (canonical): replace the "DbContext **is** the unit of work / no `IUnitOfWork`" and direct-DbContext-writes guidance with the new repository + UoW + read/write-context pattern, including this design's rationale so a future agent doesn't "simplify" it back.
   - `CLAUDE.md`: update the "Architecture rules that span multiple files" + "CQRS at the DbContext level" + "No per-entity repositories" bullets to match.
   - Per-service `AGENTS.md` (`order/`, `catalog/`) where they restate the persistence pattern.
   - Update the catalog design memory note.

## Edge cases & conventions to nail down in the plan

- **Tracking on load-for-update.** `GenericWriteRepository`'s inherited read methods default to `AsNoTracking` (`enableTracking = false`). Convention: command handlers that load-then-mutate must either pass `enableTracking: true` **or** call `writeRepo.Update(entity)` explicitly before `SaveChanges`. Spec/plan must pick one convention and document it; the handler example above uses explicit `Update()` (safe regardless of tracking).
- **Context placement / layering.** Today the write context lives in `*.Application/Database` and the read context in `*.Host/Database`. The plan must decide where `{Service}DbContextBase` lives and keep the read context resolvable where repos/UoW are registered, without breaking the layer-direction ArchUnit rules.
- **Migrations source.** EF migrations are generated from the **write** context (`{Service}DbContext`); confirm tooling still targets it after the base+sibling refactor. Existing Order migrations must continue to apply (backward-compatible).
- **Multi-tenancy / interceptors.** `BaseDbContext` provides tenant filters, soft-delete + auditing interceptors, and `EnforceTenantOnSave`. These must remain effective through `IUnitOfWork.SaveChangesAsync`; verify the UoW commits via the same `DbContext` instance the repos write to (single scoped instance per request).
- **Wolverine handler DI.** Handlers are static methods with injected deps; confirm the new abstractions resolve through Wolverine's container the same way the current ones do.

## Out of scope

- Strongly-typed entity ids (everything stays `Guid`).
- Replacing the Ardalis specification DSL.
- New service scaffolding beyond `order`/`catalog`.
- Any change to messaging (WolverineFx) or the read-model/projection strategy beyond context wiring.

## Risks

- **Low:** the abstractions already exist and compile; this is adoption + refactor, not greenfield.
- **Medium:** the `order`/`catalog` handler rewrites touch every command handler — covered by existing tests + the new ArchUnit guard.
- **Low:** doc reversal could confuse if partially done; treat docs as part of the definition of done, not a follow-up.
