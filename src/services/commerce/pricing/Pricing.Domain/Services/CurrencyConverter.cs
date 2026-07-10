using Pricing.Domain.ValueObjects;

namespace Pricing.Domain.Services;

/// <summary>Converts monetary amounts between currencies using an exchange rate.</summary>
public static class CurrencyConverter
{
    /// <summary>Converts a source amount into a target currency at the given rate.</summary>
    /// <param name="source">The source money.</param>
    /// <param name="targetCurrency">The ISO 4217 target currency.</param>
    /// <param name="rate">The multiplicative rate (source → target); must be positive.</param>
    /// <param name="decimals">The number of decimal places to round to.</param>
    /// <param name="mode">The midpoint rounding mode.</param>
    /// <returns>The converted amount as <see cref="Money"/> in <paramref name="targetCurrency"/>.</returns>
    public static Money Convert(Money source, string targetCurrency, decimal rate, int decimals, MidpointRounding mode)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (rate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rate), "Exchange rate must be positive.");
        }

        decimal converted = Math.Round(source.Amount * rate, decimals, mode);
        return new Money(converted, targetCurrency);
    }
}
