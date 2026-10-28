using MemoryPack;
using SharedKernel.Core.Events;

namespace SharedKernel.Events;

/// <summary>Signals readiness from one downstream tenant provisioner.</summary>
[MemoryPackable]
public partial class TenantProvisioningReadyIntegrationEvent : IntegrationEvent
{
    /// <summary>Gets or sets the global Customer tenant ID.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Gets or sets the authoritative Billing purchase order ID.</summary>
    public Guid SourceOrderId { get; set; }

    /// <summary>Gets or sets the immutable paid subscription ID.</summary>
    public Guid SubscriptionId { get; set; }

    /// <summary>Gets or sets the provisioner kind, either <c>license</c> or <c>database</c>.</summary>
    public string Provisioner { get; set; } = string.Empty;

    /// <summary>Gets or sets the persisted license ID or ready CNPG Cluster UID.</summary>
    public Guid EvidenceId { get; set; }

    /// <summary>Gets or sets the per-provisioner HMAC-SHA256 attestation of the immutable readiness fields.</summary>
    public string Proof { get; set; } = string.Empty;
}
