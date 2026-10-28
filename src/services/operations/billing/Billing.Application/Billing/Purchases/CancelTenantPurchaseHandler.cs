using Billings.Application.Database;
using Billings.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Events;
using Wolverine;
using Wolverine.Attributes;

namespace Billings.Application.Billing.Purchases;

/// <summary>Cancels a confirmed purchase through Wolverine's transactional message boundary.</summary>
/// <param name="context">The Billing write context.</param>
/// <param name="bus">The durable outgoing event publisher.</param>
/// <param name="signer">The Billing-only subscription signing key.</param>
public sealed class CancelTenantPurchaseHandler(BillingDbContext context, IMessageBus bus, SubscriptionPaymentSigningKey signer)
{
    /// <summary>Suspends an active paid order once; repeated owner requests do not republish.</summary>
    /// <param name="command">The signed owner's cancellation request.</param>
    /// <param name="cancellationToken">Cancellation signal.</param>
    /// <returns>The cancelled order, or null if the owner does not own it.</returns>
    [Transactional]
    public async Task<TenantPurchaseOrder?> Handle(CancelTenantPurchaseCommand command, CancellationToken cancellationToken)
    {
        if (command.OrderId == Guid.Empty || string.IsNullOrWhiteSpace(command.OwnerSubjectId))
        {
            return null;
        }

        var order = await context.TenantPurchaseOrders.SingleOrDefaultAsync(item => item.Id == command.OrderId,
            cancellationToken).ConfigureAwait(false);
        if (order is null || !string.Equals(order.BeneficiarySubjectId, command.OwnerSubjectId, StringComparison.Ordinal))
        {
            return null;
        }

        if (order.Status == "cancelled")
        {
            return order;
        }

        if (await context.TenantPurchaseOrders.AnyAsync(next =>
                next.PreviousPurchaseOrderId == order.Id && next.Status == "active" &&
                next.SubscriptionId == order.SubscriptionId && next.ValidUntilUtc > order.ValidUntilUtc,
                cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("A superseded paid period cannot cancel the renewed subscription.");
        }

        var cancelledAt = DateTimeOffset.UtcNow;
        order.Cancel(cancelledAt);
        var suspended = new SubscriptionAccessSuspendedIntegrationEvent
        {
            TenantId = order.TenantId,
            TenantIdentifier = order.TenantIdentifier,
            SourceOrderId = order.Id,
            SubscriptionId = order.SubscriptionId,
            Reason = "cancelled",
            SuspendedAtUtc = cancelledAt,
        };
        suspended.Proof = signer.Sign(suspended);
        await bus.PublishAsync(suspended).ConfigureAwait(false);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return order;
    }
}
