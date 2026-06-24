using SharedKernel.Core.Events;

namespace Catalog.Domain.DomainEvents;

/// <summary>Raised when a variant is added to an existing product.</summary>
public sealed class VariantCreated : DomainEvent
{
    public VariantCreated(Guid productId, Guid variantId, string sku)
    {
        ProductId = productId;
        VariantId = variantId;
        Sku = sku;
    }

    public Guid ProductId { get; }

    public Guid VariantId { get; }

    public string Sku { get; }
}
