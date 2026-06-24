using Catalog.Domain.ValueObjects;
using SharedKernel.Core.Domain;

namespace Catalog.Domain.Entities;

/// <summary>An effective-dated record of a supplier cost price for a variant.</summary>
public sealed class SupplierPriceHistory : BaseEntity
{
    private SupplierPriceHistory()
    {
    }

    /// <summary>Gets the cost price effective from <see cref="EffectiveFrom"/>.</summary>
    public Money CostPrice { get; private set; } = null!;

    /// <summary>Gets the moment this cost price became effective.</summary>
    public DateTimeOffset EffectiveFrom { get; private set; }

    internal static SupplierPriceHistory Create(Money costPrice, DateTimeOffset effectiveFrom)
    {
        ArgumentNullException.ThrowIfNull(costPrice);

        return new SupplierPriceHistory
        {
            CostPrice = costPrice,
            EffectiveFrom = effectiveFrom,
        };
    }
}
