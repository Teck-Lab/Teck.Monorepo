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
    public string TenantId { get; set; } = string.Empty;

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
    public static Product Create(string tenantId, string name, string? description, Guid? categoryId, string sku, Money sellPrice)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        var product = new Product
        {
            TenantId = tenantId,
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
    public Guid AddVariant(string sku, Money sellPrice, IEnumerable<VariantAttribute> attributes)
    {
        var variant = Variant.Create(sku, sellPrice, isDefault: false, attributes: attributes);
        _variants.Add(variant);
        AddDomainEvent(new VariantCreated(Id, variant.Id, variant.Sku));
        return variant.Id;
    }

    internal Variant RequireVariant(Guid variantId)
    {
        var variant = _variants.Find(v => v.Id == variantId);
        return variant ?? throw new InvalidOperationException($"Variant '{variantId}' does not belong to product '{Id}'.");
    }
}
