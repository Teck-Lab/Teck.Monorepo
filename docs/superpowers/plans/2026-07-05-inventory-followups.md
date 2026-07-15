# Inventory service — deferred follow-ups (from final review)

**Date:** 2026-07-05
**Context:** The whole-branch review returned **SHIP WITH FIXES** and confirmed the core no-oversell guarantee is correct. The one Important finding (backordered quantity double-counted as reserved stock — commit-time `tail.Reserve` never released, re-reserved on fill) was **fixed on this branch** (commit `c7ce6c5`) with a regression test; that single root-cause fix also resolved the related expiry-sweep leak. The items below are Minor/latent and are deferred.

## Minor (latent / self-healing — ticket)

1. **Idempotency unique-violation is not caught.** On concurrent re-delivery of the *same* source, both `ReservationCommitter.AttemptAsync` calls pass the `ReservationBySourceSpec` pre-check and both insert; the loser hits the unique index `(TenantId, SourceType, SourceId)` → `DbUpdateException` (NOT `DbUpdateConcurrencyException`), which the retry loop does not catch, so it propagates to Wolverine retry/dead-letter. Self-heals on redelivery (pre-check returns `AlreadyReserved`) and never double-reserves (the failed save rolls back), but it's a noisy error rather than a clean no-op. Fix: catch the unique-constraint violation and map it to `AlreadyReserved`.

2. **Duplicate product line in one order/basket hard-fails.** `AttemptAsync` computes all line allocations against unmutated availability before applying any reserve, so two lines for the same product both see full `Available`, then over-draw at reserve time (`StockItem.Reserve` throws when `!AllowBackorder`, or the `ReservationLines` PK `(ReservationId, ProductId)` collides). Normal order/basket flows aggregate lines by product, so this is latent. Fix: aggregate request lines by `ProductId` before allocating (defensive).

3. **`ReservationBySourceSpec` omits `TenantId`.** The idempotency pre-check filters only `(SourceType, SourceId)` while the unique index is `(TenantId, SourceType, SourceId)`. Harmless today (source ids are globally-unique GUIDs; tenancy dormant), but align the spec with the index before tenant filters activate.

## Accepted design choices (documented — not defects)

- **JSON `Allocations` column** on `ReservationLines` (vs an owned table): chosen because `ReservationLine` is an immutable record (nested `OwnsMany` constructor-binding is disallowed); allocations are never queried independently; validated on real Postgres by the integration tests.
- **Mixed per-location `AllowBackorder`:** the allocator uses "tail (lowest-priority) location governs backorder-eligibility." Deterministic; only diverges under non-uniform per-location policy (a degenerate config — realistic uniform policy is identical).
- **Write-side lazy expiry is sweep-based, not instant.** The read path (`GetAvailability`) discounts lapsed holds immediately; the write/allocation path trusts stored `QuantityReserved` until the sweep releases it. Direction of error is always conservative (never oversells); bounded by `InventoryOptions.SweepInterval`. Precise instant write-side expiry is a future option.

## Platform-level (shared with basket's findings)

- **Multi-tenancy is dormant:** no commerce entity carries Finbuckle's `[MultiTenant]` attribute, so the tenant query filter + SaveChanges guard are no-ops platform-wide. Inventory's consumers and the background sweep pass `TenantId` explicitly and read via tenant-filtered specs, so they are correct today AND structured for activation — but when `[MultiTenant]` is switched on, the cross-tenant background sweep and the message-consumer commit path will each need a per-tenant scope. Tracked platform-wide (also affects order's `BasketCheckedOut` consumer).

## Cannot-verify-from-diff (a human should confirm)

- **Broker round-trip:** the concurrency test drives handlers in-process (`InvokeAsync`), not over RabbitMQ. An end-to-end test that publishes `OrderPlaced` from order and confirms inventory's consumer fires over the broker would close the loop (blocked today anyway — messaging transport is dormant platform-wide, same as basket).
