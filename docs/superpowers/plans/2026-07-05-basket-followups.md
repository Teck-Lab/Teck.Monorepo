# Basket service — deferred follow-ups (from final review)

**Date:** 2026-07-05
**Context:** The whole-branch review returned **SHIP WITH FIXES**. The three Important, basket-owned findings were fixed on this branch (guest-token CSPRNG `03d3ef8`; checkout publish + ownership `22bc470`). The items below are deferred — they are platform-level, latent, or minor — and should be ticketed rather than blocking the basket merge.

## Platform-level (not basket's call to make unilaterally)

1. **Wire messaging transport platform-wide so integration events actually deliver.**
   `CheckoutHandler` now *publishes* `BasketCheckedOutIntegrationEvent` (like order's `CreateOrderHandler` publishes `OrderPlacedIntegrationEvent`), but no service wires RabbitMQ transport / the Postgres outbox — `WolverinePersistenceConfigurator.ConfigureStandardRuntime` (which calls `UseRabbitMq` + `PersistMessagesWithPostgresql` + `PublishDomainEventsFromEntityFrameworkCore`) has **zero callers**. So today the publish goes to an untransported local bus and the order service never receives it. This matches CLAUDE.md ("Redis/RabbitMQ run but are not yet consumed"). The checkout→order loop is **publish-ready and consistent with order**, but not end-to-end functional until transport is enabled for all services. When it is, also remove order's now-latent domain-event double-publish (`OrderPlacedHandler` + direct publish in `CreateOrderHandler`).

2. **Harden the empty-tenant fallback** in `SharedKernel.Infrastructure/.../MultiTenantDbExtensions.cs` (added this branch, commit `a87856c`). `AddScoped<ITenantInfo>(... ?? new TenantDetails())` silently substitutes an empty tenant when resolution fails. Not an exploit today (tenancy is dormant platform-wide — no `UseMultiTenant` middleware/strategy, entities aren't `[MultiTenant]`, no query filters), but it will silently mask a misconfigured tenant once filters are live. Recommend logging (and optionally throwing outside Development) when the tenant is unresolved. Owner: shared-infra maintainer.

3. **Drop customer's now-redundant `AddMultiTenant<TenantDetails>()`** in `CustomerPersistenceExtensions.cs` — it is now also registered by the shared hybrid method (benign/idempotent, but a smell).

## Basket, latent (fine to ship, revisit when tenancy/concurrency go live)

4. **Tenant-scope the basket specs.** `ActiveBasketByCustomerSpec`/`ActiveBasketByTokenSpec`/`BasketByIdSpec` filter on owner/id + status but not `TenantId`. Latent only (mirrors platform state); add `.Where(b => b.TenantId == ...)` once tenant filters are enforced.

5. **Optimistic concurrency.** No `xmin`/row-version on `Basket`; concurrent `AddItem`/`UpdateItemQuantity` can lost-update, and concurrent get-or-create for a new customer can create two active baskets (the `(TenantId, CustomerId, Status)` index is non-unique). Add a row version and/or a filtered unique index on active baskets if concurrency matters.

## Minor / cleanup

6. **`BasketOptions` is dead config.** `MaxItemsPerBasket`/`MaxQuantityPerLine` are configured in `Program.cs` but never enforced (no upper bound on distinct lines or quantity — mild unbounded-growth risk). Either enforce them in the domain (`IOptions<BasketOptions>` into `AddItemHandler`/domain) or remove the unused options.

7. **`Basket.AssignToCustomer` is unused.** The merge flow merges items into a customer basket rather than reassigning the guest basket, leaving `AssignToCustomer` with no callers. Remove it or wire it into merge.

8. **Assert cross-service order creation in an integration test** once messaging transport (#1) is wired — the current basket integration test asserts basket-side state + that checkout returns 201, not that an order is created.
