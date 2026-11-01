using MemoryPack;
using SharedKernel.Core.Events;

namespace SharedKernel.Events;

/// <summary>Billing-attested post-payment attachment of a Licensing-verified enterprise policy.</summary>
[MemoryPackable]
public partial class EnterprisePolicyAttachedIntegrationEvent : IntegrationEvent
{
    /// <summary>Initializes an empty event for MemoryPack.</summary>
    [MemoryPackConstructor]
    public EnterprisePolicyAttachedIntegrationEvent()
    {
    }

    /// <summary>Gets or sets the canonical Customer tenant identity.</summary>
    public Guid TenantId { get; set; }
    /// <summary>Gets or sets the tenant slug.</summary>
    public string TenantIdentifier { get; set; } = string.Empty;
    /// <summary>Gets or sets the subscription identity.</summary>
    public Guid SubscriptionId { get; set; }
    /// <summary>Gets or sets the paid source order identity.</summary>
    public Guid SourceOrderId { get; set; }
    /// <summary>Gets or sets the operator-issued policy identity.</summary>
    public string PolicyId { get; set; } = string.Empty;
    /// <summary>Gets or sets the immutable policy version.</summary>
    public string PolicyVersion { get; set; } = string.Empty;
    /// <summary>Gets or sets the effective access grace period.</summary>
    public int GracePeriodDays { get; set; }
    /// <summary>Gets or sets the effective backup retention period.</summary>
    public int BackupRetentionDays { get; set; }
    /// <summary>Gets or sets the Licensing signing key identifier.</summary>
    public string LicensingSigningKeyId { get; set; } = string.Empty;
    /// <summary>Gets or sets the signed Licensing document type.</summary>
    public string LicenseKind { get; set; } = string.Empty;
    /// <summary>Gets or sets the detached Licensing signature.</summary>
    public string LicensingSignature { get; set; } = string.Empty;
    /// <summary>Gets or sets the inclusive policy validity start.</summary>
    public DateTimeOffset ValidFromUtc { get; set; }
    /// <summary>Gets or sets the exclusive policy validity end.</summary>
    public DateTimeOffset ValidUntilUtc { get; set; }
    /// <summary>Gets or sets Billing's detached proof for all transition claims.</summary>
    public string Proof { get; set; } = string.Empty;
}
