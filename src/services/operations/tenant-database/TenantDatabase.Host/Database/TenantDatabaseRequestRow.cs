#pragma warning disable SA1600

namespace Teck.TenantDatabase.Host.Database;

/// <summary>Storage-only representation of the request queue row.</summary>
public sealed class TenantDatabaseRequestRow
{
    public Guid SubscriptionId { get; set; }

    public Guid TenantId { get; set; }

    public string DesiredState { get; set; } = null!;

    public string Phase { get; set; } = null!;

    public string? Detail { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? LeaseUntil { get; set; }

    public Guid SourceOrderId { get; set; }

    public string Proof { get; set; } = null!;

    public Guid PreviousSourceOrderId { get; set; }

    public Guid? EvidenceId { get; set; }

    public DateTimeOffset? ReadinessPublishedAt { get; set; }

    public Guid? LastClusterUid { get; set; }

    public Guid? PurgeApprovalId { get; set; }

    public string? PurgeProof { get; set; }

    public string? PurgeMessage { get; set; }

    public Guid? PurgeBackupUid { get; set; }

    public Guid? PurgeRestoreUid { get; set; }

    public string? PurgeBackupName { get; set; }

    public DateTimeOffset? PurgeVerifiedAt { get; set; }

    public DateTimeOffset? PurgedAt { get; set; }
}
