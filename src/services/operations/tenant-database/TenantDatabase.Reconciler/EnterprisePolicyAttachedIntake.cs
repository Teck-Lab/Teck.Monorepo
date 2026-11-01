using SharedKernel.Events;
using Wolverine.Attributes;

namespace Teck.TenantDatabase.Reconciler;

/// <summary>Applies a policy transition to the matching durable paid database request.</summary>
[WolverineHandler]
public sealed class EnterprisePolicyAttachedIntake(
    ITenantDatabaseRequestStore store,
    SubscriptionPaymentVerificationKeys billingKeys,
    EnterprisePolicyDocumentVerificationKeys licensingKeys,
    IEnterprisePolicyFreshnessVerifier freshnessVerifier)
{
    /// <summary>Authenticates both policy issuers and atomically updates only the current paid order.</summary>
    public async Task Handle(EnterprisePolicyAttachedIntegrationEvent transition, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(transition);
        if (!billingKeys.Verify(transition) || !licensingKeys.Verify(transition, DateTimeOffset.UtcNow))
            throw new InvalidOperationException("Enterprise policy transition requires valid Billing and Licensing signatures.");
        await freshnessVerifier.VerifyAsync(transition, ct).ConfigureAwait(false);
        if (!await store.AttachEnterprisePolicyAsync(transition, ct).ConfigureAwait(false))
            throw new InvalidOperationException("Enterprise policy transition does not match the current durable paid tenant request.");
    }
}
