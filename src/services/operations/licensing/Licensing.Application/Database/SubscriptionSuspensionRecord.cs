namespace Licensing.Application.Database;

/// <summary>Billing-authorized subscription suspension; survives a cancellation arriving before payment.</summary>
public sealed class SubscriptionSuspensionRecord
{
    /// <summary>Gets or sets the suspended subscription identifier.</summary>
    public Guid SubscriptionId { get; set; }

    /// <summary>Gets or sets the originating purchase order identifier.</summary>
    public Guid SourceOrderId { get; set; }

    /// <summary>Gets or sets the customer tenant identifier.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Gets or sets the effective suspension time.</summary>
    public DateTimeOffset SuspendedAtUtc { get; set; }
}
