using MemoryPack;
using SharedKernel.Core.Events;

namespace Catalog.Application.Products.IntegrationEvents;

/// <summary>Published when a variant is added to an existing product. Inventory-seam event (unconsumed in v1).</summary>
[MemoryPackable]
public partial class VariantCreatedIntegrationEvent : IntegrationEvent
{
    /// <summary>Initializes a new instance of the <see cref="VariantCreatedIntegrationEvent"/> class.</summary>
    [MemoryPackConstructor]
    public VariantCreatedIntegrationEvent()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="VariantCreatedIntegrationEvent"/> class.</summary>
    /// <param name="productId">The identifier of the product the variant belongs to.</param>
    /// <param name="variantId">The identifier of the newly created variant.</param>
    /// <param name="sku">The stock-keeping unit of the new variant.</param>
    public VariantCreatedIntegrationEvent(Guid productId, Guid variantId, string sku)
    {
        ProductId = productId;
        VariantId = variantId;
        Sku = sku;
    }

    /// <summary>Gets or sets the product id.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Gets or sets the variant id.</summary>
    public Guid VariantId { get; set; }

    /// <summary>Gets or sets the SKU.</summary>
    public string Sku { get; set; } = string.Empty;
}
