# Billing Service Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Implement task-by-task; steps use checkbox (`- [ ]`) syntax.

**Goal:** Build the greenfield **`billing`** service (Tier-0, `operations` group) to sibling parity: payments + invoicing keyed to orders, a provider-agnostic payment abstraction, an idempotent `OrderPlaced` consumer that captures payment and emits `PaymentCaptured`/`PaymentFailed`, an HTTP surface, EF migration, and the full test set. Mirrors `inventory` (greenfield + event-consuming) and `order`/`catalog` conventions.

**Reference services:** `inventory` (scaffolding + event consumption + idempotency), `basket`/`order` (event emit-after-commit, SmartEnum), `catalog`/`customer` (aggregate + CRUD + EF owned collections + tests).

## Global Constraints

- **Isolation:** all work in `worktree-billing-service` (`.claude/worktrees/billing-service`), branched from `main`.
- **Signed commits** as `jl@tecklab.dk`; never bypass signing.
- **Analyzers are build errors** (`TreatWarningsAsErrors=true`); XML-doc public types/members; file-scoped namespaces; ordered usings; no blanket suppressions.
- **Namespaces:** project/folder names are singular (`Billing.Domain`), **RootNamespaces/namespaces are PLURAL** (`Billings.Domain`, `Billings.Application`, `Billings.Host`) — mirror inventory (`Inventories.*`).
- **Group:** billing lives at `src/services/operations/billing/` (the dir exists but is empty). It is the FIRST `operations` service. Never reference commerce services directly — share only via `SharedKernel.Events`.
- **Central package versions** (`Directory.Packages.props`) — versionless `<PackageReference>`. Only add a version entry if billing needs a package not already listed.
- **Repository + `IUnitOfWork` single commit point;** Ardalis specs under `Application/{Cap}/ReadModels/`; Mapperly in `Application/{Cap}/Mapping/`; Options via `IOptions<T>` (never `IConfiguration` in handlers).
- **ErrorOr→HTTP-200-null gap:** matched, not fixed (endpoints invoke the inner `T`).
- **Every commit:** build the touched project warning-clean first.

## Scope

**In scope**
- Domain: `Payment` (aggregate, `ITenantScoped`), `Invoice` (aggregate, `ITenantScoped`, owned `InvoiceLine`), `Money` VO, `PaymentStatus` SmartEnum (Pending/Authorized/Captured/Failed/Refunded).
- Provider abstraction: `IPaymentProvider` (Application) + a stub impl configured via `IOptions<PaymentProviderOptions>` — **no real provider hardcoded**; the stub simulates capture (success/decline configurable).
- `OrderPlaced` consumer: idempotent by `OrderId` (unique) — on receipt, capture payment via the provider, create an invoice, emit `PaymentCaptured` (success) or `PaymentFailed` (decline). Re-delivery is a no-op.
- Commands/queries: `CapturePayment` (manual `POST /payments` for an order), `GetPayment`, `GetInvoice`, `ListPayments`.
- HTTP endpoints: `POST /payments`, `GET /payments/{paymentId}`, `GET /payments`, `GET /invoices/{invoiceId}`.
- Events: `PaymentCapturedIntegrationEvent`, `PaymentFailedIntegrationEvent` in `SharedKernel.Events`.
- Full scaffolding: 3 projects + 3 test projects, `.slnx`, Aspire (`AppHost.cs` + `.csproj`), appsettings, `InitialBilling` migration, deploy base (`operations` label).

**Deferred (documented — see final task)**
- **RabbitMQ transport wiring** (`UseRabbitMq`/`ConfigureStandardRuntime`). Dormant in ALL siblings today, so the `OrderPlaced` consumer is discovered + unit/handler-testable but does not receive cross-service messages until transport is turned on platform-wide. Do NOT wire it here (platform task).
- **Tenant resolution** (dormant like all siblings; `TenantId` stamped empty).
- Provider webhook endpoint; `PaymentMethod` management (entity + CRUD); refund flow (status enum has `Refunded` but no feature); real provider SDK integration.
- No `nx.json` release group (follow the `inventory` precedent — inventory ships without one).

**Security watch-items (from brief):** never persist raw card data — only a provider token/reference string; provider behind the interface + `IOptions`; idempotent capture guarded by the `OrderId` unique key.

---

## Task 1: Project scaffolding (3 projects, builds empty)

Create, mirroring `inventory` exactly (see the scaffolding recipe), at `src/services/operations/billing/`:
- `Billing.Domain/Billing.Domain.csproj` (RootNamespace `Billings.Domain`; refs SharedKernel.Core; pkgs Ardalis.SmartEnum, Ardalis.Specification, ErrorOr).
- `Billing.Application/Billing.Application.csproj` (`Billings.Application`; refs Domain + SharedKernel.Core/Events/Infrastructure; pkgs ErrorOr, FluentValidation, Riok.Mapperly, WolverineFx).
- `Billing.Host/Billing.Host.csproj` (`Microsoft.NET.Sdk.Web`, `Billings.Host`; refs Teck.ServiceDefaults + SharedKernel.Core/Events/Grpc.Contracts/Infrastructure + Application + Domain; pkgs FastEndpoints, EFCore, Npgsql.EFCore, WolverineFx, WolverineFx.EntityFrameworkCore).
- A placeholder is fine to make each compile (e.g. an `AssemblyMarker`/temporary type), removed as real types land.

- [ ] Create the 3 csproj + minimal content; `dotnet build` each warning-clean.
- [ ] Add the 3 `<Project>` lines + `/src/services/operations/billing/` folder to `Teck.Platform.slnx`.
- [ ] Commit: `chore(billing): scaffold Domain/Application/Host projects`.

---

## Task 2: `PaymentCaptured` + `PaymentFailed` event contracts

Create in `src/shared/SharedKernel.Events/`: `PaymentCapturedIntegrationEvent.cs`, `PaymentFailedIntegrationEvent.cs` — `[MemoryPackable] public partial class ... : IntegrationEvent`, `[MemoryPackConstructor]`, props: `PaymentId` (Guid), `OrderId` (Guid), `TenantId` (string=empty), `Amount` (decimal), `Currency` (string=empty); Failed adds `Reason` (string=empty). Mirror `BasketCheckedOutIntegrationEvent`.

- [ ] Create both; build SharedKernel.Events warning-clean.
- [ ] Commit: `feat(events): PaymentCaptured + PaymentFailed contracts`.

---

## Task 3: Domain — `Payment`, `Invoice`, `Money`, `PaymentStatus`

Under `Billing.Domain/`:
- `ValueObjects/Money.cs` — mirror `Catalog.Domain/ValueObjects/Money.cs` (Amount decimal + Currency string; validation).
- `Entities/PaymentStatus.cs` — `sealed class PaymentStatus : SmartEnum<PaymentStatus>` (Pending=1, Authorized=2, Captured=3, Failed=4, Refunded=5), private ctor. (Mirror `OrderStatus`.)
- `Entities/Payment.cs` — `sealed : BaseEntity, IAggregateRoot, ITenantScoped`. Fields: `OrderId`, `CustomerId`, `Amount` (Money), `Status` (PaymentStatus), `ProviderReference` (string?, tokenized). `static Create(tenantId, orderId, customerId, Money amount)` → Status=Pending; `MarkCaptured(string providerReference)` → Captured + raise `PaymentCaptured` domain event; `MarkFailed(string reason)` → Failed + raise `PaymentFailed` domain event.
- `Entities/Invoice.cs` — `sealed : BaseEntity, IAggregateRoot, ITenantScoped`, owned `InvoiceLine` collection. Fields: `OrderId`, `IssuedAt`, total `Amount` (Money). `static Create(tenantId, orderId, Money total, IEnumerable<InvoiceLine> lines, DateTimeOffset issuedAt)`.
- `Entities/InvoiceLine.cs` — owned `: BaseEntity`, `internal static Create(...)`: `ProductId`, `Description`, `Quantity`, `UnitPrice` (Money or decimal+currency).
- `DomainEvents/PaymentCaptured.cs`, `DomainEvents/PaymentFailed.cs` — sealed, in `Billings.Domain.DomainEvents` (NOTE: enforced namespace is `*.Domain.DomainEvents`, per the arch rule — NOT `Domain.Events`).

- [ ] Create the entities/VO/enum/events; unit tests (`tests/unit/Billing.UnitTests/Domain/*`) — after Task 12's test-project creation, OR create the unit-test project here. (Order: create `Billing.UnitTests` csproj in this task so domain tests can run.) Cover: Payment.Create→Pending; MarkCaptured/MarkFailed transitions + events; Invoice.Create; Money validation; PaymentStatus values.
- [ ] Build Domain + UnitTests warning-clean; run tests.
- [ ] Commit: `feat(billing): payment/invoice domain + PaymentStatus`.

---

## Task 4: Persistence scaffolding (DbContext split, repos, options)

Mirror inventory's persistence wiring:
- `Billing.Application/Database/BillingDbContextBase.cs` (abstract; DbSets `Payments`, `Invoices`; `ApplyConfigurationsFromAssembly` before base).
- `Billing.Application/Database/BillingDbContext.cs` (write leaf).
- `Billing.Host/Database/BillingReadDbContext.cs` (NoTracking).
- `Billing.Host/Database/BillingPersistenceExtensions.cs` (`AddBillingPersistence`: `AddHybridMultiTenantDbContexts<BillingDbContext,BillingReadDbContext>` keys `BillingWrite`/`BillingRead`, serviceName "billing"; repo open-generics; `IUnitOfWork`; `AddHttpContextAccessor`).
- `Billing.Host/Database/{BillingDbContextDesignTimeFactory,BillingReadRepository,BillingWriteRepository}.cs`.
- `Billing.Application/Billing/PaymentProviderOptions.cs` (`const string SectionName = "PaymentProvider"`; e.g. `bool SimulateSuccess`, `string ProviderName`).

- [ ] Create the files; build Application + Host warning-clean.
- [ ] Commit: `feat(billing): DbContext split, repositories, provider options`.

---

## Task 5: Payment provider abstraction + stub

- `Billing.Application/Billing/Payments/IPaymentProvider.cs` — `Task<PaymentProviderResult> CaptureAsync(Guid orderId, Money amount, CancellationToken ct)`; `PaymentProviderResult(bool Success, string? ProviderReference, string? FailureReason)`.
- `Billing.Host/Infrastructure/StubPaymentProvider.cs` — `IPaymentProvider` impl reading `IOptions<PaymentProviderOptions>`; returns success with a synthetic token when `SimulateSuccess`, else a decline. Register `AddScoped<IPaymentProvider, StubPaymentProvider>()` in Program.cs. **No real provider, no card data.**

- [ ] Create + register; build Host warning-clean; small unit test for the stub.
- [ ] Commit: `feat(billing): payment-provider abstraction + stub`.

---

## Task 6: CQRS — CapturePayment + queries (+ mappers, DTOs, specs)

Under `Billing.Application/Billing/Payments/` and `.../Invoices/`:
- DTOs: `PaymentDto`, `InvoiceDto`, `InvoiceLineDto`.
- Specs: `PaymentByIdSpec`, `PaymentByOrderSpec` (idempotency lookup), `AllPaymentsSpec`, `InvoiceByIdSpec`.
- Mappers: Mapperly `PaymentMapper`, `InvoiceMapper`.
- `CapturePaymentCommand(Guid OrderId, Guid CustomerId, decimal Amount, string Currency) : ICommand<ErrorOr<PaymentDto>>` + handler: idempotency (existing payment for OrderId → return it), create Payment (Pending), call `IPaymentProvider.CaptureAsync`, MarkCaptured/MarkFailed, create Invoice on success, `SaveChangesAsync`, then publish `PaymentCaptured`/`PaymentFailed` integration event. Inject repos + `IUnitOfWork` + `IPaymentProvider` + `ITenantInfo` + `IMessageBus`.
- `GetPaymentQuery`, `ListPaymentsQuery`, `GetInvoiceQuery` handlers (read).

- [ ] Create; handler unit tests (in-memory + stubbed provider + substituted `IMessageBus`): capture success → Captured + event + invoice; provider decline → Failed + PaymentFailed event, no invoice; idempotent re-capture returns existing. Copy a `BillingTestContext` from `CatalogTestContext`.
- [ ] Build + run unit tests warning-clean.
- [ ] Commit: `feat(billing): capture-payment + query handlers + unit tests`.

---

## Task 7: `OrderPlaced` consumer

`Billing.Application/Billing/EventHandlers/IntegrationEvents/OrderPlacedHandler.cs` — `public static class`, `public static async Task Handle(OrderPlacedIntegrationEvent evt, ...deps..., IMessageBus bus, CancellationToken ct)`. Reuse the CapturePayment logic (call the same handler/committer or a shared internal capture routine keyed by `evt.OrderId`, `evt.CustomerId`, `evt.TenantId`, `evt.Total`). Idempotent by OrderId. Mirror inventory's `OrderPlacedHandler` shape.

- [ ] Create; unit test invoking `Handle` directly with an `OrderPlacedIntegrationEvent` (success + re-delivery no-op).
- [ ] Build + test. Commit: `feat(billing): OrderPlaced consumer (idempotent capture)`.

---

## Task 8: EF configuration + `InitialBilling` migration

- `Billing.Application/Database/Configurations/{PaymentConfiguration,InvoiceConfiguration}.cs`: tables `payments`/`invoices`/`invoice_lines`; `Money` owned (Amount+Currency columns); `PaymentStatus` `.HasConversion(s=>s.Value, v=>PaymentStatus.FromValue(v))`; **unique index on `Payment.OrderId`** (idempotency); owned `InvoiceLine` with `.ValueGeneratedNever()` on its Id (the catalog nested-owned gotcha); `Ignore(DomainEvents)`; `TenantId` HasMaxLength(64).
- Generate `InitialBilling` migration in `Billing.Host`; `dotnet format` the generated files (block→file-scoped/trailing-comma gotcha).

- [ ] Config + migration; build Host warning-clean; inspect migration (payments+invoices+invoice_lines, unique OrderId index).
- [ ] Commit: `feat(billing): EF config + InitialBilling migration`.

---

## Task 9: HTTP endpoints

Under `Billing.Host/Endpoints/`: Request+Validator+`AuthenticatedEndpoint` per feature — `POST /payments` (201 PaymentDto, permission `("billing","manage","public")`), `GET /payments/{paymentId}` (200, read), `GET /payments` (200 list, read), `GET /invoices/{invoiceId}` (200, read). Dispatch-only; ErrorOr handlers invoke inner type; `Version(0)`.

- [ ] Create; build Host warning-clean. Commit: `feat(billing): billing HTTP endpoints`.

---

## Task 10: Aspire + appsettings wiring

- `Billing.Host/appsettings.json` + `appsettings.Development.json` (TeckService `BillingServiceCors`, Serilog `logs/billing-.log`, conn keys `BillingWrite`/`BillingRead` db `billing`, Keycloak `resource: "billing-api"`, `PaymentProvider` section).
- `src/aspire/Teck.AppHost/Teck.AppHost.csproj`: add ProjectReference to `Billing.Host` (required for `Projects.Billing_Host`).
- `src/aspire/Teck.AppHost/AppHost.cs`: `var billingDb = postgres.AddDatabase("billingdb");` + flat `AddProject<Projects.Billing_Host>("billing")` block (WithHttpEndpoint, `ConnectionStrings__BillingWrite/Read`, WithReference rabbitmq/redis/keycloak, WaitFor billingDb + keycloak).

- [ ] Wire; build AppHost warning-clean. Commit: `chore(billing): Aspire registration + appsettings`.

---

## Task 11: Integration tests + architecture tests

- `tests/integration/Billing.IntegrationTests/` — csproj + `MockBearerAuthenticationHandler` (copy from Basket, add `sub` if needed) + `BillingIntegrationTestBase` (boot `Billing.Host` over Testcontainers; `RunTeckServiceAsync`/JasperFx → set `JasperFxEnvironment.AutoStartHost=true`; connection-string + auth wiring) + `PaymentFlowTests` (POST /payments capture → 201; GET payment/invoice; list). Register in `.slnx`.
- `tests/unit/Billing.UnitTests/` already created in Task 3.
- `tests/architecture/Billing.Architecture.UnitTests/` — copy Order's 7-test class, swap `Order`→`Billing` (Host-not-ref-Domain; App-not-ref-Host; aggregates ITenantScoped; App-not-depend-DbContext/IRepositoryBase; handlers end `Handler`; endpoints derive AuthenticatedEndpoint; `SharedArchitectureRules.AssertAll`). Register in `.slnx`. **NOTE:** `Payment` + `Invoice` must be `ITenantScoped` to satisfy the aggregate rule (no Tenant-style carve-out here — billing has no registry aggregate).

- [ ] Create all; `dotnet test` each green (integration proves migration + endpoints + capture flow end-to-end). Commit: `test(billing): integration + architecture tests`.

---

## Task 12: Deploy base + full gate + finish

- `deploy/billing/base/{deployment,service,kustomization}.yaml` mirroring `deploy/catalog/base` but `part-of: operations`, name `billing-api`, image `ghcr.io/teck-lab/teck-monorepo/operations/billing-api`, `billing-postgres`/`billing-rabbitmq` secretRefs.
- Update `docs/superpowers/plans/services/billing.md` status → complete; record deferrals (RabbitMQ transport dormant, tenancy dormant, webhook/PaymentMethod/refund/real-provider, no nx release group).
- Run `nx affected -t build test lint typecheck --base=main` (expect green; note the pre-existing `Gateway.Public.IntegrationTests` failure is unrelated).
- Final whole-branch review, then finishing-a-development-branch → PR against `main`.

- [ ] Deploy base + doc + gate + final review + PR.

---

## Self-Review

- **Greenfield parity:** scaffolding mirrors inventory; event consume/emit + SmartEnum + Options mirror inventory/order; aggregate/EF/endpoints/tests mirror catalog/customer.
- **Security:** no raw card data (provider token string only); provider behind `IPaymentProvider` + `IOptions`; idempotent capture via `OrderId` unique index.
- **Honesty / deferrals:** RabbitMQ transport dormant (consumer ready, cross-service delivery off — matches ALL siblings); tenancy dormant; webhook/PaymentMethod/refund/real-provider out of scope; no nx release group (inventory precedent). All documented in the work-package doc + PR.
- **Known gotchas pre-empted:** owned `InvoiceLine.Id` `ValueGeneratedNever()`; migration `dotnet format`; handlers end `Handler`; domain events in `*.Domain.DomainEvents`; Aspire DB-name `db` suffix; AppHost.csproj ProjectReference for `Projects.Billing_Host`; plural `Billings.*` namespaces.
