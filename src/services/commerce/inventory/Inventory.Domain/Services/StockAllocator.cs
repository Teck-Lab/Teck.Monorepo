using Inventories.Domain.Entities;
using Inventories.Domain.ValueObjects;

namespace Inventories.Domain.Services;

/// <summary>
/// Computes a priority-ordered allocation plan for a requested quantity across a product's stock
/// items. This service is pure: it never mutates the supplied stock items; the caller applies the
/// resulting plan (e.g. by calling <see cref="StockItem.Reserve(int)"/>) once it decides to commit.
/// </summary>
public static class StockAllocator
{
    /// <summary>
    /// Allocates the requested quantity across the given stock items, filling from each in the
    /// supplied priority order. Each item contributes at most its <see cref="StockItem.Available"/>
    /// quantity from on-hand stock; if a shortfall remains after all items have been drawn from,
    /// the lowest-priority (last) item absorbs the remainder as a backorder if it allows one.
    /// </summary>
    /// <param name="itemsInPriorityOrder">The candidate stock items, ordered from highest to lowest allocation priority.</param>
    /// <param name="requestedQuantity">The quantity to allocate. Must not be negative.</param>
    /// <returns>
    /// The resulting allocation plan. <see cref="AllocationResult.Satisfied"/> is <see langword="false"/>
    /// only when the items' combined available stock is less than <paramref name="requestedQuantity"/>
    /// and the lowest-priority item does not allow backorder.
    /// </returns>
    public static AllocationResult Allocate(IReadOnlyList<StockItem> itemsInPriorityOrder, int requestedQuantity)
    {
        ArgumentNullException.ThrowIfNull(itemsInPriorityOrder);
        if (requestedQuantity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedQuantity), "Requested quantity cannot be negative.");
        }

        var allocations = new List<Allocation>();
        int remaining = requestedQuantity;

        foreach (StockItem item in itemsInPriorityOrder)
        {
            if (remaining <= 0)
            {
                break;
            }

            int take = Math.Min(remaining, Math.Max(0, item.Available));
            if (take > 0)
            {
                allocations.Add(new Allocation(item.LocationId, take));
                remaining -= take;
            }
        }

        if (remaining <= 0)
        {
            return new AllocationResult(true, allocations, 0);
        }

        bool tailAllowsBackorder = itemsInPriorityOrder.Count > 0 && itemsInPriorityOrder[^1].AllowBackorder;
        if (tailAllowsBackorder)
        {
            return new AllocationResult(true, allocations, remaining);
        }

        return new AllocationResult(false, [], 0);
    }
}
