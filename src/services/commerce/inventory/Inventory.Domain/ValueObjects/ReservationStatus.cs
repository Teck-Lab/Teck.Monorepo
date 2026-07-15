using Ardalis.SmartEnum;

namespace Inventories.Domain.ValueObjects;

/// <summary>
/// Represents the lifecycle status of a stock reservation.
/// </summary>
public sealed class ReservationStatus : SmartEnum<ReservationStatus>
{
    /// <summary>The reservation is holding stock pending commitment or expiry.</summary>
    public static readonly ReservationStatus Held = new(nameof(Held), 1);

    /// <summary>The reservation has been committed to an order.</summary>
    public static readonly ReservationStatus Committed = new(nameof(Committed), 2);

    /// <summary>The reservation has been fulfilled and stock has left inventory.</summary>
    public static readonly ReservationStatus Fulfilled = new(nameof(Fulfilled), 3);

    /// <summary>The reservation was released, returning stock to availability.</summary>
    public static readonly ReservationStatus Released = new(nameof(Released), 4);

    /// <summary>The reservation's hold expired without being committed.</summary>
    public static readonly ReservationStatus Expired = new(nameof(Expired), 5);

    private ReservationStatus(string name, int value)
        : base(name, value)
    {
    }

    /// <summary>
    /// Gets a value indicating whether the reservation is still active, i.e. it is
    /// currently holding or has committed stock that has not yet been released, fulfilled, or expired.
    /// </summary>
    public bool IsActive => this == Held || this == Committed;
}
