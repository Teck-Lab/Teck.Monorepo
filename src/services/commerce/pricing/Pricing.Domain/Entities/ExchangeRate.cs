using SharedKernel.Core.Domain;

namespace Pricing.Domain.Entities;

/// <summary>
/// A tenant-managed exchange rate from one currency to another. v1 keeps at most one rate per
/// (FromCurrency, ToCurrency) pair; the validity window is optional (null = always valid).
/// </summary>
public sealed class ExchangeRate : BaseEntity, IAggregateRoot, ITenantScoped
{
    private ExchangeRate()
    {
    }

    /// <summary>Gets the source ISO currency.</summary>
    public string FromCurrency { get; private set; } = string.Empty;

    /// <summary>Gets the target ISO currency.</summary>
    public string ToCurrency { get; private set; } = string.Empty;

    /// <summary>Gets the multiplicative rate (from → to).</summary>
    public decimal Rate { get; private set; }

    /// <summary>Gets the inclusive validity start, or null for open-started.</summary>
    public DateTimeOffset? ValidFrom { get; private set; }

    /// <summary>Gets the exclusive validity end, or null for open-ended.</summary>
    public DateTimeOffset? ValidUntil { get; private set; }

    /// <inheritdoc/>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Creates a new exchange rate.</summary>
    /// <param name="from">The source ISO currency.</param>
    /// <param name="to">The target ISO currency.</param>
    /// <param name="rate">The positive multiplicative rate.</param>
    /// <param name="validFrom">The inclusive validity start, or null.</param>
    /// <param name="validUntil">The exclusive validity end, or null.</param>
    /// <param name="tenantId">The owning tenant.</param>
    /// <returns>The new exchange rate.</returns>
    public static ExchangeRate Create(string from, string to, decimal rate, DateTimeOffset? validFrom, DateTimeOffset? validUntil, string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(from);
        ArgumentException.ThrowIfNullOrWhiteSpace(to);
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("From and to currencies must differ.", nameof(to));
        }

        ValidateWindow(validFrom, validUntil);
        if (rate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rate), "Rate must be positive.");
        }

        return new ExchangeRate
        {
            FromCurrency = from,
            ToCurrency = to,
            Rate = rate,
            ValidFrom = validFrom,
            ValidUntil = validUntil,
            TenantId = tenantId,
        };
    }

    /// <summary>Updates the rate.</summary>
    /// <param name="rate">The new positive rate.</param>
    public void UpdateRate(decimal rate)
    {
        if (rate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rate), "Rate must be positive.");
        }

        Rate = rate;
    }

    /// <summary>Updates the validity window.</summary>
    /// <param name="from">The new inclusive start, or null.</param>
    /// <param name="until">The new exclusive end, or null.</param>
    public void UpdateValidity(DateTimeOffset? from, DateTimeOffset? until)
    {
        ValidateWindow(from, until);
        ValidFrom = from;
        ValidUntil = until;
    }

    /// <summary>Determines whether this rate is usable at a moment.</summary>
    /// <param name="at">The moment to test.</param>
    /// <returns><c>true</c> if within the (possibly open) window.</returns>
    public bool IsValidAt(DateTimeOffset at) =>
        (ValidFrom is null || at >= ValidFrom) && (ValidUntil is null || at < ValidUntil);

    private static void ValidateWindow(DateTimeOffset? from, DateTimeOffset? until)
    {
        if (from is not null && until is not null && until <= from)
        {
            throw new ArgumentException("ValidUntil must be after ValidFrom.", nameof(until));
        }
    }
}
