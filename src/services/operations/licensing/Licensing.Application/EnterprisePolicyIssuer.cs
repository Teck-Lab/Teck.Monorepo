using System.Security.Cryptography;

namespace Licensing.Application;

/// <summary>Issues immutable policy versions only to an appropriately authorized human operator.</summary>
/// <param name="store">Durable policy and audit persistence.</param>
/// <param name="privateKey">Active RSA policy signing key.</param>
/// <param name="signingKeyId">Public key identifier.</param>
public sealed class EnterprisePolicyIssuer(IEnterprisePolicyStore store, RSA privateKey, string signingKeyId)
{
    private const string RequiredScope = "licensing:enterprise-policy:issue";
    private const string RequiredRole = "licensing-enterprise-policy-issuer";

    /// <summary>Signs and durably records a policy after checking operator identity and request claims.</summary>
    /// <param name="actor">The authorized human operator issuing the policy.</param>
    /// <param name="request">The immutable policy claims to sign and persist.</param>
    /// <param name="nowUtc">The UTC issuance and validity-evaluation timestamp.</param>
    /// <param name="cancellationToken">A token to observe while the operation completes.</param>
    /// <returns>The signed and durably persisted policy document.</returns>
    public async Task<EnterprisePolicyDocument> IssueAsync(EnterprisePolicyOperator actor, IssueEnterprisePolicyRequest request, DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);
        ValidateOperator(actor);
        var policy = EnterprisePolicyDocument.Sign(request.TenantId, request.SubscriptionId, request.SourceOrderId, request.PolicyId, request.PolicyVersion,
            request.GracePeriodDays, request.BackupRetentionDays, request.ValidFromUtc, request.ValidUntilUtc, privateKey, signingKeyId);
        if (!policy.Verify(request.TenantId, request.SubscriptionId, request.SourceOrderId, nowUtc, privateKey))
        {
            throw new InvalidOperationException("The issued enterprise policy failed its signature or binding verification.");
        }

        await store.AddAsync(policy, new EnterprisePolicyAudit(policy.PolicyId, policy.PolicyVersion, actor.Subject, "issued", nowUtc), cancellationToken)
            .ConfigureAwait(false);
        return policy;
    }

    /// <param name="policyId">The policy identifier.</param>
    /// <param name="policyVersion">The immutable policy version.</param>
    /// <param name="cancellationToken">A token to observe while the operation completes.</param>
    /// <returns>The exact policy document or <see langword="null"/> if it is not present.</returns>
    public Task<EnterprisePolicyDocument?> GetAsync(string policyId, string policyVersion, CancellationToken cancellationToken) =>
        store.GetAsync(policyId, policyVersion, cancellationToken);

    /// <summary>Revokes a policy version under the dedicated operator identity.</summary>
    /// <param name="actor">The authorized human operator requesting revocation.</param>
    /// <param name="policyId">The policy identifier.</param>
    /// <param name="policyVersion">The immutable policy version.</param>
    /// <param name="nowUtc">The UTC revocation timestamp.</param>
    /// <param name="cancellationToken">A token to observe while the operation completes.</param>
    /// <returns><see langword="true"/> when revoked; otherwise, <see langword="false"/>.</returns>
    public Task<bool> RevokeAsync(EnterprisePolicyOperator actor, string policyId, string policyVersion, DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ValidateOperator(actor);
        return store.RevokeAsync(policyId, policyVersion, actor.Subject, nowUtc, cancellationToken);
    }

    /// <summary>Retrieves a policy and its status after matching all signed identity bindings.</summary>
    /// <param name="policyId">The policy identifier.</param>
    /// <param name="policyVersion">The immutable policy version.</param>
    /// <param name="tenantId">The expected tenant identifier.</param>
    /// <param name="subscriptionId">The expected subscription identifier.</param>
    /// <param name="sourceOrderId">The expected source order identifier.</param>
    /// <param name="nowUtc">The UTC instant at which to evaluate validity.</param>
    /// <param name="cancellationToken">A token to observe while the operation completes.</param>
    /// <returns>The matching policy and state, or <see langword="null"/> for missing/mismatched identity.</returns>
    public Task<EnterprisePolicyState?> GetStateAsync(string policyId, string policyVersion, Guid tenantId, Guid subscriptionId, Guid sourceOrderId,
        DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
        store.GetStateAsync(policyId, policyVersion, tenantId, subscriptionId, sourceOrderId, nowUtc, cancellationToken);

    private static void ValidateOperator(EnterprisePolicyOperator actor)
    {
        if (string.IsNullOrWhiteSpace(actor.Subject) || actor.Subject.StartsWith("service-account-", StringComparison.Ordinal) || actor.Audience != "licensing-api" ||
            !actor.Scopes.Contains(RequiredScope) || !actor.Roles.Contains(RequiredRole))
        {
            throw new UnauthorizedAccessException("A human licensing enterprise policy issuer is required.");
        }
    }
}
