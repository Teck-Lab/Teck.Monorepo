using Ardalis.SmartEnum;

namespace Orders.Domain.ValueObjects;

public sealed class OrderStatus : SmartEnum<OrderStatus>
{
    public static readonly OrderStatus Pending = new(nameof(Pending), 1);

    public static readonly OrderStatus Confirmed = new(nameof(Confirmed), 2);

    public static readonly OrderStatus Shipped = new(nameof(Shipped), 3);

    public static readonly OrderStatus Delivered = new(nameof(Delivered), 4);

    public static readonly OrderStatus Cancelled = new(nameof(Cancelled), 5);

    private OrderStatus(string name, int value)
        : base(name, value)
    {
    }
}
