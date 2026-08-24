namespace Inventories.Application.Inventory;

/// <summary>Configuration options for the inventory service.</summary>
public sealed class InventoryOptions
{
    /// <summary>Gets the duration a stock hold is retained before it expires.</summary>
    public TimeSpan HoldTtl { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>Gets the maximum number of times a reservation may be retried after allocation contention.</summary>
    public int MaxReserveRetries { get; init; } = 3;

    /// <summary>Gets the interval at which expired holds are swept.</summary>
    public TimeSpan SweepInterval { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>Gets the maximum time an order may wait for backordered stock.</summary>
    public TimeSpan BackorderWait { get; init; } = TimeSpan.FromDays(7);
}
