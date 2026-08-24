using Ardalis.SmartEnum;

namespace Notifications.Domain.ValueObjects;

/// <summary>Represents the fixed, shopper-safe email template selected for a notification.</summary>
public sealed class NotificationKind : SmartEnum<NotificationKind>
{
    /// <summary>Requests a shopper action for a payment.</summary>
    public static readonly NotificationKind PaymentActionRequired = new(nameof(PaymentActionRequired), 1);
    /// <summary>Confirms an order.</summary>
    public static readonly NotificationKind OrderConfirmed = new(nameof(OrderConfirmed), 2);
    /// <summary>Explains an order cancellation.</summary>
    public static readonly NotificationKind OrderCancelled = new(nameof(OrderCancelled), 3);
    /// <summary>Explains an order rejection.</summary>
    public static readonly NotificationKind OrderRejected = new(nameof(OrderRejected), 4);
    /// <summary>Explains a backorder outcome.</summary>
    public static readonly NotificationKind BackorderOutcome = new(nameof(BackorderOutcome), 5);

    private NotificationKind(string name, int value) : base(name, value) { }
}
