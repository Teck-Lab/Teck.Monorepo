using Inventories.Application.Inventory.ReadModels;
using Inventories.Domain.Entities;
using Inventories.Domain.Services;
using Inventories.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Core.Database;
using SharedKernel.Events;

namespace Inventories.Application.Inventory.Features.CommitReservation.V1;

/// <summary>
/// Holds the load → allocate → reserve → save loop for committing a stock reservation, together
/// with the optimistic-concurrency retry that makes it correct under contention. Kept as a
/// reusable application service so both the <c>OrderPlaced</c> commit consumer and (later) the
/// basket-hold consumer share exactly one copy of this correctness-critical logic.
/// </summary>
internal static class ReservationCommitter
{
    /// <summary>
    /// Attempts to commit the requested reservation, retrying on optimistic-concurrency conflicts.
    /// </summary>
    /// <remarks>
    /// The first attempt runs against the caller's ambient repositories/unit of work. On a
    /// <see cref="DbUpdateConcurrencyException"/> the ambient DbContext is poisoned — its change
    /// tracker now holds stock items that were already mutated by <see cref="StockItem.Reserve(int)"/>
    /// and a failed save. Reloading within that same context would return those stale, mutated
    /// instances (EF's identity map wins over the database for tracking queries), so re-running would
    /// re-apply the reserve on top of already-reserved quantities and double-count. Each retry
    /// therefore runs in a brand-new DI scope, giving a fresh DbContext with an empty identity map
    /// that observes the true committed database state. The retry budget is
    /// <see cref="InventoryOptions.MaxReserveRetries"/>.
    /// </remarks>
    /// <param name="stockItems">The stock write repository for the first (ambient) attempt.</param>
    /// <param name="reservations">The reservation write repository for the first (ambient) attempt.</param>
    /// <param name="locationPriorities">The location-priority read repository for the first (ambient) attempt.</param>
    /// <param name="unitOfWork">The unit of work for the first (ambient) attempt.</param>
    /// <param name="scopeFactory">Factory used to open a fresh scope (fresh DbContext) per retry.</param>
    /// <param name="maxRetries">The maximum number of retries after the first attempt.</param>
    /// <param name="request">The reservation to commit.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The commit result describing what happened and what to publish.</returns>
    public static Task<ReservationCommitResult> CommitAsync(
        IGenericWriteRepository<StockItem, Guid> stockItems,
        IGenericWriteRepository<Reservation, Guid> reservations,
        IGenericReadRepository<LocationPriority, Guid> locationPriorities,
        IUnitOfWork unitOfWork,
        IServiceScopeFactory scopeFactory,
        int maxRetries,
        ReservationCommitRequest request,
        CancellationToken ct) =>
        ExecuteAsync(stockItems, reservations, locationPriorities, unitOfWork, scopeFactory, maxRetries, request, expiresAt: null, ct);

    /// <summary>
    /// Attempts to place a <see cref="ReservationStatus.Held"/> reservation that expires at
    /// <paramref name="expiresAt"/> unless committed first, retrying on optimistic-concurrency
    /// conflicts exactly as <see cref="CommitAsync"/> does.
    /// </summary>
    /// <remarks>See <see cref="CommitAsync"/> for the retry rationale, which applies unchanged here.</remarks>
    /// <param name="stockItems">The stock write repository for the first (ambient) attempt.</param>
    /// <param name="reservations">The reservation write repository for the first (ambient) attempt.</param>
    /// <param name="locationPriorities">The location-priority read repository for the first (ambient) attempt.</param>
    /// <param name="unitOfWork">The unit of work for the first (ambient) attempt.</param>
    /// <param name="scopeFactory">Factory used to open a fresh scope (fresh DbContext) per retry.</param>
    /// <param name="maxRetries">The maximum number of retries after the first attempt.</param>
    /// <param name="request">The reservation to hold.</param>
    /// <param name="expiresAt">The point in time at which the hold expires unless committed first.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The commit result describing what happened and what to publish.</returns>
    public static Task<ReservationCommitResult> HoldForAsync(
        IGenericWriteRepository<StockItem, Guid> stockItems,
        IGenericWriteRepository<Reservation, Guid> reservations,
        IGenericReadRepository<LocationPriority, Guid> locationPriorities,
        IUnitOfWork unitOfWork,
        IServiceScopeFactory scopeFactory,
        int maxRetries,
        ReservationCommitRequest request,
        DateTimeOffset expiresAt,
        CancellationToken ct) =>
        ExecuteAsync(stockItems, reservations, locationPriorities, unitOfWork, scopeFactory, maxRetries, request, expiresAt, ct);

    private static async Task<ReservationCommitResult> ExecuteAsync(
        IGenericWriteRepository<StockItem, Guid> stockItems,
        IGenericWriteRepository<Reservation, Guid> reservations,
        IGenericReadRepository<LocationPriority, Guid> locationPriorities,
        IUnitOfWork unitOfWork,
        IServiceScopeFactory scopeFactory,
        int maxRetries,
        ReservationCommitRequest request,
        DateTimeOffset? expiresAt,
        CancellationToken ct)
    {
        try
        {
            return await AttemptAsync(stockItems, reservations, locationPriorities, unitOfWork, request, expiresAt, ct)
                .ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Fall through to fresh-scope retries below.
        }

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            // A brand-new scope hands out a fresh DbContext (empty identity map) so the reload sees
            // the true committed database state rather than the previous attempt's mutated entities.
            using IServiceScope scope = scopeFactory.CreateScope();
            IServiceProvider sp = scope.ServiceProvider;
            try
            {
                return await AttemptAsync(
                    sp.GetRequiredService<IGenericWriteRepository<StockItem, Guid>>(),
                    sp.GetRequiredService<IGenericWriteRepository<Reservation, Guid>>(),
                    sp.GetRequiredService<IGenericReadRepository<LocationPriority, Guid>>(),
                    sp.GetRequiredService<IUnitOfWork>(),
                    request,
                    expiresAt,
                    ct).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Retry in the next fresh scope until the budget is exhausted.
            }
        }

        return ReservationCommitResult.Contention(ToLines(request.Lines));
    }

    private static async Task<ReservationCommitResult> AttemptAsync(
        IGenericWriteRepository<StockItem, Guid> stockItems,
        IGenericWriteRepository<Reservation, Guid> reservations,
        IGenericReadRepository<LocationPriority, Guid> locationPriorities,
        IUnitOfWork unitOfWork,
        ReservationCommitRequest request,
        DateTimeOffset? expiresAt,
        CancellationToken ct)
    {
        // 1. Idempotency FIRST: a matching reservation means this source has already been committed.
        Reservation? existing = await reservations
            .FirstOrDefaultAsync(new ReservationBySourceSpec(request.Source, request.SourceId), ct)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return ReservationCommitResult.AlreadyReserved(existing.Id);
        }

        // 2. Load the tenant's ordered location preference (null → deterministic fallback ordering).
        LocationPriority? priority = await locationPriorities
            .FirstOrDefaultAsync(new LocationPriorityByTenantSpec(request.TenantId), ct)
            .ConfigureAwait(false);
        IReadOnlyList<Guid> priorityOrder = priority?.LocationIds ?? [];

        // 3. Allocate every line first (pure, no mutation) so all-or-nothing can be decided before
        //    any stock is touched.
        var plans = new List<(List<StockItem> Ordered, AllocationResult Allocation, ReservationRequestLine Line)>();
        var failing = new List<StockReservationLine>();
        foreach (ReservationRequestLine line in request.Lines)
        {
            IReadOnlyList<StockItem> items = await stockItems
                .ListAsync(new StockItemsByProductForTenantSpec(line.ProductId, request.TenantId), enableTracking: true, ct)
                .ConfigureAwait(false);
            List<StockItem> ordered = OrderByPriority(items, priorityOrder);
            AllocationResult allocation = StockAllocator.Allocate(ordered, line.Quantity);
            if (!allocation.Satisfied)
            {
                failing.Add(new StockReservationLine(line.ProductId, line.Quantity, 0));
            }

            plans.Add((ordered, allocation, line));
        }

        // 4. All-or-nothing: if any line is unsatisfiable, mutate nothing and reject.
        if (failing.Count > 0)
        {
            return ReservationCommitResult.Rejected(failing);
        }

        // 5. Apply the reserves and build the reservation. A single SaveChanges is the only commit.
        var reservationLines = new List<ReservationLine>();
        var affected = new Dictionary<Guid, StockItem>();
        var preState = new Dictionary<Guid, (bool Depleted, bool BelowReorder)>();

        void Track(StockItem item)
        {
            if (affected.TryAdd(item.Id, item))
            {
                // Capture the pre-reserve state so post-commit we only publish on the crossing.
                preState[item.Id] = (item.IsDepleted(), item.CrossedReorderThreshold());
            }
        }

        foreach ((List<StockItem> ordered, AllocationResult allocation, ReservationRequestLine line) in plans)
        {
            Dictionary<Guid, StockItem> byLocation = ordered.ToDictionary(item => item.LocationId);
            foreach (Allocation drawn in allocation.Allocations)
            {
                StockItem item = byLocation[drawn.LocationId];
                Track(item);
                item.Reserve(drawn.Quantity);
            }

            if (allocation.BackorderedQuantity > 0)
            {
                // The lowest-priority item absorbs the backordered remainder (it is the only item the
                // allocator lets back-order); its Reserve permits exceeding available. Restocking the
                // backorder is handled later (Task 19), not here.
                StockItem tail = ordered[^1];
                Track(tail);
                tail.Reserve(allocation.BackorderedQuantity);
            }

            reservationLines.Add(new ReservationLine(line.ProductId, line.Quantity, allocation.BackorderedQuantity, allocation.Allocations));
        }

        Reservation reservation = expiresAt is null
            ? Reservation.CreateCommitted(request.Source, request.SourceId, request.TenantId, reservationLines)
            : Reservation.CreateHeld(request.Source, request.SourceId, request.TenantId, expiresAt.Value, reservationLines);
        await reservations.AddAsync(reservation, ct).ConfigureAwait(false);

        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        var affectedSnapshots = affected.Values.Select(item =>
        {
            (bool wasDepleted, bool wasBelowReorder) = preState[item.Id];
            return new AffectedStock(
                item.ProductId,
                item.LocationId,
                item.TenantId,
                item.Available,
                item.ReorderThreshold,
                NewlyDepleted: !wasDepleted && item.IsDepleted(),
                NewlyReorderTriggered: !wasBelowReorder && item.CrossedReorderThreshold());
        }).ToList();

        var committedLines = reservationLines
            .Select(line => new StockReservationLine(line.ProductId, line.RequestedQuantity, line.BackorderedQuantity))
            .ToList();

        return ReservationCommitResult.Committed(reservation.Id, committedLines, affectedSnapshots);
    }

    private static List<StockItem> OrderByPriority(IReadOnlyList<StockItem> items, IReadOnlyList<Guid> priorityOrder)
    {
        var rank = new Dictionary<Guid, int>();
        for (int index = 0; index < priorityOrder.Count; index++)
        {
            rank[priorityOrder[index]] = index;
        }

        // Priority-listed locations first (in their configured order); everything else falls back to
        // a deterministic ordering by location id so allocation is stable regardless of load order.
        return items
            .OrderBy(item => rank.TryGetValue(item.LocationId, out int index) ? index : int.MaxValue)
            .ThenBy(item => item.LocationId)
            .ToList();
    }

    private static IReadOnlyList<StockReservationLine> ToLines(IReadOnlyList<ReservationRequestLine> lines) =>
        lines.Select(line => new StockReservationLine(line.ProductId, line.Quantity, 0)).ToList();
}
