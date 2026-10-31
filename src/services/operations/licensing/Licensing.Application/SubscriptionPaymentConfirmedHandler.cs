using SharedKernel.Events;

namespace Licensing.Application;

/// <summary>Consumes Billing's confirmed subscription snapshot and cascades a signed readiness result.</summary>
/// <param name="issuer">The persistent signed-license issuer.</param>
/// <param name="readinessKey">The licensing-only readiness proof key.</param>
/// <param name="billingKeys">Trusted Billing public keys; Licensing never has Billing's private payment key.</param>
public sealed class SubscriptionPaymentConfirmedHandler(LicenseIssuer issuer, ReadinessSigningKey readinessKey, SubscriptionPaymentVerificationKeys billingKeys)
{
    /// <summary>Issues idempotently and reports readiness only after the signed license is persisted.</summary>
    /// <param name="paid">The captured or audited externally invoiced subscription.</param>
    /// <param name="cancellationToken">Cancellation signal.</param>
    /// <returns>An authenticated readiness event for Wolverine to publish.</returns>
    public async Task<TenantProvisioningReadyIntegrationEvent> Handle(SubscriptionPaymentConfirmedIntegrationEvent paid, CancellationToken cancellationToken)
    {
        if (!billingKeys.Verify(paid))
        {
            throw new InvalidOperationException("Paid subscription was not attested by Billing.");
        }

        if (paid.SourceOrderId == Guid.Empty || paid.SubscriptionId == Guid.Empty || paid.TenantId == Guid.Empty ||
            paid.PaymentMethod is not ("captured" or "external_invoice") ||
            paid.ValidFromUtc == default || paid.ValidUntilUtc <= paid.ValidFromUtc ||
            paid.ValidUntilUtc <= DateTimeOffset.UtcNow)
        {
            throw new InvalidOperationException("A current confirmed subscription is required before licensing.");
        }

        var license = paid.PreviousSourceOrderId == Guid.Empty
            ? await issuer.IssueAsync(paid.SourceOrderId, paid.SubscriptionId, paid.TenantId,
                paid.PlanId, paid.PlanVersion, paid.Features, paid.ValidFromUtc,
                paid.ValidUntilUtc - paid.ValidFromUtc, cancellationToken).ConfigureAwait(false)
            : await issuer.IssuePaidRenewalAsync(paid.PreviousSourceOrderId, paid.SourceOrderId,
                paid.SubscriptionId, paid.TenantId, paid.PlanId, paid.PlanVersion, paid.Features,
                paid.ConfirmedAtUtc, paid.ValidUntilUtc, cancellationToken).ConfigureAwait(false);
        var ready = new TenantProvisioningReadyIntegrationEvent
        {
            TenantId = paid.TenantId,
            SourceOrderId = paid.SourceOrderId,
            SubscriptionId = paid.SubscriptionId,
            Provisioner = "license",
            EvidenceId = license.LicenseId,
        };
        ready.Proof = readinessKey.Sign(ready);
        return ready;
    }
}
