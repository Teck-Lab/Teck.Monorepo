using SharedKernel.Core.Events;

namespace Catalog.Domain.DomainEvents;

/// <summary>Raised when a variant is added to an existing product.</summary>
public sealed class VariantCreated : DomainEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="VariantCreated"/> class.
    /// </summary>
    /// <param name="productId">The id of the owning product.</param>
    /// <param name="variantId">The id of the newly added variant.</param>
    /// <param name="sku">The stock-keeping unit of the new variant.</param>
    public VariantCreated(Guid productId, Guid variantId, string sku)
    {
        ProductId = productId;
        VariantId = variantId;
        Sku = sku;
    }

    /// <summary>Gets the id of the owning product.</summary>
    public Guid ProductId { get; }

    /// <summary>Gets the id of the newly added variant.</summary>
    public Guid VariantId { get; }

    /// <summary>Gets the stock-keeping unit of the new variant.</summary>
    public string Sku { get; }
}
