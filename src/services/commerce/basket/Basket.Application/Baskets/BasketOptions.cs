namespace Baskets.Application.Baskets;

/// <summary>Configuration options for the basket service.</summary>
public sealed class BasketOptions
{
    /// <summary>Gets the maximum number of distinct lines allowed in a basket.</summary>
    public int MaxItemsPerBasket { get; init; } = 100;

    /// <summary>Gets the maximum quantity allowed on a single line.</summary>
    public int MaxQuantityPerLine { get; init; } = 999;
}
