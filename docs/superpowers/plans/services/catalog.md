# Work Package: `catalog` service

**Group:** commerce · **Tier:** 0 · **Status:** 🟡 skeleton → complete · **Branch:** `worktree-catalog-service`
**⚠️ Already in progress** in an existing `worktree-catalog-service` session — coordinate before starting; don't double-build.

> Scope brief, not a finished plan. This service has partial projects already — complete it, mirroring **order**/**basket** and honoring whatever the in-progress session has landed. Read `src/services/AGENTS.md` and `COORDINATION.md` first.

## Bounded context
Owns **product master data**: products, variants, categories, attributes. NOT price (→ pricing) and NOT stock (→ inventory). One product's descriptive identity.

## Domain (starting shape)
- `Product` (aggregate root, `ITenantScoped`): sku, name, description, category refs, attribute set, publish status.
- `Variant` (entity/owned): option values (size/color), sku.
- `Category` (aggregate or entity): tree/hierarchy, slug.
- `Attribute` / `AttributeValue`: typed product attributes.
- Smart enums: `ProductStatus` (Draft/Published/Archived).

## Events
- **Emits:** catalog master-data events (`ProductCreated`/`ProductUpdated`/`ProductPublished`, and per the roadmap `ProductPriceChanged` — but with pricing split out, prefer emitting product-identity events here and leave price events to **pricing**; settle this boundary in your spec). **catalog owns these contracts.** Consumers: search, basket (product name snapshot), recommendation.
- **Consumes:** none.

## API surface (indicative)
- `GET /products`, `GET /products/{id}`, category browse, attribute queries (read-heavy — Specifications + read context).
- Product/category/attribute CRUD (authenticated, tenant-scoped).

## Dependencies & ordering
Independent — start/continue now.

## Shared-file touchpoints
`.slnx`, `AppHost.csproj`/`AppHost.cs` (catalog resource may already exist — it's referenced in `AppHost.cs`), `SharedKernel.Events/*` (new product event files). No `nx.json` change.

## Watch-items
- Big read surface — invest in Specifications and a clean read model; this is the service most likely to want feature-level file splits if one session isn't enough.
- Category hierarchy: decide adjacency-list vs materialized-path early (affects queries + EF config).
- Coordinate the price-event boundary with the pricing session so `ProductPriceChanged` vs `PriceChanged` isn't defined twice.
