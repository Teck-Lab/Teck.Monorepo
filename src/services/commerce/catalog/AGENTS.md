# Catalog Service

## Overview
Owns product master data and sourcing: products, variants, categories, sell
prices, suppliers, and supplier cost prices. Catalog never holds stock counts —
inventory is a separate (future) service. See the design spec at
`docs/superpowers/specs/2026-06-23-catalog-service-design.md`.

## Capabilities
- Products (products, variants, categories, sell pricing)
- Suppliers (suppliers, variant↔supplier links, supplier price history)

## Events
- Emits: `ProductPriceChanged` (variant sell price), `ProductCreated`, `VariantCreated`
- Consumes: none (v1)

## Database
- PostgreSQL
- EF Core migrations in-app (Plan 3)

## Dependencies
- SharedKernel.*
- Teck.Cloud.ServiceDefaults

## Conventions
Follow `src/services/AGENTS.md` and `src/services/commerce/AGENTS.md`. Mirror the
`order` service structure.
