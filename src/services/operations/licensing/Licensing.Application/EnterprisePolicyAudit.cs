namespace Licensing.Application;

/// <summary>Audit record for policy issuance and revocation.</summary>
public sealed record EnterprisePolicyAudit(string PolicyId, string PolicyVersion, string ActorSubjectId, string Action, DateTimeOffset AtUtc);
