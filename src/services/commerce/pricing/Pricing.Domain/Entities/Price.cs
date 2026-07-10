using Pricing.Domain.ValueObjects;
using SharedKernel.Core.Domain;

namespace Pricing.Domain.Entities;

/// <summary>
/// A product's price within a <see cref="PriceList"/>. A first-class, tenant-scoped entity indexed
/// by (TenantId, ProductId) for the resolution hot path; mutated only through the owning list.
/// </summary>
public sealed class Price : BaseEntity, ITenantScoped
{
    private readonly List<PriceTier> _tiers = [];

    private Price()
    {
    }

    /// <summary>Gets the product identifier.</summary>
    public Guid ProductId { get; private set; }

    /// <summary>Gets the base unit amount (used when no tier applies).</summary>
    public Money Amount { get; private set; } = null!;

    /// <summary>Gets the quantity tiers, ascending by minimum quantity.</summary>
    public IReadOnlyList<PriceTier> Tiers => _tiers;

    /// <summary>Gets the owning price list identifier.</summary>
    public Guid PriceListId { get; private set; }

    /// <summary>Gets the owning price list navigation.</summary>
    public PriceList PriceList { get; private set; } = null!;

    /// <inheritdoc/>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Returns the unit amount for a quantity (highest applicable tier, else base amount).</summary>
    /// <param name="quantity">The requested quantity.</param>
    /// <returns>The applicable unit amount.</returns>
    public Money UnitAmountFor(int quantity)
    {
        Money best = Amount;
        int bestMin = 0;
        foreach (PriceTier tier in _tiers)
        {
            if (tier.MinQuantity <= quantity && tier.MinQuantity >= bestMin)
            {
                best = tier.Amount;
                bestMin = tier.MinQuantity;
            }
        }

        return best;
    }

    /// <summary>Creates a price for a product.</summary>
    /// <param name="productId">The product identifier.</param>
    /// <param name="amount">The base unit amount.</param>
    /// <param name="tiers">The quantity tiers (may be empty).</param>
    /// <param name="tenantId">The owning tenant.</param>
    /// <returns>The new price.</returns>
    internal static Price Create(Guid productId, Money amount, IReadOnlyList<PriceTier> tiers, string tenantId)
    {
        var price = new Price { ProductId = productId, TenantId = tenantId };
        price.Update(amount, tiers);
        return price;
    }

    /// <summary>Replaces the amount and tiers, validating tier ordering and currency.</summary>
    /// <param name="amount">The new base unit amount.</param>
    /// <param name="tiers">The new quantity tiers.</param>
    internal void Update(Money amount, IReadOnlyList<PriceTier> tiers)
    {
        ArgumentNullException.ThrowIfNull(amount);
        ArgumentNullException.ThrowIfNull(tiers);

        int previousMin = 0;
        foreach (PriceTier tier in tiers)
        {
            if (tier.MinQuantity < 1)
            {
                throw new ArgumentException("Tier minimum quantity must be at least 1.", nameof(tiers));
            }

            if (tier.MinQuantity <= previousMin)
            {
                throw new ArgumentException("Tiers must have strictly ascending, unique minimum quantities.", nameof(tiers));
            }

            if (!string.Equals(tier.Amount.Currency, amount.Currency, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Tier currency must match the price currency.", nameof(tiers));
            }

            previousMin = tier.MinQuantity;
        }

        Amount = amount;
        _tiers.Clear();
        _tiers.AddRange(tiers);
    }
}
