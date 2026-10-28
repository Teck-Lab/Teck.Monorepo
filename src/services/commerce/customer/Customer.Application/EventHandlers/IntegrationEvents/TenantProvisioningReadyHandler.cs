using Microsoft.EntityFrameworkCore;
using SharedKernel.Events;

namespace Customers.Application.EventHandlers.IntegrationEvents;

/// <summary>Applies one idempotent downstream readiness signal to the global tenant registry.</summary>
/// <param name="context">The global Customer registry.</param>
/// <param name="verifier">The configured issuer-specific readiness proof verifier.</param>
public sealed class TenantProvisioningReadyHandler(global::Customers.Application.Database.CustomerDbContext context, TenantReadinessVerifier verifier)
{
    /// <summary>Marks a tenant ready for one downstream system.</summary>
    /// <param name="message">The readiness signal.</param>
    /// <param name="cancellationToken">Cancellation signal.</param>
    /// <returns>A completed task after persistence.</returns>
    public async Task Handle(TenantProvisioningReadyIntegrationEvent message, CancellationToken cancellationToken)
    {
        var tenant = await context.Tenants.SingleOrDefaultAsync(item => item.Id == message.TenantId,
            cancellationToken).ConfigureAwait(false);
        if (tenant is null)
        {
            throw new InvalidOperationException("Readiness signal references an unknown tenant.");
        }

        if (message.SourceOrderId == Guid.Empty || message.SubscriptionId == Guid.Empty ||
            tenant.PurchaseOrderId != message.SourceOrderId || tenant.SubscriptionId != message.SubscriptionId)
        {
            throw new InvalidOperationException("Readiness is not correlated with the confirmed subscription order.");
        }

        if (!verifier.Verify(message))
        {
            throw new InvalidOperationException("Readiness was not attested by its provisioner.");
        }

        tenant.MarkProvisionerReady(message.Provisioner);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
