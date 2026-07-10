using Ardalis.SmartEnum;

namespace Pricing.Domain.ValueObjects;

/// <summary>Represents the lifecycle status of a price list.</summary>
public sealed class PriceListStatus : SmartEnum<PriceListStatus>
{
    /// <summary>The list is being edited and is not yet resolvable.</summary>
    public static readonly PriceListStatus Draft = new(nameof(Draft), 1);

    /// <summary>The list is active and participates in price resolution.</summary>
    public static readonly PriceListStatus Active = new(nameof(Active), 2);

    /// <summary>The list is archived and no longer resolvable.</summary>
    public static readonly PriceListStatus Archived = new(nameof(Archived), 3);

    private PriceListStatus(string name, int value)
        : base(name, value)
    {
    }
}
