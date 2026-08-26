using Catalog.Domain.DomainEvents;
using Catalog.Domain.ValueObjects;
using SharedKernel.Core.Domain;

namespace Catalog.Domain.Entities;

/// <summary>
/// The catalog product aggregate root. Owns its variants, which own their
/// supplier links and price history.
/// </summary>
public sealed class Product : BaseEntity, IAggregateRoot, ITenantScoped
{
    private readonly List<Variant> _variants = new();

    private Product()
    {
    }

    /// <inheritdoc/>
    public string TenantId { get; set; } = null!;

    /// <summary>Gets the product name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Gets the description.</summary>
    public string? Description { get; private set; }

    /// <summary>Gets the optional category id.</summary>
    public Guid? CategoryId { get; private set; }

    /// <summary>Gets a value indicating whether the product is active.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Gets the variants (at least one — the default).</summary>
    public IReadOnlyList<Variant> Variants => _variants;

    /// <summary>Creates a product with a single default variant.</summary>
    /// <param name="tenantId">The owning tenant id.</param>
    /// <param name="name">The product name.</param>
    /// <param name="description">The optional description.</param>
    /// <param name="categoryId">The optional category id.</param>
    /// <param name="sku">The stock-keeping unit of the default variant.</param>
    /// <param name="sellPrice">The sell price of the default variant.</param>
    /// <returns>The newly created product.</returns>
    public static Product Create(string? tenantId, string name, string? description, Guid? categoryId, string sku, Money sellPrice)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        var product = new Product
        {
            TenantId = tenantId!,
            Name = name,
            Description = description,
            CategoryId = categoryId,
            IsActive = true,
        };

        var defaultVariant = Variant.Create(sku, sellPrice, isDefault: true, attributes: []);
        product._variants.Add(defaultVariant);

        product.AddDomainEvent(new ProductCreated(product.Id, product.TenantId, product.Name, [defaultVariant.Id]));
        return product;
    }

    /// <summary>Adds a non-default variant and raises <see cref="VariantCreated"/>.</summary>
    /// <param name="sku">The stock-keeping unit of the new variant.</param>
    /// <param name="sellPrice">The sell price of the new variant.</param>
    /// <param name="attributes">The descriptive attributes of the new variant.</param>
    /// <returns>The id of the newly added variant.</returns>
    public Guid AddVariant(string sku, Money sellPrice, IEnumerable<VariantAttribute> attributes)
    {
        var variant = Variant.Create(sku, sellPrice, isDefault: false, attributes: attributes);
        _variants.Add(variant);
        AddDomainEvent(new VariantCreated(Id, variant.Id, variant.Sku));
        return variant.Id;
    }

    /// <summary>Changes a variant's sell price; raises an event only on a real change.</summary>
    /// <param name="variantId">The id of the variant to reprice.</param>
    /// <param name="newPrice">The new sell price.</param>
    public void ChangeVariantSellPrice(Guid variantId, Money newPrice)
    {
        ArgumentNullException.ThrowIfNull(newPrice);
        var variant = RequireVariant(variantId);
        var old = variant.SellPrice;

        if (old.Equals(newPrice))
        {
            return;
        }

        variant.ChangeSellPrice(newPrice);
        AddDomainEvent(new VariantSellPriceChanged(Id, variant.Id, old.Amount, newPrice.Amount, newPrice.Currency));
    }

    /// <summary>Links a supplier to a variant with sourcing details.</summary>
    /// <param name="variantId">The id of the variant to source.</param>
    /// <param name="supplierId">The id of the supplier to link.</param>
    /// <param name="costPrice">The supplier cost price.</param>
    /// <param name="supplierSku">The supplier's own SKU for this variant.</param>
    /// <param name="leadTimeDays">The lead time in days.</param>
    /// <param name="minOrderQuantity">The minimum order quantity.</param>
    /// <param name="isPreferred">Whether this becomes the preferred supplier for the variant.</param>
    /// <returns>The id of the created variant-supplier link.</returns>
    public Guid LinkSupplier(
        Guid variantId,
        Guid supplierId,
        Money costPrice,
        string supplierSku,
        int leadTimeDays,
        int minOrderQuantity,
        bool isPreferred)
    {
        var variant = RequireVariant(variantId);
        var link = variant.LinkSupplier(supplierId, costPrice, supplierSku, leadTimeDays, minOrderQuantity, isPreferred, DateTimeOffset.UtcNow);
        return link.Id;
    }

    /// <summary>Changes a variant↔supplier cost price, recording history.</summary>
    /// <param name="variantId">The id of the affected variant.</param>
    /// <param name="supplierId">The id of the supplier whose cost changes.</param>
    /// <param name="newCost">The new supplier cost price.</param>
    public void ChangeSupplierCost(Guid variantId, Guid supplierId, Money newCost)
    {
        ArgumentNullException.ThrowIfNull(newCost);
        var variant = RequireVariant(variantId);
        var link = variant.RequireSupplier(supplierId);
        var old = link.CostPrice;

        if (old.Equals(newCost))
        {
            return;
        }

        link.ChangeCost(newCost, DateTimeOffset.UtcNow);
        AddDomainEvent(new SupplierCostPriceChanged(Id, variant.Id, supplierId, old.Amount, newCost.Amount, newCost.Currency));
    }

    /// <summary>Sets the single preferred supplier for a variant.</summary>
    /// <param name="variantId">The id of the affected variant.</param>
    /// <param name="supplierId">The id of the supplier to mark as preferred.</param>
    public void SetPreferredSupplier(Guid variantId, Guid supplierId)
    {
        var variant = RequireVariant(variantId);
        variant.SetPreferred(supplierId);
    }

    /// <summary>Deactivates the product and all its variants.</summary>
    public void Deactivate()
    {
        IsActive = false;
        foreach (var variant in _variants)
        {
            variant.Deactivate();
        }
    }

    internal Variant RequireVariant(Guid variantId)
    {
        var variant = _variants.Find(v => v.Id == variantId);
        return variant ?? throw new InvalidOperationException($"Variant '{variantId}' does not belong to product '{Id}'.");
    }
}
