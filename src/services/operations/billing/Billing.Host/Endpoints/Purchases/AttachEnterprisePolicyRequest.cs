namespace Billings.Host.Endpoints.Purchases;

/// <summary>Operator-supplied exact Licensing policy identity and version.</summary>
public sealed class AttachEnterprisePolicyRequest
{
    /// <summary>Gets or sets the exact policy identity.</summary>
    public string PolicyId { get; set; } = string.Empty;
    /// <summary>Gets or sets the exact immutable policy version.</summary>
    public string PolicyVersion { get; set; } = string.Empty;
}
