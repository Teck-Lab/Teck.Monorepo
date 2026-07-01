using Ardalis.SmartEnum;

namespace Baskets.Domain.ValueObjects;

/// <summary>
/// Represents the lifecycle status of a basket.
/// </summary>
public sealed class BasketStatus : SmartEnum<BasketStatus>
{
    /// <summary>The basket is open and accepting changes.</summary>
    public static readonly BasketStatus Active = new(nameof(Active), 1);

    /// <summary>The basket has been checked out and converted to an order.</summary>
    public static readonly BasketStatus CheckedOut = new(nameof(CheckedOut), 2);

    /// <summary>The basket was abandoned without checkout.</summary>
    public static readonly BasketStatus Abandoned = new(nameof(Abandoned), 3);

    /// <summary>The basket was merged into another basket on login.</summary>
    public static readonly BasketStatus Merged = new(nameof(Merged), 4);

    private BasketStatus(string name, int value)
        : base(name, value)
    {
    }
}
