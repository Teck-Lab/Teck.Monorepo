using Catalog.Domain.Entities;
using MemoryPack;
using SharedKernel.Core.Events;

namespace Catalog.Application.Products.IntegrationEvents;

/// <summary>
/// Published when a product is created. Inventory-seam event (unconsumed in v1).
/// TenantId is informational; the message envelope's X-TenantId is authoritative.
/// </summary>
[MemoryPackable]
public partial class ProductCreatedIntegrationEvent : IntegrationEvent
{
    /// <summary>Initializes a new instance of the <see cref="ProductCreatedIntegrationEvent"/> class.</summary>
    [MemoryPackConstructor]
    public ProductCreatedIntegrationEvent()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ProductCreatedIntegrationEvent"/> class from a created product.</summary>
    /// <param name="product">The product that was created.</param>
    public ProductCreatedIntegrationEvent(Product product)
    {
        ProductId = product.Id;
        TenantId = product.TenantId;
        Name = product.Name;
        VariantIds = product.Variants.Select(v => v.Id).ToList();
    }

    /// <summary>Gets or sets the product id.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Gets or sets the tenant id.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets or sets the product name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the initial variant ids.</summary>
    public List<Guid> VariantIds { get; set; } = [];
}
