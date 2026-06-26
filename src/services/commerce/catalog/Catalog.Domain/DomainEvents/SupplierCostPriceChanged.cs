using SharedKernel.Core.Events;

namespace Catalog.Domain.DomainEvents;

/// <summary>Raised when a variant↔supplier cost price changes.</summary>
public sealed class SupplierCostPriceChanged : DomainEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SupplierCostPriceChanged"/> class.
    /// </summary>
    /// <param name="productId">The id of the owning product.</param>
    /// <param name="variantId">The id of the affected variant.</param>
    /// <param name="supplierId">The id of the supplier whose cost changed.</param>
    /// <param name="oldAmount">The previous cost amount.</param>
    /// <param name="newAmount">The new cost amount.</param>
    /// <param name="currency">The ISO currency code of the amounts.</param>
    public SupplierCostPriceChanged(Guid productId, Guid variantId, Guid supplierId, decimal oldAmount, decimal newAmount, string currency)
    {
        ProductId = productId;
        VariantId = variantId;
        SupplierId = supplierId;
        OldAmount = oldAmount;
        NewAmount = newAmount;
        Currency = currency;
    }

    /// <summary>Gets the id of the owning product.</summary>
    public Guid ProductId { get; }

    /// <summary>Gets the id of the affected variant.</summary>
    public Guid VariantId { get; }

    /// <summary>Gets the id of the supplier whose cost changed.</summary>
    public Guid SupplierId { get; }

    /// <summary>Gets the previous cost amount.</summary>
    public decimal OldAmount { get; }

    /// <summary>Gets the new cost amount.</summary>
    public decimal NewAmount { get; }

    /// <summary>Gets the ISO currency code of the amounts.</summary>
    public string Currency { get; }
}
