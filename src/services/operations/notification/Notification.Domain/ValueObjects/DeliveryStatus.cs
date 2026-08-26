using Ardalis.SmartEnum;

namespace Notifications.Domain.ValueObjects;

/// <summary>Represents the durable dispatch state of a notification delivery.</summary>
public sealed class DeliveryStatus : SmartEnum<DeliveryStatus>
{
    /// <summary>The delivery is waiting for a contact or transport attempt.</summary>
    public static readonly DeliveryStatus Pending = new(nameof(Pending), 1);
    /// <summary>The deterministic sender accepted the delivery.</summary>
    public static readonly DeliveryStatus Sent = new(nameof(Sent), 2);
    /// <summary>The most recent transport attempt failed and may be retried.</summary>
    public static readonly DeliveryStatus Retryable = new(nameof(Retryable), 3);

    private DeliveryStatus(string name, int value) : base(name, value) { }
}
