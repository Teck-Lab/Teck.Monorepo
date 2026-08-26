using Ardalis.SmartEnum;

namespace Billings.Domain.ValueObjects;

/// <summary>Represents the lifecycle state of one provider payment attempt.</summary>
public sealed class PaymentAttemptStatus : SmartEnum<PaymentAttemptStatus>
{
    /// <summary>The provider call has been recorded but has not completed.</summary>
    public static readonly PaymentAttemptStatus Pending = new(nameof(Pending), 1);

    /// <summary>The provider is processing the attempt asynchronously.</summary>
    public static readonly PaymentAttemptStatus Processing = new(nameof(Processing), 2);

    /// <summary>The provider captured the payment.</summary>
    public static readonly PaymentAttemptStatus Succeeded = new(nameof(Succeeded), 3);

    /// <summary>The provider declined or rejected the payment.</summary>
    public static readonly PaymentAttemptStatus Failed = new(nameof(Failed), 4);

    /// <summary>The payment requires additional shopper authentication.</summary>
    public static readonly PaymentAttemptStatus RequiresAction = new(nameof(RequiresAction), 5);

    /// <summary>The payment requires a replacement payment method.</summary>
    public static readonly PaymentAttemptStatus RequiresPaymentMethod = new(nameof(RequiresPaymentMethod), 6);

    /// <summary>The attempt was cancelled before completion.</summary>
    public static readonly PaymentAttemptStatus Cancelled = new(nameof(Cancelled), 7);

    private PaymentAttemptStatus(string name, int value)
        : base(name, value)
    {
    }
}
