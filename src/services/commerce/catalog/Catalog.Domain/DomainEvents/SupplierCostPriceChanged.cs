using SharedKernel.Core.Events;

namespace Catalog.Domain.DomainEvents;

/// <summary>Raised when a variant↔supplier cost price changes.</summary>
public sealed class SupplierCostPriceChanged : DomainEvent
{
    public SupplierCostPriceChanged(Guid productId, Guid variantId, Guid supplierId, decimal oldAmount, decimal newAmount, string currency)
    {
        ProductId = productId;
        VariantId = variantId;
        SupplierId = supplierId;
        OldAmount = oldAmount;
        NewAmount = newAmount;
        Currency = currency;
    }

    public Guid ProductId { get; }

    public Guid VariantId { get; }

    public Guid SupplierId { get; }

    public decimal OldAmount { get; }

    public decimal NewAmount { get; }

    public string Currency { get; }
}
