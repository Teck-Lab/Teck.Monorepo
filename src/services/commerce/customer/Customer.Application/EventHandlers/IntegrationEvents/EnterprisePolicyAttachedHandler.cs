using Customers.Domain.Entities;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine.Attributes;

namespace Customers.Application.EventHandlers.IntegrationEvents;

/// <summary>Persists a Billing-attested, Licensing-verified policy for the current paid tenant order.</summary>
/// <param name="tenants">Customer tenant write repository.</param>
/// <param name="unitOfWork">Atomic Customer persistence boundary.</param>
/// <param name="billingKeys">Trusted Billing attestation public keys.</param>
/// <param name="licensingKeys">Trusted Licensing policy public keys.</param>
/// <param name="freshnessVerifier">Live Licensing status lookup.</param>
[WolverineHandler]
public sealed class EnterprisePolicyAttachedHandler(
    IGenericWriteRepository<Tenant, Guid> tenants,
    IUnitOfWork unitOfWork,
    SubscriptionPaymentVerificationKeys billingKeys,
    EnterprisePolicyDocumentVerificationKeys licensingKeys,
    IEnterprisePolicyFreshnessVerifier freshnessVerifier)
{
    /// <summary>Verifies and durably applies an immutable policy transition.</summary>
    /// <param name="message">Signed Billing policy transition.</param>
    /// <param name="cancellationToken">Cancellation signal.</param>
    /// <returns>A task that completes after an authenticated transition is saved.</returns>
    public async Task Handle(EnterprisePolicyAttachedIntegrationEvent message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        await freshnessVerifier.VerifyAsync(message, cancellationToken).ConfigureAwait(false);
        if (!billingKeys.Verify(message) || !licensingKeys.Verify(message, DateTimeOffset.UtcNow))
        {
            throw new InvalidOperationException("Enterprise policy transition requires valid Billing and Licensing signatures.");
        }

        var tenant = await tenants.FindOneAsync(item => item.Id == message.TenantId, enableTracking: true, cancellationToken).ConfigureAwait(false);
        if (tenant is null || !string.Equals(tenant.Identifier, message.TenantIdentifier, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Enterprise policy tenant identity does not match Customer registry.");
        }

        var changed = tenant.ApplyEnterprisePolicy(message.SourceOrderId, message.SubscriptionId, message.PolicyId, message.PolicyVersion,
            message.GracePeriodDays, message.BackupRetentionDays, message.LicensingSigningKeyId, message.LicensingSignature,
            message.ValidFromUtc, message.ValidUntilUtc, message.Proof);
        if (changed)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
