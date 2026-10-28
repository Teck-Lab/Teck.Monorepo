using Customers.Application.Database;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Events;

namespace Customers.Application.EventHandlers.IntegrationEvents;

/// <summary>Applies a trusted Billing confirmation while keeping database/license access gated.</summary>
/// <param name="context">The global Customer tenant registry.</param>
/// <param name="organizations">The Keycloak organization and owner membership boundary.</param>
/// <param name="billingKeys">Billing's public-only payment attestation keys.</param>
public sealed class SubscriptionPaymentConfirmedHandler(CustomerDbContext context, IOrganizationProvisioner organizations, SubscriptionPaymentVerificationKeys billingKeys)
{
    /// <summary>Correlates and applies the immutable paid-subscription snapshot idempotently.</summary>
    /// <param name="message">The authoritative Billing subscription snapshot.</param>
    /// <param name="cancellationToken">Cancellation signal.</param>
    /// <returns>A completed task when applied or already processed.</returns>
    public async Task Handle(SubscriptionPaymentConfirmedIntegrationEvent message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!billingKeys.Verify(message))
        {
            throw new InvalidOperationException("Confirmed payment was not attested by Billing.");
        }

        var tenant = await context.Tenants.SingleOrDefaultAsync(item => item.Id == message.TenantId,
            cancellationToken).ConfigureAwait(false);
        if (tenant is null || !string.Equals(tenant.Identifier, message.TenantIdentifier, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Confirmed payment tenant identity does not match the Customer reservation.");
        }

        if (tenant.PurchaseOrderId == message.SourceOrderId &&
            tenant.PreviousPaidOrderId != message.PreviousSourceOrderId)
        {
            throw new InvalidOperationException("Confirmed payment predecessor differs from the recorded Billing period.");
        }

        if (tenant.PurchaseOrderId == message.SourceOrderId && tenant.SubscriptionId == message.SubscriptionId &&
            tenant.ValidUntilUtc is DateTimeOffset recordedUntilUtc && recordedUntilUtc.UtcTicks / 10 != message.ValidUntilUtc.UtcTicks / 10)
        {
            throw new InvalidOperationException("Confirmed payment period differs from the recorded Billing order.");
        }

        if (tenant.PurchaseOrderId == message.SourceOrderId && tenant.SubscriptionId == message.SubscriptionId)
        {
            if (tenant.Status == "suspended" || tenant.KeycloakOrganizationId.HasValue)
            {
                return;
            }
        }

        if (message.PreviousSourceOrderId != Guid.Empty)
        {
            tenant.ApplyRenewalConfirmation(message.PreviousSourceOrderId, message.SourceOrderId,
                message.SubscriptionId, message.PlanId, message.PlanVersion, message.DatabaseStrategy,
                message.DatabaseProvider, message.HasReadReplicas, message.ValidUntilUtc);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (tenant.PurchaseOrderId != Guid.Empty && tenant.PurchaseOrderId != message.SourceOrderId ||
            tenant.SubscriptionId != Guid.Empty && tenant.SubscriptionId != message.SubscriptionId)
        {
            throw new InvalidOperationException("Tenant reservation is correlated with a different purchase order.");
        }

        if (tenant.PurchaseOrderId == Guid.Empty)
        {
            tenant.ApplyPurchaseConfirmation(message.SourceOrderId, message.SubscriptionId, message.PlanId, message.PlanVersion,
                message.DatabaseStrategy, message.DatabaseProvider, message.HasReadReplicas, message.ValidUntilUtc);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var organizationId = await organizations.EnsureOwnerOrganizationAsync(tenant.Id, tenant.Identifier, tenant.OwnerSubjectId,
            cancellationToken).ConfigureAwait(false);
        tenant.LinkOrganization(organizationId);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
