using Catalog.Domain.DomainEvents;
using MemoryPack;
using SharedKernel.Core.Events;

namespace Catalog.Application.Products.IntegrationEvents;

/// <summary>Published when a variant's sell price changes. Consumed by basket/order (v1).</summary>
[MemoryPackable]
public partial class ProductPriceChangedIntegrationEvent : IntegrationEvent
{
    /// <summary>Initializes a new instance of the <see cref="ProductPriceChangedIntegrationEvent"/> class.</summary>
    [MemoryPackConstructor]
    public ProductPriceChangedIntegrationEvent()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ProductPriceChangedIntegrationEvent"/> class from the domain event.</summary>
    /// <param name="domainEvent">The domain event describing the sell price change.</param>
    /// <param name="tenantId">The identifier of the tenant the change occurred in.</param>
    public ProductPriceChangedIntegrationEvent(VariantSellPriceChanged domainEvent, string tenantId)
    {
        ProductId = domainEvent.ProductId;
        VariantId = domainEvent.VariantId;
        OldAmount = domainEvent.OldAmount;
        NewAmount = domainEvent.NewAmount;
        Currency = domainEvent.Currency;
        TenantId = tenantId;
    }

    /// <summary>Gets or sets the product id.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Gets or sets the variant id.</summary>
    public Guid VariantId { get; set; }

    /// <summary>Gets or sets the previous amount.</summary>
    public decimal OldAmount { get; set; }

    /// <summary>Gets or sets the new amount.</summary>
    public decimal NewAmount { get; set; }

    /// <summary>Gets or sets the ISO currency code.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Gets or sets the tenant id (informational; envelope X-TenantId is authoritative).</summary>
    public string TenantId { get; set; } = string.Empty;
}
