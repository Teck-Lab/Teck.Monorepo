namespace Licensing.Application;

/// <summary>Persistent storage contract for signed policies and their audit trail.</summary>
public interface IEnterprisePolicyStore
{
    /// <summary>Persists a signed policy and its issuance audit record.</summary>
    /// <param name="policy">The immutable signed policy document.</param>
    /// <param name="audit">The issuance event to persist atomically with the policy.</param>
    /// <param name="cancellationToken">A token to observe while the operation completes.</param>
    /// <returns>A task representing completion of the durable write.</returns>
    Task AddAsync(EnterprisePolicyDocument policy, EnterprisePolicyAudit audit, CancellationToken cancellationToken);

    /// <summary>Returns the exact immutable policy version, if present.</summary>
    /// <param name="policyId">The policy identifier.</param>
    /// <param name="policyVersion">The immutable policy version.</param>
    /// <param name="cancellationToken">A token to observe while the operation completes.</param>
    /// <returns>The policy document or <see langword="null"/> when no exact version exists.</returns>
    Task<EnterprisePolicyDocument?> GetAsync(string policyId, string policyVersion, CancellationToken cancellationToken);

    /// <summary>Revokes an exact policy version and durably records the authorized actor.</summary>
    /// <param name="policyId">The policy identifier.</param>
    /// <param name="policyVersion">The immutable policy version.</param>
    /// <param name="actorSubjectId">The subject identifier of the revoking operator.</param>
    /// <param name="atUtc">The revocation timestamp in UTC.</param>
    /// <param name="cancellationToken">A token to observe while the operation completes.</param>
    /// <returns><see langword="true"/> when revoked; otherwise, <see langword="false"/>.</returns>
    Task<bool> RevokeAsync(string policyId, string policyVersion, string actorSubjectId, DateTimeOffset atUtc, CancellationToken cancellationToken);

    /// <summary>Returns policy status only when tenant, subscription and source order match the signed document.</summary>
    /// <param name="policyId">The policy identifier.</param>
    /// <param name="policyVersion">The immutable policy version.</param>
    /// <param name="tenantId">The expected tenant identifier.</param>
    /// <param name="subscriptionId">The expected subscription identifier.</param>
    /// <param name="sourceOrderId">The expected source order identifier.</param>
    /// <param name="nowUtc">The UTC instant at which to evaluate validity.</param>
    /// <param name="cancellationToken">A token to observe while the operation completes.</param>
    /// <returns>The matching policy and lifecycle status, or <see langword="null"/> for missing/mismatched identity.</returns>
    Task<EnterprisePolicyState?> GetStateAsync(string policyId, string policyVersion, Guid tenantId, Guid subscriptionId, Guid sourceOrderId, DateTimeOffset nowUtc, CancellationToken cancellationToken);
}
