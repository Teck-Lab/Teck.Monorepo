using SharedKernel.Core.Events;

namespace Catalog.Domain.DomainEvents;

/// <summary>Raised when a product (with its initial variants) is created.</summary>
public sealed class ProductCreated : DomainEvent
{
    public ProductCreated(Guid productId, string tenantId, string name, IReadOnlyList<Guid> variantIds)
    {
        ProductId = productId;
        TenantId = tenantId;
        Name = name;
        VariantIds = variantIds;
    }

    public Guid ProductId { get; }

    public string TenantId { get; }

    public string Name { get; }

    public IReadOnlyList<Guid> VariantIds { get; }
}
