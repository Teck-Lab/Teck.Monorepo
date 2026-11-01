using MemoryPack;
using SharedKernel.Core.Events;

namespace SharedKernel.Events;

/// <summary>Billing-authorized loss of tenant subscription access after cancellation or failed renewal.</summary>
[MemoryPackable]
public partial class SubscriptionAccessSuspendedIntegrationEvent : IntegrationEvent
{
    /// <summary>Gets or sets the canonical Customer tenant registry ID.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Gets or sets the immutable Customer reservation identifier.</summary>
    public string TenantIdentifier { get; set; } = string.Empty;

    /// <summary>Gets or sets the originating Billing subscription purchase order.</summary>
    public Guid SourceOrderId { get; set; }

    /// <summary>Gets or sets the confirmed subscription being suspended.</summary>
    public Guid SubscriptionId { get; set; }

    /// <summary>Gets or sets either <c>cancelled</c> or <c>renewal_failed</c>.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Gets or sets Billing's immutable effective suspension time.</summary>
    public DateTimeOffset SuspendedAtUtc { get; set; }

    /// <summary>Gets or sets Billing's base64 RSA-PSS signature on the complete suspension snapshot.</summary>
    public string Proof { get; set; } = string.Empty;
}
