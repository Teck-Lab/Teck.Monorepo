using SharedKernel.Core.Domain;

namespace Billings.Domain.ValueObjects;

/// <summary>
/// An immutable monetary amount in a given currency.
/// </summary>
public sealed class Money : ValueObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Money"/> class.
    /// </summary>
    /// <param name="amount">The non-negative amount.</param>
    /// <param name="currency">The ISO currency code.</param>
    public Money(decimal amount, string currency)
    {
        if (amount < 0)
        {
            throw new ArgumentException("Amount cannot be negative.", nameof(amount));
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException("Currency is required.", nameof(currency));
        }

        Amount = amount;
        Currency = currency;
    }

    /// <summary>Gets the amount.</summary>
    public decimal Amount { get; }

    /// <summary>Gets the ISO currency code.</summary>
    public string Currency { get; }

    /// <inheritdoc/>
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }
}
