using MemoryPack;
using SharedKernel.Core.Events;

namespace SharedKernel.Events;

/// <summary>
/// Authoritative snapshot of one annual period confirmed by captured payment or audited external invoice.
/// Consumers MUST deduplicate by source order and retain the subscription ID across paid periods.
/// </summary>
[MemoryPackable]
public partial class SubscriptionPaymentConfirmedIntegrationEvent : IntegrationEvent
{
    /// <summary>Initializes a new instance of the <see cref="SubscriptionPaymentConfirmedIntegrationEvent"/> class.</summary>
    [MemoryPackConstructor]
    public SubscriptionPaymentConfirmedIntegrationEvent()
    {
    }

    /// <summary>Gets or sets the stable subscription identifier shared by all independently paid periods.</summary>
    public Guid SubscriptionId { get; set; }

    /// <summary>Gets or sets the global Customer registry identifier.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Gets or sets the globally unique tenant slug for downstream provisioning.</summary>
    public string TenantIdentifier { get; set; } = string.Empty;

    /// <summary>Gets or sets the originating purchase order identifier.</summary>
    public Guid SourceOrderId { get; set; }

    /// <summary>Gets or sets the immutable quoted plan identifier and version.</summary>
    public string PlanId { get; set; } = string.Empty;

    /// <summary>Gets or sets the immutable plan version.</summary>
    public string PlanVersion { get; set; } = string.Empty;

    /// <summary>Gets or sets the authoritative license features from the quoted plan version.</summary>
    public Dictionary<string, string> Features { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Gets or sets the per-location access point limit.</summary>
    public int? MaxAccessPointsPerLocation { get; set; }

    /// <summary>Gets or sets whether Barman scheduled backups were included in the paid order.</summary>
    public bool HasScheduledBackups { get; set; }

    /// <summary>Gets or sets whether high availability was included in the paid order.</summary>
    public bool HasHighAvailability { get; set; }

    /// <summary>Gets or sets the per-location device limit.</summary>
    public int? MaxDevicesPerLocation { get; set; }

    /// <summary>Gets or sets the per-location product limit.</summary>
    public int? MaxProductsPerLocation { get; set; }

    /// <summary>Gets or sets the tenant location limit.</summary>
    public int MaxLocations { get; set; }

    /// <summary>Gets or sets whether custom branding is included.</summary>
    public bool SupportsCustomBranding { get; set; }

    /// <summary>Gets or sets whether analytics are included.</summary>
    public bool SupportsAnalytics { get; set; }

    /// <summary>Gets or sets the captured amount in minor currency units.</summary>
    public long AmountMinor { get; set; }

    /// <summary>Gets or sets the ISO 4217 currency code.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Gets or sets the tenant database strategy snapshot.</summary>
    public string DatabaseStrategy { get; set; } = string.Empty;

    /// <summary>Gets or sets the tenant database provider snapshot.</summary>
    public string DatabaseProvider { get; set; } = string.Empty;

    /// <summary>Gets or sets whether the plan includes database read replicas.</summary>
    public bool HasReadReplicas { get; set; }

    /// <summary>Gets or sets the database read replica count.</summary>
    public int ReplicaCount { get; set; }

    /// <summary>Gets or sets the trusted confirmation source (`captured` or `external_invoice`).</summary>
    public string PaymentMethod { get; set; } = string.Empty;

    /// <summary>Gets or sets the confirmation time in UTC.</summary>
    public DateTimeOffset ConfirmedAtUtc { get; set; }

    /// <summary>Gets or sets the subscription validity start time in UTC.</summary>
    public DateTimeOffset ValidFromUtc { get; set; }

    /// <summary>Gets or sets the subscription validity end time in UTC.</summary>
    public DateTimeOffset ValidUntilUtc { get; set; }

    /// <summary>Gets or sets Billing's RSA-PSS attestation of every immutable paid entitlement and provisioning field.</summary>
    public string Proof { get; set; } = string.Empty;

    /// <summary>Gets or sets the previous paid source order; empty only for an original purchase.</summary>
    public Guid PreviousSourceOrderId { get; set; }
    /// <summary>Signed payload schema version; version 4 includes effective retention policy.</summary>
    public int PolicyVersion { get; set; }

    /// <summary>Operator-issued policy identity, null for default policy.</summary>
    public string? EnterprisePolicyId { get; set; }

    /// <summary>Immutable operator-issued policy version, null for default policy.</summary>
    public string? EnterprisePolicyVersion { get; set; }

    /// <summary>Effective grace period in days, defaults to seven.</summary>
    public int EffectiveGracePeriodDays { get; set; } = 7;

    /// <summary>Effective recovery retention in days, defaults to thirty.</summary>
    public int EffectiveBackupRetentionDays { get; set; } = 30;
}
