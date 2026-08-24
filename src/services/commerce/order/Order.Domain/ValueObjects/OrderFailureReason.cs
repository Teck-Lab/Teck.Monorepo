using Ardalis.SmartEnum;

namespace Orders.Domain.ValueObjects;

/// <summary>Represents a shopper-safe order failure reason.</summary>
public sealed class OrderFailureReason : SmartEnum<OrderFailureReason>
{
    /// <summary>No lifecycle failure is recorded.</summary>
    public static readonly OrderFailureReason None = new(nameof(None), 0);

    /// <summary>Payment needs shopper action.</summary>
    public static readonly OrderFailureReason PaymentActionRequired = new(nameof(PaymentActionRequired), 1);

    /// <summary>Stock was rejected.</summary>
    public static readonly OrderFailureReason StockRejected = new(nameof(StockRejected), 2);

    /// <summary>A backorder expired.</summary>
    public static readonly OrderFailureReason BackorderExpired = new(nameof(BackorderExpired), 3);

    /// <summary>The repriced total exceeded the shopper ceiling.</summary>
    public static readonly OrderFailureReason PriceExceededAuthorization = new(nameof(PriceExceededAuthorization), 4);

    private OrderFailureReason(string name, int value)
        : base(name, value)
    {
    }

    /// <summary>Maps a billing-private category to a safe order failure.</summary>
    /// <param name="category">The safe billing category.</param>
    /// <returns>The safe order failure value.</returns>
    public static OrderFailureReason FromCategory(string category) => PaymentActionRequired;
}
