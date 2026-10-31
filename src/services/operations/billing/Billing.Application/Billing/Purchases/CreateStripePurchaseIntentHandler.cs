using Billings.Domain.Entities;
using SharedKernel.Core.Database;

using Wolverine.Attributes;

namespace Billings.Application.Billing.Purchases;

/// <summary>Owner-visible availability of a pending Stripe payment path.</summary>
public enum StripeIntentReservationStatus
{
    /// <summary>No order is visible to the authenticated buyer.</summary>
    NotFound,
    /// <summary>The owner's order cannot safely start or retry this payment path.</summary>
    Conflict,
    /// <summary>The provider attempt reservation is committed before contacting Stripe.</summary>
    Available,
}

/// <summary>Creates or reuses a provider intent for an authenticated order owner.</summary>
/// <param name="orders">The global Billing purchase repository.</param>
/// <param name="stripe">The provider-backed intent gateway.</param>
/// <param name="unitOfWork">The persistence boundary for the bound intent ID.</param>
/// <param name="tenantLock">The tenant-wide purchase and purge serialization lock.</param>
public sealed class CreateStripePurchaseIntentHandler(
    IGenericWriteRepository<TenantPurchaseOrder, Guid> orders,
    IStripePurchaseGateway stripe,
    IUnitOfWork unitOfWork,
    IPurchaseTenantLock tenantLock)
{
    /// <summary>Durably reserves a pending owner's Stripe payment path before any provider request.</summary>
    /// <param name="orderId">The purchase order ID.</param>
    /// <param name="buyerSubjectId">The authenticated buyer subject.</param>
    /// <param name="cancellationToken">Cancellation signal.</param>
    /// <returns>Not found for foreign orders, conflict for an unavailable owner's payment path, or available.</returns>
    public async Task<StripeIntentReservationStatus> ReserveAsync(Guid orderId, string buyerSubjectId, CancellationToken cancellationToken)
    {
        var candidate = await orders.FindOneAsync(item => item.Id == orderId, false, cancellationToken).ConfigureAwait(false);
        if (candidate is null || !string.Equals(candidate.BeneficiarySubjectId, buyerSubjectId, StringComparison.Ordinal))
        {
            return StripeIntentReservationStatus.NotFound;
        }

        if (candidate.Status != "pending")
        {
            return StripeIntentReservationStatus.Conflict;
        }

        if (!await tenantLock.AcquireAsync(candidate.TenantId, cancellationToken).ConfigureAwait(false))
        {
            return StripeIntentReservationStatus.Conflict;
        }

        var order = await orders.FindOneAsync(item => item.Id == orderId, true, cancellationToken).ConfigureAwait(false);
        if (order is null || order.TenantId != candidate.TenantId ||
            !string.Equals(order.BeneficiarySubjectId, buyerSubjectId, StringComparison.Ordinal))
        {
            return StripeIntentReservationStatus.NotFound;
        }

        if (order.Status != "pending")
        {
            return StripeIntentReservationStatus.Conflict;
        }

        if (!order.StripeIntentAttempted && order.StripePaymentIntentId is null)
        {
            order.ReserveStripeIntentAttempt(DateTimeOffset.UtcNow);
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return order.StripePaymentIntentId is not null || order.CanRetryUnboundStripeIntent(DateTimeOffset.UtcNow)
            ? StripeIntentReservationStatus.Available
            : StripeIntentReservationStatus.Conflict;
    }

    /// <summary>Creates or returns one PaymentIntent for the pending order.</summary>
    /// <param name="orderId">The purchase order ID.</param>
    /// <param name="buyerSubjectId">The authenticated buyer subject.</param>
    /// <param name="cancellationToken">Cancellation signal.</param>
    /// <returns>The Stripe PaymentIntent and its client secret.</returns>
    [Transactional]
    public async Task<StripePurchaseIntent?> HandleAsync(Guid orderId, string buyerSubjectId, CancellationToken cancellationToken)
    {
        var candidate = await orders.FindOneAsync(item => item.Id == orderId, false, cancellationToken).ConfigureAwait(false);
        if (candidate is null || candidate.Status != "pending" ||
            !string.Equals(candidate.BeneficiarySubjectId, buyerSubjectId, StringComparison.Ordinal))
        {
            return null;
        }

        if (!await tenantLock.AcquireAsync(candidate.TenantId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var order = await orders.FindOneAsync(item => item.Id == orderId, true, cancellationToken).ConfigureAwait(false);
        if (order is null || order.Status != "pending" ||
            !string.Equals(order.BeneficiarySubjectId, buyerSubjectId, StringComparison.Ordinal))
        {
            return null;
        }

        if (order.TenantId != candidate.TenantId)
        {
            throw new InvalidOperationException("Purchase tenant identity changed during serialization.");
        }

        if (!order.StripeIntentAttempted)
        {
            throw new InvalidOperationException("Stripe intent creation requires a committed purchase attempt reservation.");
        }

        if (order.StripePaymentIntentId is null && !order.CanRetryUnboundStripeIntent(DateTimeOffset.UtcNow))
        {
            return null;
        }

        var intent = order.StripePaymentIntentId is { } existing
            ? await stripe.GetAsync(existing, cancellationToken).ConfigureAwait(false)
            : await stripe.CreateAsync(order, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(intent.ClientSecret) ||
            order.StripePaymentIntentId is not null && order.StripePaymentIntentId != intent.PaymentIntentId)
        {
            throw new InvalidOperationException("Stripe did not return the order's bound PaymentIntent.");
        }

        order.BindStripePaymentIntent(intent.PaymentIntentId);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return intent;
    }
}
