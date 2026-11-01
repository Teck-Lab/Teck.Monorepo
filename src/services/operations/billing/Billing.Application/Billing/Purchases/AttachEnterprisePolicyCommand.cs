namespace Billings.Application.Billing.Purchases;

/// <summary>Requests an exact operator-issued policy attachment for an existing paid order.</summary>
/// <param name="OrderId">The immutable paid source-order identifier.</param>
/// <param name="PolicyId">The exact Licensing policy identity.</param>
/// <param name="PolicyVersion">The exact immutable Licensing policy version.</param>
/// <param name="RequestedBySubjectId">The authenticated operator subject.</param>
public sealed record AttachEnterprisePolicyCommand(Guid OrderId, string PolicyId, string PolicyVersion, string RequestedBySubjectId);
