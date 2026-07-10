using SharedKernel.Core.Domain;

namespace Pricing.Domain.ValueObjects;

/// <summary>
/// The scope a price list applies to. A null dimension is a wildcard that matches any request value.
/// </summary>
public sealed class PriceScope : ValueObject
{
    /// <summary>Initializes a new instance of the <see cref="PriceScope"/> class.</summary>
    /// <param name="currency">The ISO 4217 currency (required — a list is single-currency).</param>
    /// <param name="country">The ISO 3166-1 alpha-2 country, or null for any.</param>
    /// <param name="customerGroupId">The customer group, or null for any.</param>
    /// <param name="channelId">The sales channel, or null for any.</param>
    public PriceScope(string currency, string? country, Guid? customerGroupId, Guid? channelId)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException("Currency is required.", nameof(currency));
        }

        Currency = currency;
        Country = country;
        CustomerGroupId = customerGroupId;
        ChannelId = channelId;
    }

    /// <summary>Gets the ISO 4217 currency.</summary>
    public string Currency { get; }

    /// <summary>Gets the ISO 3166-1 alpha-2 country, or null for any.</summary>
    public string? Country { get; }

    /// <summary>Gets the customer group, or null for any.</summary>
    public Guid? CustomerGroupId { get; }

    /// <summary>Gets the sales channel, or null for any.</summary>
    public Guid? ChannelId { get; }

    /// <summary>Gets the number of set (non-wildcard) non-currency dimensions.</summary>
    public int Specificity =>
        (Country is null ? 0 : 1) + (CustomerGroupId is null ? 0 : 1) + (ChannelId is null ? 0 : 1);

    /// <summary>Determines whether this scope is compatible with a request context.</summary>
    /// <param name="country">The request country.</param>
    /// <param name="customerGroupId">The request customer group.</param>
    /// <param name="channelId">The request channel.</param>
    /// <returns><c>true</c> if every set dimension equals the corresponding request value.</returns>
    public bool IsCompatibleWith(string? country, Guid? customerGroupId, Guid? channelId)
    {
        if (Country is not null && !string.Equals(Country, country, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (CustomerGroupId is not null && CustomerGroupId != customerGroupId)
        {
            return false;
        }

        return ChannelId is null || ChannelId == channelId;
    }

    /// <inheritdoc/>
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Currency;
        yield return Country;
        yield return CustomerGroupId;
        yield return ChannelId;
    }
}
