using Ardalis.SmartEnum;

namespace Billings.Domain.Entities;

/// <summary>
/// Represents the lifecycle status of a payment.
/// </summary>
public sealed class PaymentStatus : SmartEnum<PaymentStatus>
{
    /// <summary>
    /// The payment has been created but not yet authorized.
    /// </summary>
    public static readonly PaymentStatus Pending = new(nameof(Pending), 1);

    /// <summary>
    /// The payment has been authorized by the provider.
    /// </summary>
    public static readonly PaymentStatus Authorized = new(nameof(Authorized), 2);

    /// <summary>
    /// The payment has been captured.
    /// </summary>
    public static readonly PaymentStatus Captured = new(nameof(Captured), 3);

    /// <summary>
    /// The payment has failed.
    /// </summary>
    public static readonly PaymentStatus Failed = new(nameof(Failed), 4);

    /// <summary>The payment has been refunded.</summary>
    public static readonly PaymentStatus Refunded = new(nameof(Refunded), 5);

    private PaymentStatus(string name, int value)
        : base(name, value)
    {
    }
}
