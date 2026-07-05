# Work Package: `customer` service

**Group:** commerce · **Tier:** 0 · **Status:** 🟡 skeleton → complete · **Branch:** `worktree-customer-service`
**Parallelism:** independent — consumes no events. **Special role:** it is the tenant authority.

> Scope brief, not a finished plan. Partial projects exist (it already has a gRPC `GetTenantDatabaseInfo` handler and `TenantByIdSpec`). Complete it, mirroring **order**/**basket**. Read `src/services/AGENTS.md` and `COORDINATION.md` first, and study the existing `Customer.*` projects + `CustomerArchitectureTests` (it documents the query-less arch-test pattern you'll reuse).

## Bounded context
Owns **the tenant registry + customer profiles**: tenants (the multi-tenancy source of truth for every other service), customer accounts, addresses, and customer groups. Note: identity/auth is Keycloak (external) — this service owns profile/domain data, not credentials.

## Domain (starting shape)
- `Tenant` (aggregate root) — the global tenant registry; **explicitly NOT `ITenantScoped`** (it *is* the tenant authority; see the `CustomerArchitectureTests` rationale that skips the tenant-scoped rule for it).
- `Customer` (aggregate root, `ITenantScoped`): profile, linked Keycloak subject id.
- `Address` (owned), `CustomerGroup`.

## Events
- **Emits:** `CustomerCreated` (customerId, tenantId, keycloak subject) — **customer owns this contract.** order consumes it (per roadmap) to associate orders with customers.
- **Consumes:** none.

## API surface (indicative)
- gRPC: tenant lookup (already partially built — `GetTenantDatabaseInfo`).
- HTTP: customer profile + address CRUD (authenticated, tenant-scoped). Note the existing service exposes gRPC only; adding HTTP endpoints means the arch test's endpoint rule (skipped today) becomes applicable — mirror how basket/order register FastEndpoints.

## Dependencies & ordering
Independent — start now.

## Shared-file touchpoints
`.slnx`, `AppHost.cs` (customer resource already exists), `SharedKernel.Events/CustomerCreatedIntegrationEvent.cs` (new). No `nx.json` change.

## Watch-items
- The `Tenant` aggregate is the one entity that must NOT be `ITenantScoped` — keep that carve-out and its arch-test skip documented (copy `CustomerArchitectureTests`'s rationale).
- Query-less today → arch test skips `QueriesShouldNotModifyState` (and `CommandsShouldBeImmutable` if still no WolverineFx commands). Once you add real CQRS commands/queries, revisit which shared rules apply (basket keeps `CommandsShouldBeImmutable` because it has commands).
- This service underpins every other tenant — changes to `TenantDetails` / tenant resolution ripple platform-wide; coordinate loudly.
