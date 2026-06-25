using Catalog.Domain.ValueObjects;
using SharedKernel.Core.Domain;

namespace Catalog.Domain.Entities;

/// <summary>The link between a variant and a supplier, carrying sourcing details.</summary>
public sealed class VariantSupplier : BaseEntity
{
    private readonly List<SupplierPriceHistory> _priceHistory = new();

    private VariantSupplier()
    {
    }

    /// <summary>Gets the linked supplier id.</summary>
    public Guid SupplierId { get; private set; }

    /// <summary>Gets the current supplier cost price.</summary>
    public Money CostPrice { get; private set; } = null!;

    /// <summary>Gets the supplier's own SKU for this variant.</summary>
    public string SupplierSku { get; private set; } = string.Empty;

    /// <summary>Gets the lead time in days.</summary>
    public int LeadTimeDays { get; private set; }

    /// <summary>Gets the minimum order quantity.</summary>
    public int MinOrderQuantity { get; private set; }

    /// <summary>Gets a value indicating whether this is the preferred supplier for the variant.</summary>
    public bool IsPreferred { get; private set; }

    /// <summary>Gets the cost price history (newest entries appended).</summary>
    public IReadOnlyList<SupplierPriceHistory> PriceHistory => _priceHistory;

    internal static VariantSupplier Create(
        Guid supplierId,
        Money costPrice,
        string supplierSku,
        int leadTimeDays,
        int minOrderQuantity,
        bool isPreferred,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(costPrice);

        if (supplierId == Guid.Empty)
        {
            throw new ArgumentException("SupplierId is required.", nameof(supplierId));
        }

        var link = new VariantSupplier
        {
            SupplierId = supplierId,
            CostPrice = costPrice,
            SupplierSku = supplierSku,
            LeadTimeDays = leadTimeDays,
            MinOrderQuantity = minOrderQuantity,
            IsPreferred = isPreferred,
        };
        link._priceHistory.Add(SupplierPriceHistory.Create(costPrice, now));
        return link;
    }

    internal void ChangeCost(Money newCost, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(newCost);
        CostPrice = newCost;
        _priceHistory.Add(SupplierPriceHistory.Create(newCost, now));
    }

    internal void MarkPreferred(bool isPreferred) => IsPreferred = isPreferred;
}
