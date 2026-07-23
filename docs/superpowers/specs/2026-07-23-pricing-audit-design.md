# Pricing Service — Audit & Completion Design

**Date:** 2026-07-23  
**Scope:** Audit the existing `pricing` service against its approved work-package brief and design spec, confirm the remaining gaps, and bound the completion work.  
**Grounding:**
- Work-package brief: `docs/superpowers/plans/services/pricing.md`
- Approved design: `docs/superpowers/specs/2026-07-05-pricing-service-design.md`
- Coordination rules: `docs/superpowers/plans/services/COORDINATION.md`
- Implementation: `src/services/commerce/pricing/`
- Tests: `tests/unit/Pricing.UnitTests/`, `tests/architecture/Pricing.Architecture.UnitTests/`, `tests/integration/Pricing.IntegrationTests/`

## 1. Purpose, scope, and non-goals

### Purpose
- Determine whether the existing Pricing service matches the approved contract and conventions.
- Identify only confirmed, bounded fixes.
- Define the focused regression coverage needed to protect those fixes.
- Specify the exact update to the stale `pricing.md` plan status.

### In scope
- Audit of source, tests, and shared-file touchpoints against the approved design.
- Decision record for the `PriceChanged` monetary payload (Amount + Currency) and ChangeType representation.
- Gap: public-gateway YARP routes for pricing endpoints.
- Test additions required to cover the gap.
- Plan-status update criteria.

### Out of scope / non-goals
- No new Pricing features (discounts, promotions, tax, bulk import, external FX provider, caching layer).
- No rewrite of the service, domain model, or event strategy.
- No changes to the `PriceChanged` contract shape unless evidence shows a real inconsistency; the audit confirms the current shape matches the approved design.
- No changes to the shared ErrorOr → HTTP status-code pipeline; that is a platform-wide limitation accepted by the design spec (`2026-07-05-pricing-service-design.md` §Error handling).
- No `nx.json` or release-group changes (commerce group already exists).
- No Aspire host registration changes (already present in `src/aspire/Teck.AppHost/AppHost.cs` lines 12, 74–82).

## 2. Current state

The Pricing service is implemented and currently builds and tests green:

| Project | Status |
|---|---|
| `Pricing.Domain` | Exists; implements `PriceList`, `Price`, `ExchangeRate`, `Money`, `PriceScope`, `PriceTier`, `PriceListStatus`, `PriceChanged` domain event, `PriceResolutionService`, `CurrencyConverter`. |
| `Pricing.Application` | Exists; CQRS handlers, read-model specifications, Mapperly mappers, `PricingDbContextBase`/`PricingDbContext`, `PricingOptions`, `IExchangeRateProvider`, `PricingEventPublisher`. |
| `Pricing.Host` | Exists; FastEndpoints, persistence extensions, `PricingReadDbContext`, migrations, `ExchangeRateProviderStub`, `Program.cs`/`Program.Public.cs`. |
| Unit tests | 51 passed (`tests/unit/Pricing.UnitTests/`). |
| Architecture tests | 6 passed (`tests/architecture/Pricing.Architecture.UnitTests/`). |
| Integration tests | 4 passed (`tests/integration/Pricing.IntegrationTests/`). |
| Solution registration | `Teck.Platform.slnx` lines 40–42 include the three source projects; lines 78, 85, 92 include the three test projects. |
| Aspire registration | `src/aspire/Teck.AppHost/AppHost.cs` registers `pricingdb` and `Projects.Pricing_Host` (lines 12, 77–82). |
| Shared event contract | `src/shared/SharedKernel.Events/PriceChangedIntegrationEvent.cs` exists and is owned by pricing. |
| Plan status | `docs/superpowers/plans/services/pricing.md` line 3 still marks the service as **🆕 new** — stale. |

Architecture conventions observed:
- Clean-architecture direction: Domain → Application → Host.
- Three-context EF shape: `PricingDbContextBase` (abstract, model once), `PricingDbContext` (write), `PricingReadDbContext` (NoTracking).
- Handlers use `IGenericReadRepository<,>` / `IGenericWriteRepository<,>` + `IUnitOfWork`; no direct `DbContext` or `Ardalis.Specification.IRepositoryBase` in Application.
- Static WolverineFx handlers, FastEndpoints `AuthenticatedEndpoint`, ServiceScan DI, Mapperly mapping, multi-tenant `ITenantScoped` aggregates.
- Migration `20260705181135_InitialPricing` present in `Pricing.Host/Database/Migrations/`.

## 3. Audit decision matrix

| Area | Evidence | Finding | Decision |
|---|---|---|---|
| **Domain model** | `Pricing.Domain/Entities/PriceList.cs`, `Price.cs`, `ExchangeRate.cs`; `ValueObjects/Money.cs`, `PriceScope.cs`, `PriceTier.cs`, `PriceListStatus.cs` | Matches approved design spec §Domain model. | ✅ Correct — no change. |
| **Price resolution algorithm** | `Pricing.Domain/Services/PriceResolutionService.cs`, `ResolvedSelection.cs`, `PriceResolutionContext.cs` | Implements active/valid filtering, scope compatibility, most-specific + tie-break, native-currency preference, quantity tiers. | ✅ Correct — no change. |
| **Currency conversion** | `Pricing.Domain/Services/CurrencyConverter.cs`, `PricingOptions.cs` | Rounding policy defaults to `MidpointRounding.ToEven`, 2 decimals. Stub provider registered. | ✅ Correct — no change. |
| **CQRS / persistence** | `Pricing.Application/Database/PricingDbContextBase.cs`, `PricingDbContext.cs`; `Pricing.Host/Database/PricingPersistenceExtensions.cs`, `PricingReadDbContext.cs`, `PricingWriteRepository.cs`, `PricingReadRepository.cs` | Three-context split, open-generic repos, `UnitOfWork<PricingDbContext>` bound to scoped write context. | ✅ Correct — no change. |
| **Command/query handlers** | `Pricing.Application/Pricing/Features/*/V1/*Handler.cs` | Static handlers, use generic repo + UoW, commit once, publish integration events after commit. | ✅ Correct — no change. |
| **`PriceChanged` domain event** | `Pricing.Domain/DomainEvents/PriceChanged.cs` | Carries `ProductId`, `PriceListId`, `TenantId`, `Amount`, `Currency`, `EffectiveFrom`, `PriceChangeType`. | ✅ Correct — matches approved design. |
| **`PriceChangedIntegrationEvent` contract** | `src/shared/SharedKernel.Events/PriceChangedIntegrationEvent.cs` | Carries same fields as domain event; `ChangeType` serialized as string. | ✅ Correct — matches approved design. |
| **Event publication** | `Pricing.Application/Pricing/PricingEventPublisher.cs` | Publishes `PriceChangedIntegrationEvent` after `SaveChangesAsync`. Matches `order` and `basket` pattern (direct publish from command handler). | ✅ Correct — no change. |
| **Endpoints** | `Pricing.Host/Endpoints/Pricing/*.cs` | All endpoints from the design spec table are implemented: `GET /prices/resolve`, `POST /price-lists`, `GET /price-lists`, `GET /price-lists/{id}`, `PUT /price-lists/{id}`, `POST /price-lists/{id}/activate`, `POST /price-lists/{id}/archive`, `PUT /price-lists/{id}/prices/{productId}`, `DELETE /price-lists/{id}/prices/{productId}`, `PUT /exchange-rates`, `GET /exchange-rates`, `DELETE /exchange-rates/{from}/{to}`. | ✅ Correct — no change. |
| **Public gateway routing** | `src/services/gateway/public/appsettings.json` | Only `order` routes/clusters exist. Pricing endpoints are not reachable from the public BFF. | ❌ Confirmed gap — fix. |
| **Tests** | `tests/unit/Pricing.UnitTests/`, `tests/architecture/`, `tests/integration/Pricing.IntegrationTests/` | 51 + 6 + 4 tests pass. Domain, handler, resolution, integration covered. No gateway routing test. | ⚠️ Add focused regression test for gateway routes. |
| **Plan status** | `docs/superpowers/plans/services/pricing.md` line 3 | Still marked "🆕 new". | 📝 Update to complete. |

## 4. Intended change boundaries

The completion work is bounded to the following confirmed, additive changes:

1. **Public gateway YARP routes** — add pricing routes and cluster to `src/services/gateway/public/appsettings.json` (and `appsettings.Development.json` if it differs). No other gateway code changes.
2. **Gateway regression test** — add one focused test in `tests/integration/Gateway.Public.IntegrationTests/GatewayFlowTests.cs` that exercises a pricing route through the gateway edge pipeline and confirms tenant/db-strategy/token headers are forwarded.
3. **Plan status update** — edit `docs/superpowers/plans/services/pricing.md` line 3 to reflect completion and remove the "scope brief" language on line 6.

No changes to Pricing source, tests, migrations, SharedKernel, Aspire host, or solution file.

## 5. Contract choices — `PriceChanged` monetary payload consistency

### Decision: keep `Amount` and `Currency` as separate fields

Both the domain event and the integration event use `decimal Amount` plus `string Currency` as two separate fields. The audit confirms this is the correct choice for the current contract.

**Reasoning:**
1. **Approved design contract.** The design spec (`2026-07-05-pricing-service-design.md` §Shared contract) explicitly defines `PriceChangedIntegrationEvent` as carrying `Amount (decimal)` and `Currency (string)`. The work-package brief (`pricing.md` line 17) lists the event as `PriceChanged (productId, priceListId, new amount, currency, effective date)`. Separate fields are the documented shape.
2. **No shared `Money` DTO.** `SharedKernel.Events` does not contain a `Money` value object, and `grep` for `Money` in `src/shared/` returns nothing. Introducing a new shared type would force a rebuild of every service and is not required by the approved design.
3. **Consistency with sibling events.** `OrderPlacedIntegrationEvent`, `OrderPlacedLine`, and `BasketCheckedOutLine` also carry monetary values as flat `decimal` fields (without currency). Pricing is the only commerce service that *must* include currency because it is multi-currency; the extra `Currency` field is therefore a domain-specific addition, not an inconsistency.
4. **ChangeType serialization.** The domain event uses enum `PriceChangeType`; the integration event uses `string ChangeType`. This matches the approved design and avoids coupling consumers to the pricing domain assembly.
5. **Additive emission.** The brief and design both state that `PriceChanged` emission is additive — no consumer exists today. Keeping the contract stable avoids future churn when basket/search/catalog subscribe.

**Boundary:** do not change the `PriceChangedIntegrationEvent` shape or add a `Money` DTO to `SharedKernel.Events` as part of this audit.

## 6. Focused test strategy

Existing coverage is strong and passing. Only one focused regression test is required.

### Existing test evidence
- `tests/unit/Pricing.UnitTests/PriceResolutionServiceTests.cs` — covers no-candidates, draft exclusion, most-specific scope, native-currency preference, quantity tiers, incompatible scope.
- `tests/unit/Pricing.UnitTests/PriceListTests.cs` — covers draft vs active emission, archive emission, currency mismatch, tier ordering, validity window.
- `tests/unit/Pricing.UnitTests/PriceTests.cs` — covers tier selection and currency validation.
- `tests/unit/Pricing.UnitTests/ResolvePriceHandlerTests.cs` — covers native, not-found, no FX rate, and converted cross-currency paths.
- `tests/unit/Pricing.UnitTests/PricingEventPublisherTests.cs` — covers `PriceChanged` → `PriceChangedIntegrationEvent` mapping.
- `tests/unit/Pricing.UnitTests/PricingDbContextTests.cs` — covers EF model build and owned-type round-trip.
- `tests/integration/Pricing.IntegrationTests/PriceResolutionTests.cs` — end-to-end native + cross-currency resolve against Testcontainers Postgres.
- `tests/integration/Pricing.IntegrationTests/ErrorPathTests.cs` — soft-deleted rate re-add, non-ascending tier validation edge.
- `tests/architecture/Pricing.Architecture.UnitTests/PricingArchitectureTests.cs` — layer rules, tenant-scoped aggregates, repository abstractions, endpoint derivation, shared rules via `AssertAll`.

### Regression test to add
**Location:** `tests/integration/Gateway.Public.IntegrationTests/GatewayFlowTests.cs`

Add a new fact that:
1. Uses the existing `GatewayFixture` (mock auth, fake token exchange, fake DB strategy, in-memory echo upstream).
2. Sends an authenticated request to a pricing route, e.g. `GET /prices/resolve?productId=<guid>&currency=USD&quantity=1`.
3. Asserts the response is `200 OK` (echoed by the upstream test server) and that the forwarded request carries `X-TenantId`, `X-Tenant-DbStrategy`, and `Authorization: Bearer <exchanged-token>`.

This mirrors the existing `AuthenticatedRequest_ForwardsTenantAndDbStrategyAndExchangedBearer` test for `/orders/123` but targets the new pricing cluster. Because the test fixture currently routes all cluster forwards to the same echo handler, the test can be added without changing the fixture infrastructure.

**Rationale:** The only confirmed gap is public-gateway routing. A single gateway-level regression test is sufficient to prove the routes are wired correctly and the edge pipeline forwards the trusted headers. Pricing-domain behavior is already covered by its own integration tests.

## 7. Update-to-plan criteria

After the confirmed fixes land, update `docs/superpowers/plans/services/pricing.md`:

- **Line 3:** change `Status: 🆕 new` to `Status: ✅ complete` (or the conventional completed marker used by sibling plans).
- **Line 4:** keep the parallelism note or remove it if no longer relevant; the service is independent and implemented.
- **Line 6:** remove or rewrite the "This is a scope brief, not a finished plan" sentence; replace with a pointer to the approved design spec and a note that the implementation is in `src/services/commerce/pricing/`.
- **Lines 17–30 (Events / API / Dependencies / Shared-file touchpoints):** keep as-is, or add a note that the public gateway routes are the final touchpoint added during completion.

The update is a single-file, non-code change that resolves the stale status.

## 8. Risks and assumptions

### Risks
- **Gateway route ordering:** pricing routes share prefix `/prices/` with other potential services. The new routes must be added with the same catch-all pattern used by the order cluster to avoid precedence conflicts. Route IDs should follow the existing `order-read` / `order-write` naming convention (e.g., `pricing-read`, `pricing-write`).
- **Cluster destination naming:** the pricing cluster destination must match the Aspire service name (`pricing`) so service discovery resolves correctly in `appsettings.json` (`"Address": "http://pricing"`).
- **Development vs production config:** if `appsettings.Development.json` overrides routes, both files must be updated to keep local and deployed behavior consistent.

### Assumptions
- The public gateway is the only ingress that needs pricing routes today (admin gateway is out of scope).
- The existing `ErrorOr` → `200`/null-body behavior for `ResolvePrice` not-found and `GetPriceList` not-found remains acceptable as a platform limitation; fixing it is platform-wide and not part of this completion.
- The `ExchangeRateProviderStub` returning empty rates is acceptable for v1; real FX adapter remains deferred per the design spec.
- No consumer of `PriceChangedIntegrationEvent` exists yet, so the contract can remain stable.

## 9. Success criteria

1. This audit design document is approved and stored at `docs/superpowers/specs/2026-07-23-pricing-audit-design.md`.
2. The confirmed gap (public gateway routing) is fixed in `src/services/gateway/public/appsettings.json` (and matching Development config if present).
3. A focused gateway regression test is added and passes.
4. All pricing tests continue to pass (`dotnet test` for unit, architecture, and integration projects).
5. The public gateway tests continue to pass.
6. `docs/superpowers/plans/services/pricing.md` status is updated to complete.
7. No new pricing features are introduced and no service rewrite occurs.

---

**Self-check:** The audit is grounded in the actual `pricing.md` plan, the approved `2026-07-05-pricing-service-design.md` spec, and the existing source/tests. The only confirmed fix is additive public-gateway routing plus its regression test; the `PriceChanged` monetary payload is confirmed consistent with the approved contract and is left unchanged. The plan-status update is explicitly bounded.
