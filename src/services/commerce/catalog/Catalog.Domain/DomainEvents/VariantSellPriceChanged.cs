using SharedKernel.Core.Events;

namespace Catalog.Domain.DomainEvents;

/// <summary>Raised when a variant's sell price changes.</summary>
public sealed class VariantSellPriceChanged : DomainEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="VariantSellPriceChanged"/> class.
    /// </summary>
    /// <param name="productId">The id of the owning product.</param>
    /// <param name="variantId">The id of the affected variant.</param>
    /// <param name="oldAmount">The previous sell price amount.</param>
    /// <param name="newAmount">The new sell price amount.</param>
    /// <param name="currency">The ISO currency code of the amounts.</param>
    public VariantSellPriceChanged(Guid productId, Guid variantId, decimal oldAmount, decimal newAmount, string currency)
    {
        ProductId = productId;
        VariantId = variantId;
        OldAmount = oldAmount;
        NewAmount = newAmount;
        Currency = currency;
    }

    /// <summary>Gets the id of the owning product.</summary>
    public Guid ProductId { get; }

    /// <summary>Gets the id of the affected variant.</summary>
    public Guid VariantId { get; }

    /// <summary>Gets the previous sell price amount.</summary>
    public decimal OldAmount { get; }

    /// <summary>Gets the new sell price amount.</summary>
    public decimal NewAmount { get; }

    /// <summary>Gets the ISO currency code of the amounts.</summary>
    public string Currency { get; }
}
