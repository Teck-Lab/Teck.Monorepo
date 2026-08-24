using Ardalis.SmartEnum;

namespace Orders.Domain.ValueObjects;

/// <summary>Represents the independent stock substate of an order.</summary>
public sealed class StockState : SmartEnum<StockState>
{
    /// <summary>Stock is pending.</summary>
    public static readonly StockState Pending = new(nameof(Pending), 1);

    /// <summary>Stock is reserved.</summary>
    public static readonly StockState Reserved = new(nameof(Reserved), 2);

    /// <summary>Stock is awaiting backorder replenishment.</summary>
    public static readonly StockState Backordered = new(nameof(Backordered), 3);

    /// <summary>Stock is ready but must be repriced.</summary>
    public static readonly StockState AwaitingPriceCheck = new(nameof(AwaitingPriceCheck), 4);

    /// <summary>Stock cannot be supplied.</summary>
    public static readonly StockState Rejected = new(nameof(Rejected), 5);

    /// <summary>The backorder expired.</summary>
    public static readonly StockState Expired = new(nameof(Expired), 6);

    private StockState(string name, int value)
        : base(name, value)
    {
    }
}
