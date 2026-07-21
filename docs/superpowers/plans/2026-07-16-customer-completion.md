# Customer Completion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development (or superpowers:executing-plans) to implement task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Complete the `customer` commerce service from its current *tenant-authority-only* skeleton to sibling-service parity by adding a **`Customer` profile aggregate** (with an owned `Address` collection), its CQRS handlers + HTTP CRUD surface, a `CustomerCreated` integration event, handler/integration tests, an EF migration, and the arch-test updates that adding real aggregates/endpoints requires.

**Context (why this isn't a pure catalog-style mirror):** The platform auth design (`docs/superpowers/specs/2026-06-28-platform-auth-architecture-design.md`) deliberately scoped this service to the **`Tenant` authority slice** (gRPC `GetTenantDatabaseInfo`) and listed "a full-featured customer service" as a non-goal. Two pieces of plumbing a real customer service would want are **dormant platform-wide**:
1. **Tenant resolution is not wired into any host** (`AddTeckCloudMultiTenancy` is never called), so `ITenantInfo.Id` resolves to empty at runtime — exactly like `order`/`basket`/`catalog` today.
2. **Nothing reads the Keycloak `sub` claim** anywhere; there is no `ICurrentUser`.

This plan takes the **pragmatic, sibling-consistent increment**: add the customer profile aggregate mirroring `catalog`'s `Supplier`, read the `sub` via a small self-contained host accessor (mirroring `BasketIdentityAccessor`), and let `TenantId` stay dormant like every sibling (forward-compatible — it populates for free once tenancy is switched on). Turning on tenant resolution platform-wide is **out of scope** (see Deferred).

## Global Constraints

- **Isolation:** all work in the `worktree-customer-service` worktree (`.claude/worktrees/customer-service`), branched from `main`.
- **Signed commits only** as `jl@tecklab.dk`; never bypass signing.
- **Analyzers are build errors** (`TreatWarningsAsErrors=true`); root `.editorconfig` is an allowlist. XML-doc every public type/member; file-scoped namespaces; ordered usings; no new blanket suppressions.
- **Namespaces:** the customer projects use root namespace **`Customers.*`** (note the plural — `Customers.Domain`, `Customers.Application`, `Customers.Host`), not `Customer.*`. Match the existing skeleton exactly.
- **Endpoints are dispatch-only:** Request + Validator + `AuthenticatedEndpoint<TReq,TResp>` calling `bus.InvokeAsync<T>`. Mapping stays in `Application/{Cap}/Mapping/` (Mapperly). No logic in the Host.
- **ErrorOr→HTTP status is the known deferred platform gap:** errored `ErrorOr<T>` handler results surface as HTTP 200 + null. Read handlers return `ErrorOr<T>`; endpoints invoke the **inner** `T`. Match siblings; no local fix.
- **Repository + `IUnitOfWork` single commit point.** Handlers depend on `IGenericReadRepository<T,Guid>` / `IGenericWriteRepository<T,Guid>` + `IUnitOfWork`. Query logic in Ardalis `Specification`s under `Application/{Cap}/ReadModels/`. Load-to-mutate uses `enableTracking: true`.
- **Every commit:** build the touched project warning-clean before committing.
- **Reference services to mirror:** `catalog` (Supplier aggregate = the CRUD template), `basket` (identity accessor + integration-event publish + integration-test auth harness).

---

## Scope

**In scope**
- `Customer` aggregate (`ITenantScoped`): `KeycloakSubjectId` (string), `Email`, `FirstName`, `LastName`, `IsActive`, owned `Address` collection.
- Commands: `CreateCustomer` (emits `CustomerCreated`), `UpdateCustomerProfile`, `AddCustomerAddress`. Queries: `GetCustomer`, `ListCustomers`.
- HTTP endpoints for each (routes under the `customer` service prefix, `Version(0)`).
- `CustomerCreatedIntegrationEvent` in `SharedKernel.Events` (customer owns this contract).
- `ICustomerIdentityAccessor` + host impl reading the `sub` / `ClaimTypes.NameIdentifier` claim.
- Mapperly mappers, response DTOs, Ardalis specs, EF configuration (+ `.ValueGeneratedNever()` on the owned `Address.Id`), `DbSet<Customer>` on `TenantDbContextBase`.
- Migration `AddCustomerProfiles` (additive: `customers` + `addresses` tables alongside existing `tenants`).
- Handler unit tests + Testcontainers HTTP integration tests (new `MockBearerAuthenticationHandler` + `CustomerIntegrationTestBase` that sets a `sub` claim).
- Arch-test updates: re-enable the endpoint, handler, and tenant-scoped rules with a documented **`Tenant` carve-out** (Tenant stays non-`ITenantScoped`).

**Deferred (documented, not built here)**
- **Turning on tenant resolution** (`AddTeckCloudMultiTenancy` in the host). Platform-rippling; belongs in a dedicated auth-phase task. `TenantId` stays dormant (empty) like all siblings until then.
- **`CustomerGroup`** aggregate — not on the critical path (`order` needs `CustomerCreated` + profile). Follow-up.
- **First-login auto-provisioning** flow (who calls `POST /customers` on first Keycloak login) — a gateway/auth concern, not this service.

---

## Task 1: `CustomerCreated` integration-event contract

**Files:** Create `src/shared/SharedKernel.Events/CustomerCreatedIntegrationEvent.cs`.

Mirror `BasketCheckedOutIntegrationEvent`: `[MemoryPackable] public partial class ... : IntegrationEvent` in namespace `SharedKernel.Events`, `[MemoryPackConstructor]` empty ctor, mutable props `CustomerId` (Guid), `TenantId` (string = string.Empty), `KeycloakSubjectId` (string = string.Empty), `Email` (string = string.Empty).

- [ ] Create the event class (XML-documented).
- [ ] `dotnet build src/shared/SharedKernel.Events/SharedKernel.Events.csproj` — warning-clean.
- [ ] Commit: `feat(events): CustomerCreated integration-event contract`.

---

## Task 2: `Customer` + `Address` domain

**Files:** Create `Customer.Domain/Entities/Customer.cs`, `Customer.Domain/Entities/Address.cs`, `Customer.Domain/Events/CustomerCreated.cs` (domain event, sealed).

- `Address : BaseEntity` (owned; `internal static Create(...)`, private ctor): `Line1`, `Line2?`, `City`, `PostalCode`, `Country`, `IsPrimary`.
- `Customer : BaseEntity, IAggregateRoot, ITenantScoped`: `string TenantId { get; set; }` (interface — non-private setter), all other setters private; `KeycloakSubjectId`, `Email`, `FirstName`, `LastName`, `IsActive`; private `List<Address> _addresses` exposed as `IReadOnlyList<Address> Addresses`.
  - `static Customer Create(string tenantId, string keycloakSubjectId, string email, string firstName, string lastName)` — validates required fields; raises `CustomerCreated` domain event; `IsActive = true`.
  - `void UpdateProfile(string firstName, string lastName)`; `Guid AddAddress(...)` (first address → `IsPrimary = true`).
- `CustomerCreated` sealed domain event in `...Domain.Events` carrying `CustomerId`, `TenantId`, `KeycloakSubjectId`, `Email`.

- [ ] Create the three files (mirror `catalog` `Supplier`/`Variant` + `SupplierPriceHistory` owned pattern; `DomainEventRules` require sealed events in the `Domain.Events` namespace).
- [ ] Unit tests `tests/unit/Customer.UnitTests/Domain/CustomerTests.cs`: `Create` sets values + raises event; rejects blank email/subject; `AddAddress` marks first primary.
- [ ] Build Domain + UnitTests warning-clean; run the new tests.
- [ ] Commit: `feat(customer): Customer + Address domain`.

---

## Task 3: Application layer — DTOs, specs, mappers, DbSet

**Files:** under `Customer.Application/Customers/` create `Responses/CustomerDto.cs`, `Responses/AddressDto.cs`, `ReadModels/CustomerByIdSpec.cs`, `ReadModels/AllCustomersSpec.cs`, `ReadModels/CustomerBySubjectSpec.cs`, `Mapping/CustomerMapper.cs`; edit `Customer.Application/Database/TenantDbContextBase.cs` to add `public DbSet<Customer> Customers => Set<Customer>();`.

- `CustomerDto(Guid Id, string KeycloakSubjectId, string Email, string FirstName, string LastName, bool IsActive, IReadOnlyList<AddressDto> Addresses)`; `AddressDto(Guid Id, string Line1, string? Line2, string City, string PostalCode, string Country, bool IsPrimary)`.
- Specs: Ardalis `Specification<Customer>` (`ById`, `All`, `BySubject`).
- `CustomerMapper`: `[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)] static partial class` with `ToDto` extensions for `Customer` and `Address`.

- [ ] Create the files; add the `DbSet`.
- [ ] Build Application warning-clean.
- [ ] Commit: `feat(customer): customer DTOs, specs, mapper, DbSet`.

---

## Task 4: Identity accessor (reads Keycloak `sub`)

**Files:** Create `Customer.Application/Customers/ICustomerIdentityAccessor.cs` (interface: `string? KeycloakSubjectId { get; }`), `Customer.Host/Infrastructure/CustomerIdentityAccessor.cs` (impl over `IHttpContextAccessor` reading `HttpContext.User.FindFirstValue("sub") ?? FindFirstValue(ClaimTypes.NameIdentifier)`); register in `Program.cs` (`AddScoped`). Mirror `BasketIdentityAccessor`.

- [ ] Create both + register. Build Host warning-clean.
- [ ] Commit: `feat(customer): keycloak-subject identity accessor`.

---

## Task 5: CQRS handlers

**Files:** under `Customer.Application/Customers/Features/{Feature}/V1/` (Command/Query + `*Handler`):
- `CreateCustomer` (`ICommand<CustomerDto>`): handler injects `IGenericWriteRepository<Customer,Guid>`, `IUnitOfWork`, `ITenantInfo`, `IMessageBus`. `Customer.Create(tenant.Id ?? string.Empty, command.KeycloakSubjectId, ...)` → `AddAsync` → `SaveChangesAsync` → **then** `bus.PublishAsync(new CustomerCreatedIntegrationEvent{...})` (publish after commit, per basket convention). Returns `CustomerDto`.
- `UpdateCustomerProfile` (`ICommand<ErrorOr<CustomerDto>>`): load tracking → `UpdateProfile` → save.
- `AddCustomerAddress` (`ICommand<ErrorOr<AddressDto>>`): load tracking → `AddAddress` → save.
- `GetCustomer` (`IQuery<ErrorOr<CustomerDto>>`) + `ListCustomers` (`IQuery<IReadOnlyList<CustomerDto>>`): read repo + spec.

Handlers are `public static class ...Handler` (name MUST end `Handler` — arch rule), method-param DI, `.ConfigureAwait(false)` on every await.

- [ ] Create commands/queries + handlers.
- [ ] Handler unit tests (`tests/unit/Customer.UnitTests/Application/*HandlerTests.cs`): copy a `CustomerTestContext` helper from `CatalogTestContext` (in-memory + stubbed-save). Cover Create (+ event captured via a substituted `IMessageBus`), Update, AddAddress, Get/List.
- [ ] Build + run unit tests.
- [ ] Commit: `feat(customer): customer CQRS handlers + unit tests`.

---

## Task 6: EF configuration + migration

**Files:** Create `Customer.Application/Database/Configurations/CustomerConfiguration.cs`; generate migration in `Customer.Host`.

- `ToTable("customers")`; `HasKey(Id)`; `Property(TenantId).HasMaxLength(64)`; string maxlengths; `Ignore(DomainEvents)`; unique index on `KeycloakSubjectId`.
- `OwnsMany(c => c.Addresses)` → `ToTable("addresses")`, `WithOwner().HasForeignKey("CustomerId")`, `HasKey(a => a.Id)`, **`Property(a => a.Id).ValueGeneratedNever()`** (the nested-owned INSERT gotcha from catalog), string maxlengths; `Navigation(c => c.Addresses).UsePropertyAccessMode(PropertyAccessMode.Field)`.

- [ ] Create configuration; build Application.
- [ ] `dotnet ef migrations add AddCustomerProfiles --project src/services/commerce/customer/Customer.Host/Customer.Host.csproj --startup-project <same> --context CustomerDbContext --output-dir Database/Migrations`.
- [ ] Inspect: creates `customers` + `addresses`; leaves `tenants` untouched.
- [ ] `dotnet format` the generated migration (block→file-scoped namespace, trailing commas, drop redundant `using System;` — same fix catalog needed).
- [ ] Build Host warning-clean.
- [ ] Commit: `feat(customer): customer/address EF config + AddCustomerProfiles migration`.

---

## Task 7: HTTP endpoints

**Files:** under `Customer.Host/Endpoints/Customers/` — Request + Validator + `AuthenticatedEndpoint` per feature:
- `POST /customers` (201, `CustomerDto`), `GET /customers/{customerId}` (200), `GET /customers` (200 list), `PUT /customers/{customerId}/profile` (200), `POST /customers/{customerId}/addresses` (201, `AddressDto`).
- Permissions: writes `new("customer","manage","public")`; reads `new("customer","read","public")`. `Version(0)`.
- `CreateCustomer` request need not carry the subject — the endpoint reads it from `ICustomerIdentityAccessor` and passes it into the command (mirrors how basket resolves identity in the handler; here we pass it through the command so the handler stays pure). Decide at implementation: inject the accessor into the endpoint OR into the handler — prefer the **handler** (matches basket) so the endpoint stays dispatch-only. If handler-injected, `CreateCustomerCommand` omits the subject and the handler reads `identity.KeycloakSubjectId`.

- [ ] Create the endpoint trio per feature.
- [ ] Build Host warning-clean.
- [ ] Commit: `feat(customer): customer HTTP endpoints`.

---

## Task 8: Integration test harness + flows

**Files:** under `tests/integration/Customer.IntegrationTests/` — copy `MockBearerAuthenticationHandler.cs` from `Basket.IntegrationTests` **and add a `sub` claim** (existing mocks don't set one; the create handler needs it); create `CustomerIntegrationTestBase.cs` (boot `Customer.Host` via `WebApplicationFactory<Program>` over Testcontainers Postgres, register `AddMultiTenant<TenantDetails>()`, mock bearer, permissive protected-resource handler — mirror `BasketIntegrationTestBase`, but note `Program` runs via JasperFx so set `JasperFxEnvironment.AutoStartHost = true`); create `CustomerProfileTests.cs`.

- [ ] Copy/adapt harness (add the `sub` claim to the mock).
- [ ] `CustomerProfileTests`: create → 201 (+ subject/email round-trip); get after create → 200; list includes it; update profile → 200 reflects change; add address → 201.
- [ ] `dotnet test tests/integration/Customer.IntegrationTests` — all green (proves migration applies + endpoints + event publish path).
- [ ] Commit: `test(customer): customer profile HTTP integration tests`.

---

## Task 9: Architecture-test updates

**Files:** edit `tests/architecture/Customer.Architecture.UnitTests/CustomerArchitectureTests.cs`.

Adding real aggregates/handlers/endpoints makes previously-skipped rules applicable again:
- **Re-enable** `...Host_ShouldNotReferenceDomainDirectly`? No — the gRPC handler still lives in Host and references `Tenant`. Keep that skip, documented.
- **Re-enable** the endpoint rule (`EndpointsShouldDeriveFromAuthenticatedEndpoint`) — now there ARE endpoints; assert they derive from `AuthenticatedEndpoint`.
- **Re-enable** `ApplicationHandlers_ShouldEndWithHandler` — now there ARE WolverineFx handlers.
- **Tenant-scoped rule:** enforce `ITenantScoped` for aggregates **except `Tenant`**. If `SharedArchitectureRules` has no exclusion hook, hand-write a `[Fact]` that asserts every `IAggregateRoot` in `Customers.Domain.Entities` except `Tenant` implements `ITenantScoped`, with a documented rationale comment (Tenant is the registry/authority).
- Re-check the covariant-generic rules (`CommandsShouldBeImmutable`/`QueriesShouldNotModifyState`): now that real `ICommand<>`/`IQuery<>` implementors exist, the ArchUnitNET empty-set crash likely no longer applies — try re-enabling; if it still throws, keep the skip with the existing documented rationale.

- [ ] Update the arch tests; run `dotnet test tests/architecture/Customer.Architecture.UnitTests` green.
- [ ] Commit: `test(customer): re-enable arch rules for aggregates/handlers/endpoints (Tenant carve-out)`.

---

## Task 10: Full gate + finish

- [ ] `nx affected -t build test lint typecheck --base=main` — all green (build, all customer test projects, Aspire smoke).
- [ ] Confirm Aspire wiring already present (it is) and, if needed, that `customer` serves the new endpoints.
- [ ] Update `docs/superpowers/plans/services/customer.md` status → `🟢 profile CRUD complete`; note the deferrals (tenant-resolution wiring, CustomerGroup, first-login provisioning).
- [ ] Finish via superpowers:finishing-a-development-branch → open PR against `main`.

---

## Self-Review

- **Sibling parity:** aggregate/handler/endpoint/test shape mirrors `catalog` `Supplier`; event contract + publish + identity accessor mirror `basket`.
- **Tenancy honesty:** `Customer` is `ITenantScoped` and maps the `TenantId` column, but resolution stays dormant like every sibling — `CustomerCreated.TenantId` is empty until tenancy is switched on platform-wide (deferred, documented). Forward-compatible.
- **Subject:** read via a self-contained host accessor over the `sub`/`NameIdentifier` claim; the integration mock is updated to set it.
- **Known gotchas pre-empted:** owned `Address.Id` `ValueGeneratedNever()`; migration `dotnet format` fix; handler names end `Handler`; owned-config-before-`base.OnModelCreating`.
- **Deferred & flagged:** tenant-resolution wiring, `CustomerGroup`, first-login provisioning.
