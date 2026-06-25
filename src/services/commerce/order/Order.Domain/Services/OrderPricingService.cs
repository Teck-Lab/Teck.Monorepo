using Orders.Domain.Entities;

namespace Orders.Domain.Services;

public static class OrderPricingService
{
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
