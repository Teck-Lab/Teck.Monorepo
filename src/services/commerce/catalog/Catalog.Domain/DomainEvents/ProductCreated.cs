using SharedKernel.Core.Events;

namespace Catalog.Domain.DomainEvents;

/// <summary>Raised when a product (with its initial variants) is created.</summary>
public sealed class ProductCreated : DomainEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProductCreated"/> class.
    /// </summary>
    /// <param name="productId">The id of the created product.</param>
    /// <param name="tenantId">The owning tenant id.</param>
    /// <param name="name">The product name.</param>
    /// <param name="variantIds">The ids of the product's initial variants.</param>
    public ProductCreated(Guid productId, string tenantId, string name, IReadOnlyList<Guid> variantIds)
    {
        ProductId = productId;
        TenantId = tenantId;
        Name = name;
        VariantIds = variantIds;
    }

    /// <summary>Gets the id of the created product.</summary>
    public Guid ProductId { get; }

    /// <summary>Gets the owning tenant id.</summary>
    public string TenantId { get; }

    /// <summary>Gets the product name.</summary>
    public string Name { get; }

    /// <summary>Gets the ids of the product's initial variants.</summary>
    public IReadOnlyList<Guid> VariantIds { get; }
}
