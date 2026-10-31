using SharedKernel.Events;
using Wolverine.Attributes;

namespace Teck.TenantDatabase.Reconciler;

/// <summary>Admits only Billing-attested paid subscription requests to the durable CNPG queue.</summary>
[WolverineHandler]
public sealed class SignedTenantDatabaseIntake(ITenantDatabaseRequestStore store, SubscriptionPaymentVerificationKeys billingKeys)
{
    /// <summary>Validates the immutable Billing snapshot before creating one subscription request.</summary>
    public async Task Handle(SubscriptionPaymentConfirmedIntegrationEvent paid, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(paid);
        if (!billingKeys.Verify(paid) || paid.SourceOrderId == Guid.Empty || paid.SubscriptionId == Guid.Empty ||
            paid.TenantId == Guid.Empty || string.IsNullOrWhiteSpace(paid.TenantIdentifier) ||
            paid.PaymentMethod is not ("captured" or "external_invoice") ||
            paid.ValidFromUtc == default || paid.ValidUntilUtc <= paid.ValidFromUtc ||
            paid.ValidUntilUtc <= DateTimeOffset.UtcNow ||
            paid.ReplicaCount is < 0 or > 3 || paid.HasReadReplicas != (paid.ReplicaCount > 0))
        {
            throw new InvalidOperationException("A current Billing-attested paid subscription is required for database provisioning.");
        }

        if (!string.Equals(paid.DatabaseStrategy, "dedicated", StringComparison.Ordinal))
        {
            return;
        }

        if (!string.Equals(paid.DatabaseProvider, "postgres", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Dedicated tenant database requires PostgreSQL.");
        }

        var desired = new TenantDatabaseDesiredState(paid.TenantId.ToString("D"), paid.TenantIdentifier,
            paid.DatabaseStrategy, paid.DatabaseProvider, paid.ReplicaCount, TenantSubscriptionStatus.Active, null);
        await store.UpsertAsync(new TenantDatabaseRequest(paid.SubscriptionId, desired, paid.ConfirmedAtUtc,
            paid.SourceOrderId, paid.Proof, paid.PreviousSourceOrderId), ct).ConfigureAwait(false);
    }
}
