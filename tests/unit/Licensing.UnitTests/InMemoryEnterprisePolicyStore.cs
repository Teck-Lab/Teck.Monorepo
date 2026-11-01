using Licensing.Application;

namespace Licensing.UnitTests;

internal sealed class InMemoryEnterprisePolicyStore : IEnterprisePolicyStore
{
    private readonly Dictionary<(string PolicyId, string PolicyVersion), EnterprisePolicyDocument> policies = [];
    private readonly Dictionary<(string PolicyId, string PolicyVersion), EnterprisePolicyStatus> states = [];

    public List<EnterprisePolicyAudit> Audit { get; } = [];

    public void AddState(EnterprisePolicyDocument policy, EnterprisePolicyStatus status)
    {
        policies[(policy.PolicyId, policy.PolicyVersion)] = policy;
        states[(policy.PolicyId, policy.PolicyVersion)] = status;
    }

    public Task AddAsync(EnterprisePolicyDocument policy, EnterprisePolicyAudit audit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!policies.TryAdd((policy.PolicyId, policy.PolicyVersion), policy))
        {
            throw new InvalidOperationException("Policy versions are immutable.");
        }

        Audit.Add(audit);
        return Task.CompletedTask;
    }

    public Task<EnterprisePolicyDocument?> GetAsync(string policyId, string policyVersion, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        policies.TryGetValue((policyId, policyVersion), out var policy);
        return Task.FromResult(policy);
    }

    public Task<bool> RevokeAsync(string policyId, string policyVersion, string actorSubjectId, DateTimeOffset atUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!policies.Remove((policyId, policyVersion)))
        {
            return Task.FromResult(false);
        }

        Audit.Add(new EnterprisePolicyAudit(policyId, policyVersion, actorSubjectId, "revoked", atUtc));
        return Task.FromResult(true);
    }

    public Task<EnterprisePolicyState?> GetStateAsync(string policyId, string policyVersion, Guid tenantId, Guid subscriptionId,
        Guid sourceOrderId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!policies.TryGetValue((policyId, policyVersion), out var policy) || policy.TenantId != tenantId ||
            policy.SubscriptionId != subscriptionId || policy.SourceOrderId != sourceOrderId)
        {
            return Task.FromResult<EnterprisePolicyState?>(null);
        }

        var status = states.TryGetValue((policyId, policyVersion), out var persistedStatus) ? persistedStatus :
            nowUtc < policy.ValidFromUtc ? EnterprisePolicyStatus.NotYetValid : nowUtc >= policy.ValidUntilUtc ? EnterprisePolicyStatus.Expired : EnterprisePolicyStatus.Active;
        return Task.FromResult<EnterprisePolicyState?>(new EnterprisePolicyState(policy, status));
    }
}
