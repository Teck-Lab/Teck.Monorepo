using Orders.Domain.ValueObjects;

namespace Orders.Domain.Services;

/// <summary>
/// Provides pricing calculations for orders.
/// </summary>
public static class OrderPricingService
{
    /// <summary>
    /// Calculates the total monetary amount for the specified order lines.
    /// </summary>
    /// <param name="lines">The order lines to total.</param>
    /// <returns>The sum of all line totals.</returns>
    public static decimal CalculateTotal(IEnumerable<OrderLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        decimal total = 0;

        foreach (OrderLine line in lines)
        {
            if (line.Quantity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(lines), "Order line quantity cannot be negative.");
            }

            if (line.UnitPrice < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(lines), "Order line unit price cannot be negative.");
            }

            total += line.Total;
        }

        return total;
    }
}
