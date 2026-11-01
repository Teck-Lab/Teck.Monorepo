namespace Licensing.Application.Database;

/// <summary>Persisted immutable signed enterprise policy version.</summary>
public sealed class EnterprisePolicyRecord
{
    /// <summary>Gets or sets policy identifier.</summary>
    public string PolicyId { get; set; } = string.Empty;
    /// <summary>Gets or sets immutable policy version.</summary>
    public string PolicyVersion { get; set; } = string.Empty;
    /// <summary>Gets or sets serialized signed policy document.</summary>
    public string Document { get; set; } = string.Empty;
    /// <summary>Gets or sets the policy issuance time used to determine the latest version.</summary>
    public DateTimeOffset IssuedAtUtc { get; set; }
    /// <summary>Gets or sets signed tenant identity for indexed purchase binding.</summary>
    public Guid TenantId { get; set; }
    /// <summary>Gets or sets signed subscription identity for indexed purchase binding.</summary>
    public Guid SubscriptionId { get; set; }
    /// <summary>Gets or sets signed source-order identity for indexed purchase binding.</summary>
    public Guid SourceOrderId { get; set; }
    /// <summary>Gets or sets policy revocation time, if revoked.</summary>
    public DateTimeOffset? RevokedAtUtc { get; set; }
    /// <summary>Gets or sets human subject who revoked policy.</summary>
    public string? RevokedBySubjectId { get; set; }
}
