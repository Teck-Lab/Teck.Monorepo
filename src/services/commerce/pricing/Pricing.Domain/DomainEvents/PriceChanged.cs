using Pricing.Domain.ValueObjects;
using SharedKernel.Core.Events;

namespace Pricing.Domain.DomainEvents;

/// <summary>Domain event raised when an effective price is created, updated, or removed.</summary>
public sealed class PriceChanged : DomainEvent
{
    /// <summary>Initializes a new instance of the <see cref="PriceChanged"/> class.</summary>
    /// <param name="productId">The product whose price changed.</param>
    /// <param name="priceListId">The owning price list.</param>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="amount">The amount involved in the change.</param>
    /// <param name="currency">The ISO currency of the amount.</param>
    /// <param name="effectiveFrom">When the change takes effect.</param>
    /// <param name="changeType">Whether the price was upserted or removed.</param>
    public PriceChanged(Guid productId, Guid priceListId, string tenantId, decimal amount, string currency, DateTimeOffset effectiveFrom, PriceChangeType changeType)
    {
        ProductId = productId;
        PriceListId = priceListId;
        TenantId = tenantId;
        Amount = amount;
        Currency = currency;
        EffectiveFrom = effectiveFrom;
        ChangeType = changeType;
    }

    /// <summary>Gets the product whose price changed.</summary>
    public Guid ProductId { get; }

    /// <summary>Gets the owning price list.</summary>
    public Guid PriceListId { get; }

    /// <summary>Gets the owning tenant.</summary>
    public string TenantId { get; }

    /// <summary>Gets the amount involved in the change.</summary>
    public decimal Amount { get; }

    /// <summary>Gets the ISO currency of the amount.</summary>
    public string Currency { get; }

    /// <summary>Gets when the change takes effect.</summary>
    public DateTimeOffset EffectiveFrom { get; }

    /// <summary>Gets whether the price was upserted or removed.</summary>
    public PriceChangeType ChangeType { get; }
}
