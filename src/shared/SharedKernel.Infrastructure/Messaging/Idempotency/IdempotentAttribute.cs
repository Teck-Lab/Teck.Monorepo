namespace SharedKernel.Infrastructure.Messaging.Idempotency;

/// <summary>
/// Marks a message type as idempotent so that duplicate deliveries are suppressed by the
/// idempotency middleware for the configured time-to-live window.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class IdempotentAttribute : Attribute
{
    /// <summary>
    /// Gets the time-to-live, in hours, for which the idempotency key is retained (default: 24).
    /// </summary>
    public int TtlHours { get; init; } = 24;
}
