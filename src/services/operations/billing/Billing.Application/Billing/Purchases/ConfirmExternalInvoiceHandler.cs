using Billings.Domain.Entities;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;
using Wolverine.Attributes;

namespace Billings.Application.Billing.Purchases;

/// <summary>Confirms an external invoice through Wolverine transactional handling.</summary>
/// <param name="orders">The global Billing purchase store.</param>
/// <param name="unitOfWork">The Billing commit boundary enlisted by Wolverine.</param>
/// <param name="bus">The Wolverine message bus.</param>
/// <param name="signer">The Billing-only private key for paid event attestation.</param>
/// <param name="tenantLock">The tenant-wide purchase and purge serialization lock.</param>
public sealed class ConfirmExternalInvoiceHandler(
    IGenericWriteRepository<TenantPurchaseOrder, Guid> orders,
    IUnitOfWork unitOfWork,
    IMessageBus bus,
    SubscriptionPaymentSigningKey signer,
    IPurchaseTenantLock tenantLock)
{
    /// <summary>Confirms a pending order with an audited invoice reference.</summary>
    /// <param name="command">The authenticated invoice verification request.</param>
    /// <param name="cancellationToken">Cancellation signal.</param>
    /// <returns>The confirmed order, or null when missing.</returns>
    [Transactional]
    public async Task<TenantPurchaseOrderDto?> Handle(ConfirmExternalInvoiceCommand command, CancellationToken cancellationToken)
    {
        var candidate = await orders.FindOneAsync(item => item.Id == command.OrderId, false, cancellationToken).ConfigureAwait(false);
        if (candidate is null)
        {
            return null;
        }

        if (!await tenantLock.AcquireAsync(candidate.TenantId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var order = await orders.FindOneAsync(item => item.Id == command.OrderId, true, cancellationToken).ConfigureAwait(false);
        if (order is null || order.TenantId != candidate.TenantId)
        {
            return null;
        }

        var invoiceReference = command.InvoiceReference?.Trim();
        var verifiedBy = command.VerifiedBy?.Trim();
        if (order.Status == "active" && order.PaymentMethod == "external_invoice")
        {
            if (string.Equals(order.ExternalInvoiceReference, invoiceReference, StringComparison.Ordinal) &&
                string.Equals(order.InvoiceVerifiedBy, verifiedBy, StringComparison.Ordinal))
            {
                return TenantPurchaseOrderMapping.ToDto(order);
            }

            throw new InvalidOperationException("An existing paid invoice cannot be re-attested with different evidence.");
        }

        if (order.Status != "pending")
        {
            throw new InvalidOperationException("Only a pending order can be confirmed with invoice evidence.");
        }

        var previous = await PurchaseRenewalPredecessor.FindAsync(orders, order, cancellationToken).ConfigureAwait(false);
        var confirmedAt = DateTimeOffset.UtcNow;
        order.ConfirmExternalInvoice(previous?.SubscriptionId ?? Guid.NewGuid(), invoiceReference!, verifiedBy!, confirmedAt,
            previous?.ValidUntilUtc);

        await bus.PublishAsync(TenantPurchaseEventSnapshot.From(order, signer)).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return TenantPurchaseOrderMapping.ToDto(order);
    }
}
