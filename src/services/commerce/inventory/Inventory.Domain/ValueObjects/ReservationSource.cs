using Ardalis.SmartEnum;

namespace Inventories.Domain.ValueObjects;

/// <summary>
/// Represents the originating aggregate that requested a stock reservation.
/// </summary>
public sealed class ReservationSource : SmartEnum<ReservationSource>
{
    /// <summary>The reservation originated from a basket checkout.</summary>
    public static readonly ReservationSource Basket = new(nameof(Basket), 1);

    /// <summary>The reservation originated from a placed order.</summary>
    public static readonly ReservationSource Order = new(nameof(Order), 2);

    private ReservationSource(string name, int value)
        : base(name, value)
    {
    }
}
