using Customers.Domain.Entities;
using SharedKernel.Core.Database;
using SharedKernel.Events;

namespace Customers.Application.EventHandlers.IntegrationEvents;

/// <summary>Suspends tenant access only after Billing signs a correlated cancellation or failed renewal.</summary>
/// <param name="tenants">Tracked global Customer registry repository.</param>
/// <param name="unitOfWork">Single registry commit boundary.</param>
/// <param name="billingKeys">Trusted Billing public keys, never its private signing authority.</param>
public sealed class SubscriptionAccessSuspendedHandler(IGenericWriteRepository<Tenant, Guid> tenants,
    IUnitOfWork unitOfWork, SubscriptionPaymentVerificationKeys billingKeys)
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

        var tenant = await tenants.FindOneAsync(item => item.Id == message.TenantId,
            enableTracking: true, cancellationToken).ConfigureAwait(false);
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
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
