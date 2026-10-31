using MemoryPack;
using SharedKernel.Core.Events;

namespace SharedKernel.Events;

/// <summary>Two distinct Customer-authorized operators approved purging one suspended paid database period.</summary>
[MemoryPackable]
public partial class TenantPurgeApprovedIntegrationEvent : IntegrationEvent
{
    /// <summary>Gets or sets the unique Customer audit decision identifier.</summary>
    public Guid ApprovalId { get; set; }

    /// <summary>Gets or sets the canonical Customer registry tenant identifier.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Gets or sets the Customer registry slug for source-order validation.</summary>
    public string TenantIdentifier { get; set; } = string.Empty;

    /// <summary>Gets or sets the stable Billing subscription identifier.</summary>
    public Guid SubscriptionId { get; set; }

    /// <summary>Gets or sets the exact paid order whose data may be purged.</summary>
    public Guid SourceOrderId { get; set; }

    /// <summary>Gets or sets the first independent Keycloak operator subject.</summary>
    public string FirstApproverSubjectId { get; set; } = string.Empty;

    /// <summary>Gets or sets the second independent Keycloak operator subject.</summary>
    public string SecondApproverSubjectId { get; set; } = string.Empty;

    /// <summary>Gets or sets Billing's effective suspension time.</summary>
    public DateTimeOffset SuspendedAtUtc { get; set; }

    /// <summary>Gets or sets the earliest permitted purge time under the configured recovery window.</summary>
    public DateTimeOffset EligibleAtUtc { get; set; }

    /// <summary>Gets or sets the time of the second authorized approval.</summary>
    public DateTimeOffset ApprovedAtUtc { get; set; }

    /// <summary>Gets or sets Customer's detached domain-separated RSA-PSS approval proof.</summary>
    public string Proof { get; set; } = string.Empty;
}
