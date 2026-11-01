using SharedKernel.Events;

namespace Licensing.Application;

/// <summary>Revokes subscription licensing on an authenticated Billing suspension.</summary>
/// <param name="issuer">The persistent signed-license issuer.</param>
/// <param name="billingKeys">Trusted Billing public payment/suspension keys.</param>
public sealed class SubscriptionAccessSuspendedHandler(LicenseIssuer issuer, SubscriptionPaymentVerificationKeys billingKeys)
{
    /// <summary>Stores a permanent cancellation tombstone, even when the paid event is still in transit.</summary>
    /// <param name="suspension">Billing's signed suspension event.</param>
    /// <param name="cancellationToken">Cancellation signal.</param>
    /// <returns>A task for the durable revocation.</returns>
    public async Task Handle(SubscriptionAccessSuspendedIntegrationEvent suspension, CancellationToken cancellationToken)
    {
        if (!billingKeys.Verify(suspension))
        {
            throw new InvalidOperationException("Subscription suspension was not attested by Billing.");
        }

        if (suspension.Reason is not ("cancelled" or "renewal_failed"))
        {
            throw new InvalidOperationException("Subscription suspension reason is invalid.");
        }

        await issuer.SuspendAsync(suspension.SourceOrderId, suspension.SubscriptionId, suspension.TenantId,
            suspension.SuspendedAtUtc, cancellationToken);
    }
}
