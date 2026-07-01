using Baskets.Domain.ValueObjects;

namespace Baskets.Domain.Services;

/// <summary>
/// Provides pricing calculations for baskets.
/// </summary>
public static class BasketPricingService
{
    /// <summary>
    /// Calculates the subtotal for the specified basket items.
    /// </summary>
    /// <param name="items">The basket items to total.</param>
    /// <returns>The sum of all line totals.</returns>
    public static decimal CalculateSubtotal(IEnumerable<BasketItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        decimal subtotal = 0;

        foreach (BasketItem item in items)
        {
            if (item.Quantity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(items), "Basket item quantity cannot be negative.");
            }

            if (item.UnitPrice < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(items), "Basket item unit price cannot be negative.");
            }

            subtotal += item.LineTotal;
        }

        return subtotal;
    }
}
