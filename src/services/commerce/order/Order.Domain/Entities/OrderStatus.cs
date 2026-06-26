using Ardalis.SmartEnum;

namespace Orders.Domain.ValueObjects;

/// <summary>
/// Represents the lifecycle status of an order.
/// </summary>
public sealed class OrderStatus : SmartEnum<OrderStatus>
{
    /// <summary>
    /// The order has been created but not yet confirmed.
    /// </summary>
    public static readonly OrderStatus Pending = new(nameof(Pending), 1);

    /// <summary>
    /// The order has been confirmed.
    /// </summary>
    public static readonly OrderStatus Confirmed = new(nameof(Confirmed), 2);

    /// <summary>
    /// The order has been shipped.
    /// </summary>
    public static readonly OrderStatus Shipped = new(nameof(Shipped), 3);

    /// <summary>
    /// The order has been delivered.
    /// </summary>
    public static readonly OrderStatus Delivered = new(nameof(Delivered), 4);

    /// <summary>
    /// The order has been cancelled.
    /// </summary>
    public static readonly OrderStatus Cancelled = new(nameof(Cancelled), 5);

    private OrderStatus(string name, int value)
        : base(name, value)
    {
    }
}
