using Ardalis.SmartEnum;

namespace Orders.Domain.Entities;

/// <summary>Represents the lifecycle status of an order.</summary>
public sealed class OrderStatus : SmartEnum<OrderStatus>
{
    /// <summary>The order awaits payment or stock outcomes.</summary>
    public static readonly OrderStatus Pending = new(nameof(Pending), 1);

    /// <summary>The order has payment and stock confirmation.</summary>
    public static readonly OrderStatus Confirmed = new(nameof(Confirmed), 2);

    /// <summary>The order has been shipped.</summary>
    public static readonly OrderStatus Shipped = new(nameof(Shipped), 3);

    /// <summary>The order has been delivered.</summary>
    public static readonly OrderStatus Delivered = new(nameof(Delivered), 4);

    /// <summary>The order was cancelled before payment capture.</summary>
    public static readonly OrderStatus Cancelled = new(nameof(Cancelled), 5);

    /// <summary>The order cannot proceed within its authorized ceiling.</summary>
    public static readonly OrderStatus Rejected = new(nameof(Rejected), 6);

    /// <summary>The order was paid but cannot be fulfilled and requires human action.</summary>
    public static readonly OrderStatus PaidUnfulfillable = new(nameof(PaidUnfulfillable), 7);

    private OrderStatus(string name, int value)
        : base(name, value)
    {
    }
}
