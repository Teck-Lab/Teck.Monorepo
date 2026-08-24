using Ardalis.SmartEnum;

namespace Orders.Domain.ValueObjects;

/// <summary>Represents the independent payment substate of an order.</summary>
public sealed class PaymentState : SmartEnum<PaymentState>
{
    /// <summary>Payment has not completed.</summary>
    public static readonly PaymentState Pending = new(nameof(Pending), 1);

    /// <summary>Payment was captured.</summary>
    public static readonly PaymentState Captured = new(nameof(Captured), 2);

    /// <summary>The shopper must provide payment action.</summary>
    public static readonly PaymentState ActionRequired = new(nameof(ActionRequired), 3);

    private PaymentState(string name, int value)
        : base(name, value)
    {
    }
}
