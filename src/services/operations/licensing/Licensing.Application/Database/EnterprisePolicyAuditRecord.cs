namespace Licensing.Application.Database;

/// <summary>Persisted audit event for policy issuance and revocation.</summary>
public sealed class EnterprisePolicyAuditRecord
{
    /// <summary>Gets or sets the generated audit identifier.</summary>
    public long Id { get; set; }
    /// <summary>Gets or sets the policy identifier.</summary>
    public string PolicyId { get; set; } = string.Empty;
    /// <summary>Gets or sets the immutable policy version.</summary>
    public string PolicyVersion { get; set; } = string.Empty;
    /// <summary>Gets or sets the authenticated human operator subject.</summary>
    public string ActorSubjectId { get; set; } = string.Empty;
    /// <summary>Gets or sets the action performed.</summary>
    public string Action { get; set; } = string.Empty;
    /// <summary>Gets or sets the UTC action time.</summary>
    public DateTimeOffset AtUtc { get; set; }
}
