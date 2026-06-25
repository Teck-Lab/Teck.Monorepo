using SharedKernel.Core.Events;

namespace Catalog.Domain.DomainEvents;

/// <summary>Raised when a variant's sell price changes.</summary>
public sealed class VariantSellPriceChanged : DomainEvent
{
    public VariantSellPriceChanged(Guid productId, Guid variantId, decimal oldAmount, decimal newAmount, string currency)
    {
        ProductId = productId;
        VariantId = variantId;
        OldAmount = oldAmount;
        NewAmount = newAmount;
        Currency = currency;
    }

    public Guid ProductId { get; }

    public Guid VariantId { get; }

    public decimal OldAmount { get; }

    public decimal NewAmount { get; }

    public string Currency { get; }
}
