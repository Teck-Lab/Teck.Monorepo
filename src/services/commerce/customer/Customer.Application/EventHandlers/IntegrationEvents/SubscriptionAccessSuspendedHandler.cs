using Customers.Application.Database;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Events;

namespace Customers.Application.EventHandlers.IntegrationEvents;

/// <summary>Suspends tenant access only after Billing signs a correlated cancellation or failed renewal.</summary>
/// <param name="context">The global Customer tenant registry.</param>
/// <param name="billingKeys">Trusted Billing public keys, never its private signing authority.</param>
public sealed class SubscriptionAccessSuspendedHandler(CustomerDbContext context, SubscriptionPaymentVerificationKeys billingKeys)
{
    /// <summary>Applies an immutable suspension without allowing a delayed paid event to reopen access.</summary>
    /// <param name="message">The signed subscription access change.</param>
    /// <param name="cancellationToken">Cancellation signal.</param>
    /// <returns>A task completed after the registry update.</returns>
    public async Task Handle(SubscriptionAccessSuspendedIntegrationEvent message, CancellationToken cancellationToken)
    {
        if (!billingKeys.Verify(message))
        {
            throw new InvalidOperationException("Subscription suspension was not attested by Billing.");
        }

        var tenant = await context.Tenants.SingleOrDefaultAsync(item => item.Id == message.TenantId,
            cancellationToken).ConfigureAwait(false);
        if (tenant is null || !string.Equals(tenant.Identifier, message.TenantIdentifier, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Subscription suspension does not match a Customer reservation.");
        }

        if (tenant.PreviousPaidOrderId == message.SourceOrderId &&
            tenant.SubscriptionId == message.SubscriptionId && tenant.PurchaseOrderId != message.SourceOrderId)
        {
            return;
        }

        tenant.SuspendSubscription(message.SourceOrderId, message.SubscriptionId, message.SuspendedAtUtc);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
