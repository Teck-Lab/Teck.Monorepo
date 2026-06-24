using Catalog.Domain.ValueObjects;
using SharedKernel.Core.Domain;

namespace Catalog.Domain.Entities;

/// <summary>A sellable variation of a product. Owned by <see cref="Product"/>.</summary>
public sealed class Variant : BaseEntity
{
    private readonly List<VariantAttribute> _attributes = new();
    private readonly List<VariantSupplier> _suppliers = new();

    private Variant()
    {
    }

    /// <summary>Gets the stock-keeping unit.</summary>
    public string Sku { get; private set; } = string.Empty;

    /// <summary>Gets the customer-facing sell price.</summary>
    public Money SellPrice { get; private set; } = null!;

    /// <summary>Gets a value indicating whether this is the product's default variant.</summary>
    public bool IsDefault { get; private set; }

    /// <summary>Gets a value indicating whether the variant is active.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Gets the descriptive attributes.</summary>
    public IReadOnlyList<VariantAttribute> Attributes => _attributes;

    /// <summary>Gets the supplier links.</summary>
    public IReadOnlyList<VariantSupplier> Suppliers => _suppliers;

    internal static Variant Create(string sku, Money sellPrice, bool isDefault, IEnumerable<VariantAttribute> attributes)
    {
        ArgumentNullException.ThrowIfNull(sellPrice);

        if (string.IsNullOrWhiteSpace(sku))
        {
            throw new ArgumentException("Sku is required.", nameof(sku));
        }

        var variant = new Variant
        {
            Sku = sku,
            SellPrice = sellPrice,
            IsDefault = isDefault,
            IsActive = true,
        };

        if (attributes is not null)
        {
            variant._attributes.AddRange(attributes);
        }

        return variant;
    }

    internal void ChangeSellPrice(Money newPrice)
    {
        ArgumentNullException.ThrowIfNull(newPrice);
        SellPrice = newPrice;
    }

    internal void Deactivate() => IsActive = false;

    internal VariantSupplier LinkSupplier(
        Guid supplierId,
        Money costPrice,
        string supplierSku,
        int leadTimeDays,
        int minOrderQuantity,
        bool isPreferred,
        DateTimeOffset now)
    {
        if (isPreferred)
        {
            ClearPreferred();
        }

        var link = VariantSupplier.Create(supplierId, costPrice, supplierSku, leadTimeDays, minOrderQuantity, isPreferred, now);
        _suppliers.Add(link);
        return link;
    }

    internal VariantSupplier RequireSupplier(Guid supplierId)
    {
        var link = _suppliers.Find(s => s.SupplierId == supplierId);
        return link ?? throw new InvalidOperationException($"Supplier '{supplierId}' is not linked to variant '{Id}'.");
    }

    internal void SetPreferred(Guid supplierId)
    {
        var target = RequireSupplier(supplierId);
        ClearPreferred();
        target.MarkPreferred(true);
    }

    private void ClearPreferred()
    {
        foreach (var supplier in _suppliers)
        {
            supplier.MarkPreferred(false);
        }
    }
}
