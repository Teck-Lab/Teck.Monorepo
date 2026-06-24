using MemoryPack;
using SharedKernel.Core.Events;

namespace Catalog.Application.Products.IntegrationEvents;

/// <summary>Published when a variant is added to an existing product. Inventory-seam event (unconsumed in v1).</summary>
[MemoryPackable]
public partial class VariantCreatedIntegrationEvent : IntegrationEvent
{
    /// <summary>Gets or sets the product id.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Gets or sets the variant id.</summary>
    public Guid VariantId { get; set; }

    /// <summary>Gets or sets the SKU.</summary>
    public string Sku { get; set; } = string.Empty;

    /// <summary>Serialization constructor.</summary>
    [MemoryPackConstructor]
    public VariantCreatedIntegrationEvent()
    {
    }

    /// <summary>Builds the event.</summary>
    public VariantCreatedIntegrationEvent(Guid productId, Guid variantId, string sku)
    {
        ProductId = productId;
        VariantId = variantId;
        Sku = sku;
    }
}
