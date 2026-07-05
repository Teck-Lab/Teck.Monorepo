using Pricing.Domain.DomainEvents;
using Pricing.Domain.ValueObjects;
using SharedKernel.Core.Domain;

namespace Pricing.Domain.Entities;

/// <summary>
/// A named, tenant-scoped set of product prices sharing one scope (currency + optional
/// country/customer-group/channel) and validity window. The write aggregate: prices are added,
/// updated, and removed only through this root, which raises <see cref="PriceChanged"/> for
/// effective (Active-list) changes.
/// </summary>
public sealed class PriceList : BaseEntity, IAggregateRoot, ITenantScoped
{
    private readonly List<Price> _prices = [];

    private PriceList()
    {
    }

    /// <summary>Gets the display name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Gets the optional description.</summary>
    public string? Description { get; private set; }

    /// <inheritdoc/>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets the lifecycle status.</summary>
    public PriceListStatus Status { get; private set; } = PriceListStatus.Draft;

    /// <summary>Gets the scope this list applies to.</summary>
    public PriceScope Scope { get; private set; } = null!;

    /// <summary>Gets the inclusive start of the validity window, or null for open-started.</summary>
    public DateTimeOffset? ValidFrom { get; private set; }

    /// <summary>Gets the exclusive end of the validity window, or null for open-ended.</summary>
    public DateTimeOffset? ValidUntil { get; private set; }

    /// <summary>Gets the prices contained in this list.</summary>
    public IReadOnlyCollection<Price> Prices => _prices;

    /// <summary>Creates a new draft price list.</summary>
    /// <param name="name">The display name.</param>
    /// <param name="scope">The scope.</param>
    /// <param name="validFrom">The inclusive validity start, or null.</param>
    /// <param name="validUntil">The exclusive validity end, or null.</param>
    /// <param name="tenantId">The owning tenant.</param>
    /// <returns>The new draft list.</returns>
    public static PriceList Create(string name, PriceScope scope, DateTimeOffset? validFrom, DateTimeOffset? validUntil, string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(scope);
        ValidateWindow(validFrom, validUntil);

        return new PriceList
        {
            Name = name,
            Scope = scope,
            ValidFrom = validFrom,
            ValidUntil = validUntil,
            TenantId = tenantId,
            Status = PriceListStatus.Draft,
        };
    }

    /// <summary>Determines whether the list's validity window contains a moment.</summary>
    /// <param name="at">The moment to test.</param>
    /// <returns><c>true</c> if within the window.</returns>
    public bool IsValidAt(DateTimeOffset at) =>
        (ValidFrom is null || at >= ValidFrom) && (ValidUntil is null || at < ValidUntil);

    /// <summary>Updates the name and description.</summary>
    /// <param name="name">The new name.</param>
    /// <param name="description">The new description.</param>
    public void UpdateDetails(string name, string? description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Description = description;
    }

    /// <summary>Replaces the scope (currency change re-validates contained prices) and re-emits when active.</summary>
    /// <param name="scope">The new scope.</param>
    public void UpdateScope(PriceScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        foreach (Price price in _prices)
        {
            if (!string.Equals(price.Amount.Currency, scope.Currency, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Cannot change scope currency while prices in the old currency exist.");
            }
        }

        Scope = scope;
        RaiseForAllPrices(PriceChangeType.Upserted);
    }

    /// <summary>Updates the validity window and re-emits when active.</summary>
    /// <param name="validFrom">The new inclusive start, or null.</param>
    /// <param name="validUntil">The new exclusive end, or null.</param>
    public void UpdateValidity(DateTimeOffset? validFrom, DateTimeOffset? validUntil)
    {
        ValidateWindow(validFrom, validUntil);
        ValidFrom = validFrom;
        ValidUntil = validUntil;
        RaiseForAllPrices(PriceChangeType.Upserted);
    }

    /// <summary>Activates the list and emits <see cref="PriceChanged"/> (Upserted) for every price.</summary>
    public void Activate()
    {
        Status = PriceListStatus.Active;
        RaiseForAllPrices(PriceChangeType.Upserted);
    }

    /// <summary>Archives the list and emits <see cref="PriceChanged"/> (Removed) for every price.</summary>
    public void Archive()
    {
        Status = PriceListStatus.Archived;
        RaiseForAllPrices(PriceChangeType.Removed);
    }

    /// <summary>Adds or updates the price for a product; emits Upserted only when the list is active.</summary>
    /// <param name="productId">The product identifier.</param>
    /// <param name="amount">The base unit amount (must match the list currency).</param>
    /// <param name="tiers">The quantity tiers.</param>
    public void AddOrUpdatePrice(Guid productId, Money amount, IReadOnlyList<PriceTier> tiers)
    {
        ArgumentNullException.ThrowIfNull(amount);
        if (!string.Equals(amount.Currency, Scope.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Price currency must match the list scope currency.", nameof(amount));
        }

        Price? existing = _prices.Find(price => price.ProductId == productId);
        if (existing is null)
        {
            _prices.Add(Price.Create(productId, amount, tiers, TenantId));
        }
        else
        {
            existing.Update(amount, tiers);
        }

        if (Status == PriceListStatus.Active)
        {
            Raise(productId, amount, PriceChangeType.Upserted);
        }
    }

    /// <summary>Removes a product's price; emits Removed only when the list is active.</summary>
    /// <param name="productId">The product identifier.</param>
    public void RemovePrice(Guid productId)
    {
        Price existing = _prices.Find(price => price.ProductId == productId)
            ?? throw new InvalidOperationException($"Product '{productId}' has no price in list '{Id}'.");

        _prices.Remove(existing);

        if (Status == PriceListStatus.Active)
        {
            Raise(productId, existing.Amount, PriceChangeType.Removed);
        }
    }

    private static void ValidateWindow(DateTimeOffset? validFrom, DateTimeOffset? validUntil)
    {
        if (validFrom is not null && validUntil is not null && validUntil <= validFrom)
        {
            throw new ArgumentException("ValidUntil must be after ValidFrom.", nameof(validUntil));
        }
    }

    private void RaiseForAllPrices(PriceChangeType changeType)
    {
        if (Status != PriceListStatus.Active && changeType == PriceChangeType.Upserted)
        {
            return;
        }

        foreach (Price price in _prices)
        {
            Raise(price.ProductId, price.Amount, changeType);
        }
    }

    private void Raise(Guid productId, Money amount, PriceChangeType changeType) =>
        AddDomainEvent(new PriceChanged(
            productId,
            Id,
            TenantId,
            amount.Amount,
            amount.Currency,
            changeType == PriceChangeType.Upserted ? ValidFrom ?? DateTimeOffset.UtcNow : DateTimeOffset.UtcNow,
            changeType));
}
