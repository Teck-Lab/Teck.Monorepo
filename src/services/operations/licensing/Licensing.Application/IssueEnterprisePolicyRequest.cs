namespace Licensing.Application;

/// <summary>Immutable policy terms requested by an operator for a confirmed purchase.</summary>
public sealed record IssueEnterprisePolicyRequest(Guid TenantId, Guid SubscriptionId, Guid SourceOrderId, string PolicyId,
    string PolicyVersion, int GracePeriodDays, int BackupRetentionDays, DateTimeOffset ValidFromUtc, DateTimeOffset ValidUntilUtc);
