using Billings.Domain.Entities;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;
using Wolverine.Attributes;

namespace Billings.Application.Billing.Purchases;

/// <summary>Cancels a confirmed purchase through Wolverine's transactional message boundary.</summary>
/// <param name="orders">The global Billing purchase repository.</param>
/// <param name="unitOfWork">The commit boundary enlisted with Wolverine's outbox.</param>
/// <param name="bus">The durable outgoing event publisher.</param>
/// <param name="signer">The Billing-only subscription signing key.</param>
/// <param name="tenantLock">The tenant-wide purchase and purge serialization lock.</param>
public sealed class CancelTenantPurchaseHandler(
    IGenericWriteRepository<TenantPurchaseOrder, Guid> orders,
    IUnitOfWork unitOfWork,
    IMessageBus bus,
    SubscriptionPaymentSigningKey signer,
    IPurchaseTenantLock tenantLock)
{
    /// <summary>Cancels the owner's paid order and publishes a signed access suspension.</summary>
    /// <param name="command">The order and authenticated owner.</param>
    /// <param name="cancellationToken">Cancellation signal.</param>
    /// <returns>The cancelled order, or null when it is missing or belongs to another owner.</returns>
    [Transactional]
    public async Task<TenantPurchaseOrderDto?> Handle(CancelTenantPurchaseCommand command, CancellationToken cancellationToken)
    {
        if (command.OrderId == Guid.Empty || string.IsNullOrWhiteSpace(command.OwnerSubjectId))
        {
            return null;
        }

        var candidate = await orders.FindOneAsync(item => item.Id == command.OrderId, false, cancellationToken).ConfigureAwait(false);
        if (candidate is null || !string.Equals(candidate.BeneficiarySubjectId, command.OwnerSubjectId, StringComparison.Ordinal))
        {
            return null;
        }

        if (!await tenantLock.AcquireAsync(candidate.TenantId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var order = await orders.FindOneAsync(item => item.Id == command.OrderId, true, cancellationToken).ConfigureAwait(false);
        if (order is null || order.TenantId != candidate.TenantId ||
            !string.Equals(order.BeneficiarySubjectId, command.OwnerSubjectId, StringComparison.Ordinal))
        {
            return null;
        }

        if (order.Status == "cancelled")
        {
            return TenantPurchaseOrderMapping.ToDto(order);
        }

        if (order.Status == "pending" && order.PaymentMethod == "stripe")
        {
            throw new InvalidOperationException("A pending Stripe payment cannot be cancelled while provider capture is uncertain.");
        }

        if (await orders.ExistsAsync(next => next.PreviousPurchaseOrderId == order.Id && next.Status == "active" &&
                next.SubscriptionId == order.SubscriptionId && next.ValidUntilUtc > order.ValidUntilUtc,
                false, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("An active renewal already supersedes this paid period.");
        }

        var now = DateTimeOffset.UtcNow;
        order.Cancel(now);

        var suspended = new SubscriptionAccessSuspendedIntegrationEvent
        {
            TenantId = order.TenantId,
            TenantIdentifier = order.TenantIdentifier,
            SourceOrderId = order.Id,
            SubscriptionId = order.SubscriptionId,
            Reason = "cancelled",
            SuspendedAtUtc = now,
        };
        suspended.Proof = signer.Sign(suspended);
        await bus.PublishAsync(suspended).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return TenantPurchaseOrderMapping.ToDto(order);
    }
}
