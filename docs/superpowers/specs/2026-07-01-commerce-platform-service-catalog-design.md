# Commerce Platform — Target Service Catalog & Roadmap

**Date:** 2026-07-01
**Status:** Approved (roadmap/reference — not directly buildable; each service gets its own spec)
**Scope:** Defines the full target set of microservices for a feature-rich, commercetools/Shopify-class multi-tenant commerce platform, mapped onto the existing nx release groups, tiered by build order.

## Purpose

This is the **enduring roadmap** for expanding the commerce platform from its current early-scaffolding state (only `order` is a complete reference service) into a broad platform covering B2C, marketplace, and omnichannel retail. It is intentionally not a single implementation plan — it is the map. Each service is built through its own `brainstorm → spec → plan → implement` cycle, in the tier order below.

The first service to be built (`basket`) has its own spec: `2026-07-01-basket-service-design.md`.

## Commerce model

**Broad platform (superset):** B2C storefront + multi-vendor marketplace + omnichannel/physical retail. Because this is the superset, services are **tiered hard** by build order so the platform is coherent and shippable at every tier boundary — the transactional spine first, reach later.

## Boundary decisions

1. **Retire the standalone `product` placeholder.** Its responsibilities split cleanly: product *master data* → `catalog`; *stock* → a new `inventory` service. One service = one bounded context.
2. **Split pricing / promotion / tax / inventory into their own services** (commercetools-style) rather than folding them into `catalog`/`order`. Each is a distinct bounded context with its own rules engine and release cadence. Approved granularity: keep them separate.
3. **Two new nx release groups:** `engagement` (review, wishlist, loyalty, recommendation, subscription, marketing) and `marketplace` (vendor) — so growth/marketplace services version independently from core commerce.
4. **Cross-service integration event contracts live in `SharedKernel.Events`** (shared contracts assembly, already referenced by service Application/Host projects). This honors "all sharing flows through `src/shared`" and lets a consumer subscribe to an event without referencing the publishing service. (Note: `OrderPlacedIntegrationEvent` currently lives in `Order.Application`; migrating genuinely-cross-service contracts into `SharedKernel.Events` is a follow-up, out of scope for any single service build.)

## Status legend

- ✅ real (complete `.csproj` reference service)
- 🟡 skeleton (partial projects exist)
- 📁 placeholder (empty directory, documented intent)
- 🆕 new (does not exist yet)

## The catalog

### Tier 0 — Transactional spine (the platform is non-functional without these)

| Service | Group | Status | Owns | Emits | Consumes |
|---|---|---|---|---|---|
| catalog | commerce | 🟡 skeleton | Products, variants, categories, attributes | `ProductPriceChanged` | — |
| basket | commerce | 📁 placeholder | Cart, line items, checkout session | `BasketCheckedOut` | `ProductPriceChanged`, `OrderPlaced` |
| order | commerce | ✅ real | Order lifecycle, fulfillment orchestration | `OrderPlaced`, `OrderShipped` | `BasketCheckedOut`, `CustomerCreated` |
| customer | commerce | 🟡 skeleton | Tenant authority + customer profiles, addresses, groups | `CustomerCreated` | — |
| inventory | commerce | 🆕 new | Stock levels, reservations, availability | `StockReserved`, `StockDepleted` | `OrderPlaced`, `BasketCheckedOut` |
| pricing | commerce | 🆕 new | Price lists, currencies, channel/customer-group prices | `PriceChanged` | — |
| billing | operations | 📁 placeholder | Payments, invoicing, payment methods | `PaymentCaptured`, `PaymentFailed` | `OrderPlaced` |

### Tier 1 — Commerce completeness (a "real" store)

| Service | Group | Status | Owns | Emits | Consumes |
|---|---|---|---|---|---|
| promotion | commerce | 🆕 new | Coupons, cart/product discounts, campaigns | `PromotionApplied` | `BasketCheckedOut` |
| shipping | commerce | 🆕 new | Shipping methods, rates, carrier + delivery tracking | `ShipmentDispatched`, `ShipmentDelivered` | `OrderPlaced` |
| tax | commerce | 🆕 new | Tax categories, rate calculation, provider integration | — | — |
| returns | commerce | 🆕 new | RMA, refunds, return lifecycle | `ReturnRequested`, `RefundIssued` | `OrderShipped` |
| notification | operations | 🆕 new | Email/SMS/push, templates, preferences | — | (broad — order/payment/shipment events) |

### Tier 2 — Discovery & engagement (conversion + retention)

| Service | Group | Status | Owns | Emits | Consumes |
|---|---|---|---|---|---|
| search | operations | 🆕 new | Product search index, facets (OpenSearch/Elastic) | — | `ProductPriceChanged`, catalog events |
| review | engagement 🆕 | 🆕 new | Ratings, reviews, moderation | `ReviewPublished` | `OrderShipped` |
| wishlist | engagement 🆕 | 🆕 new | Shopping lists, favorites | — | — |
| loyalty | engagement 🆕 | 🆕 new | Points, tiers, gift cards, rewards | `PointsAwarded` | `OrderPlaced`, `PaymentCaptured` |
| recommendation | engagement 🆕 | 🆕 new | Related products, personalization | — | order/catalog events |

### Tier 3 — Platform reach (omnichannel · marketplace · growth)

| Service | Group | Status | Owns | Emits | Consumes |
|---|---|---|---|---|---|
| location | operations | 📁 placeholder | Stores, warehouses, geocoding | — | — |
| device | operations | 📁 placeholder | POS / IoT devices, vendor sync (+VendorWorker) | — | — |
| statistic | operations | 📁 placeholder | Analytics, reporting, real-time metrics | — | (broad — most events) |
| vendor | marketplace 🆕 | 🆕 new | Seller onboarding, per-vendor catalogs, commission/payouts | `VendorPayoutDue` | `OrderPlaced`, `PaymentCaptured` |
| subscription | engagement 🆕 | 🆕 new | Recurring orders & billing | `SubscriptionRenewed` | `PaymentCaptured` |
| cms | content | 🆕 new | Content pages, blocks, blog | — | — |
| marketing | engagement 🆕 | 🆕 new | Campaigns, cart-abandonment, email marketing | — | `BasketCheckedOut` (abandonment), order events |
| image-generator | content | ✅ real | AI product imagery | — | — |

## Summary

- **~24 services**, ~13 net-new.
- New release groups: `engagement`, `marketplace` (added to `nx.json` alongside existing `commerce`, `operations`, `content`, `gateway`).
- Every service follows the standard clean-architecture structure (`src/services/AGENTS.md`) and the repository/UnitOfWork + CQRS three-context conventions.
- Integration events are the async seams between services; the shared contracts live in `SharedKernel.Events`.

## Build order

Build strictly by tier. Within Tier 0, the recommended order is **basket → inventory → pricing → catalog (complete) → customer (complete) → billing**, because `basket` closes a demonstrable cross-service loop with the already-complete `order` service (checkout → order via `BasketCheckedOut`). Each service is its own spec + plan + implementation cycle.

## Out of scope (platform-level, revisit later)

- Identity is Keycloak (external) — not modeled as a domain service.
- `audit` / activity-log service — `SharedKernel` already emits `AuditEvent`; a dedicated consumer service is deferred until there is a consumer need.
- `fraud` / risk service — deferred; would consume order/payment events when marketplace scale warrants it.
