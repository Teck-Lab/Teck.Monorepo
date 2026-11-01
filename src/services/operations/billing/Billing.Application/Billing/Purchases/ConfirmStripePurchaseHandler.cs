using Billings.Domain.Entities;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;
using Wolverine.Attributes;

namespace Billings.Application.Billing.Purchases;

/// <summary>Confirms an order only after the trusted provider verifies an exact captured intent.</summary>
/// <param name="orders">The global Billing purchase repository.</param>
/// <param name="stripe">The provider-backed intent verifier.</param>
/// <param name="unitOfWork">The Billing persistence boundary.</param>
/// <param name="bus">The durable outgoing event publisher.</param>
/// <param name="signer">The Billing-only signing authority.</param>
/// <param name="tenantLock">The tenant-wide purchase and purge serialization lock.</param>
public sealed class ConfirmStripePurchaseHandler(
    IGenericWriteRepository<TenantPurchaseOrder, Guid> orders,
    IStripePurchaseGateway stripe,
    IUnitOfWork unitOfWork,
    IMessageBus bus,
    SubscriptionPaymentSigningKey signer,
    IPurchaseTenantLock tenantLock)
{
    /// <summary>Confirms one verified capture idempotently and publishes a signed event.</summary>
    /// <param name="command">The capture notification.</param>
    /// <param name="cancellationToken">Cancellation signal.</param>
    /// <returns>True only when the capture is verified or was already confirmed.</returns>
    [Transactional]
    public async Task<bool> Handle(ConfirmStripePurchaseCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.PaymentIntentId))
        {
            return false;
        }

        var candidate = await orders.FindOneAsync(item => item.StripePaymentIntentId == command.PaymentIntentId, false, cancellationToken)
            .ConfigureAwait(false);
        if (candidate is null)
        {
            return false;
        }

        if (!await tenantLock.AcquireAsync(candidate.TenantId, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var order = await orders.FindOneAsync(item => item.StripePaymentIntentId == command.PaymentIntentId, true, cancellationToken)
            .ConfigureAwait(false);
        if (order is null || order.TenantId != candidate.TenantId)
        {
            return false;
        }

        if (order.Status != "pending" && order.Status != "active" ||
            !await stripe.IsCapturedAsync(order, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        if (order.Status == "active")
        {
            return order.SubscriptionId != Guid.Empty;
        }

        var predecessor = await PurchaseRenewalPredecessor.FindAsync(orders, order, cancellationToken).ConfigureAwait(false);
        var confirmedAt = DateTimeOffset.UtcNow;
        if (predecessor is { ValidUntilUtc: { } priorExpiry })
        {
            order.ConfirmRenewalPayment(predecessor.SubscriptionId, priorExpiry, confirmedAt);
        }
        else
        {
            order.ConfirmCapturedPayment(Guid.NewGuid(), confirmedAt);
        }

        var paid = TenantPurchaseEventSnapshot.From(order, signer);
        paid.Proof = signer.Sign(paid);
        await bus.PublishAsync(paid).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}
