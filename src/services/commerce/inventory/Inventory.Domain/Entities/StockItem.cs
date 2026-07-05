using SharedKernel.Core.Domain;

namespace Inventories.Domain.Entities;

/// <summary>
/// Represents the stock aggregate root for a single product at a single location: units on hand,
/// units reserved, and the backorder/reorder policy governing availability.
/// </summary>
public sealed class StockItem : BaseEntity, IAggregateRoot, ITenantScoped
{
    private StockItem()
    {
    }

    /// <summary>Gets the identifier of the product this stock record tracks.</summary>
    public Guid ProductId { get; private set; }

    /// <summary>Gets the identifier of the location this stock record tracks.</summary>
    public Guid LocationId { get; private set; }

    /// <inheritdoc/>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets the quantity currently on hand at the location.</summary>
    public int QuantityOnHand { get; private set; }

    /// <summary>Gets the quantity currently reserved against on-hand stock.</summary>
    public int QuantityReserved { get; private set; }

    /// <summary>Gets a value indicating whether reservations may exceed available stock.</summary>
    public bool AllowBackorder { get; private set; }

    /// <summary>Gets the available-quantity threshold at or below which a reorder is triggered.</summary>
    public int ReorderThreshold { get; private set; }

    /// <summary>Gets the row version used for optimistic concurrency (mapped to <c>xmin</c>).</summary>
    public uint RowVersion { get; private set; }

    /// <summary>Gets the quantity available to promise (on hand minus reserved).</summary>
    public int Available => QuantityOnHand - QuantityReserved;

    /// <summary>Creates a new stock item for a product at a location.</summary>
    /// <param name="productId">The product identifier.</param>
    /// <param name="locationId">The location identifier.</param>
    /// <param name="tenantId">The owning tenant identifier.</param>
    /// <param name="quantityOnHand">The initial quantity on hand.</param>
    /// <param name="allowBackorder">Whether reservations may exceed available stock.</param>
    /// <param name="reorderThreshold">The available-quantity threshold that triggers a reorder.</param>
    /// <returns>The new stock item.</returns>
    public static StockItem Create(
        Guid productId,
        Guid locationId,
        string tenantId,
        int quantityOnHand,
        bool allowBackorder,
        int reorderThreshold) => new()
        {
            ProductId = productId,
            LocationId = locationId,
            TenantId = tenantId,
            QuantityOnHand = quantityOnHand,
            AllowBackorder = allowBackorder,
            ReorderThreshold = reorderThreshold,
        };

    /// <summary>Receives incoming stock, increasing the quantity on hand.</summary>
    /// <param name="quantity">The quantity received (must be positive).</param>
    public void Receive(int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
        }

        QuantityOnHand += quantity;
    }

    /// <summary>Adjusts the quantity on hand by a positive or negative delta.</summary>
    /// <param name="delta">The signed adjustment to apply to the quantity on hand.</param>
    public void Adjust(int delta)
    {
        int updated = QuantityOnHand + delta;
        if (updated < 0)
        {
            throw new InvalidOperationException("Quantity on hand cannot go negative.");
        }

        QuantityOnHand = updated;
    }

    /// <summary>Reserves stock against available quantity, or against on-hand if backorder is allowed.</summary>
    /// <param name="quantity">The quantity to reserve (must be positive).</param>
    public void Reserve(int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
        }

        if (quantity > Available && !AllowBackorder)
        {
            throw new InvalidOperationException("Cannot reserve more than the available quantity.");
        }

        QuantityReserved += quantity;
    }

    /// <summary>Releases a previously reserved quantity, clamping at zero.</summary>
    /// <param name="quantity">The quantity to release (must be positive).</param>
    public void Release(int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
        }

        QuantityReserved = Math.Max(0, QuantityReserved - quantity);
    }

    /// <summary>Updates the backorder and reorder-threshold policy for this stock item.</summary>
    /// <param name="allowBackorder">Whether reservations may exceed available stock.</param>
    /// <param name="reorderThreshold">The available-quantity threshold that triggers a reorder.</param>
    public void SetPolicy(bool allowBackorder, int reorderThreshold)
    {
        AllowBackorder = allowBackorder;
        ReorderThreshold = reorderThreshold;
    }

    /// <summary>Determines whether available quantity has crossed at or below the reorder threshold.</summary>
    /// <returns><see langword="true"/> if a reorder should be triggered; otherwise, <see langword="false"/>.</returns>
    public bool CrossedReorderThreshold() => Available <= ReorderThreshold;

    /// <summary>Determines whether available quantity has been depleted.</summary>
    /// <returns><see langword="true"/> if available quantity is zero or negative; otherwise, <see langword="false"/>.</returns>
    public bool IsDepleted() => Available <= 0;
}
