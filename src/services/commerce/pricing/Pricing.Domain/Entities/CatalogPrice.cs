using SharedKernel.Core.Domain;

namespace Pricing.Domain.Entities;

/// <summary>A tenant-scoped fallback sell price projected from catalog.</summary>
public sealed class CatalogPrice : BaseEntity, IAggregateRoot, ITenantScoped
{
    private CatalogPrice()
    {
    }

    /// <summary>Gets the catalog product identifier.</summary>
    public Guid ProductId { get; private set; }

    /// <summary>Gets the current default catalog variant identifier.</summary>
    public Guid VariantId { get; private set; }

    /// <summary>Gets the projected sell-price amount.</summary>
    public decimal Amount { get; private set; }

    /// <summary>Gets the ISO currency code.</summary>
    public string Currency { get; private set; } = string.Empty;

    /// <summary>Gets when catalog last changed this sell price.</summary>
    public DateTimeOffset ChangedAt { get; private set; }

    /// <inheritdoc/>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Creates a catalog-price projection row.</summary>
    /// <param name="productId">The catalog product identifier.</param>
    /// <param name="variantId">The default catalog variant identifier.</param>
    /// <param name="amount">The sell-price amount.</param>
    /// <param name="currency">The ISO currency code.</param>
    /// <param name="changedAt">The source change timestamp.</param>
    /// <param name="tenantId">The owning tenant identifier.</param>
    /// <returns>The new projection row.</returns>
    public static CatalogPrice Create(Guid productId, Guid variantId, decimal amount, string currency, DateTimeOffset changedAt, string tenantId)
    {
        var price = new CatalogPrice { ProductId = productId, TenantId = tenantId };
        price.Update(variantId, amount, currency, changedAt);
        return price;
    }

    /// <summary>Applies a newer catalog projection idempotently.</summary>
    /// <param name="variantId">The default catalog variant identifier.</param>
    /// <param name="amount">The sell-price amount.</param>
    /// <param name="currency">The ISO currency code.</param>
    /// <param name="changedAt">The source change timestamp.</param>
    public void Update(Guid variantId, decimal amount, string currency, DateTimeOffset changedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        if (ChangedAt > changedAt)
        {
            return;
        }

        VariantId = variantId;
        Amount = amount;
        Currency = currency.ToUpperInvariant();
        ChangedAt = changedAt;
    }
}
