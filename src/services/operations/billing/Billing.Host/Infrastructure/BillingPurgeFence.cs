using System.Data;
using Billings.Application.Billing.Purchases;
using Billings.Application.Database;
using Billings.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Billings.Host.Infrastructure;

/// <summary>Serializes Billing authorization of external tenant purge operations.</summary>
/// <param name="db">Billing's durable write context.</param>
/// <param name="tenantLock">The per-tenant purchase and purge transaction lock.</param>
public sealed class BillingPurgeFence(BillingDbContext db, IPurchaseTenantLock tenantLock)
{
    /// <summary>Acquires a durable purge grant only for an expired paid order with no payable or paid successor.</summary>
    /// <param name="tenantId">The globally unique tenant identifier.</param>
    /// <param name="subscriptionId">The subscription being purged.</param>
    /// <param name="expectedSourceOrderId">The approved expired source purchase order.</param>
    /// <param name="approvalId">The unique approval correlation identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The active grant identifier, or <see langword="null"/> when authorization is denied.</returns>
    public async Task<Guid?> TryAcquireAsync(Guid tenantId, Guid subscriptionId, Guid expectedSourceOrderId,
        Guid approvalId, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || subscriptionId == Guid.Empty || expectedSourceOrderId == Guid.Empty || approvalId == Guid.Empty)
        {
            return null;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        await tenantLock.AcquireForCompletionAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var existing = await db.Set<TenantPurgeGrant>().SingleOrDefaultAsync(grant =>
            grant.TenantId == tenantId && grant.CompletedAtUtc == null, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing.SubscriptionId == subscriptionId && existing.SourceOrderId == expectedSourceOrderId &&
                existing.ApprovalId == approvalId ? existing.Id : null;
        }

        if (await db.Set<TenantPurgeGrant>().AnyAsync(grant => grant.TenantId == tenantId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var source = await db.TenantPurchaseOrders.SingleOrDefaultAsync(order => order.Id == expectedSourceOrderId &&
            order.TenantId == tenantId && order.SubscriptionId == subscriptionId, cancellationToken).ConfigureAwait(false);
        if (source is null || (source.Status != "cancelled" && source.Status != "suspended") || source.ValidUntilUtc is null ||
            source.ValidUntilUtc > DateTimeOffset.UtcNow || await db.TenantPurchaseOrders.AnyAsync(order =>
                order.TenantId == tenantId && (order.Status == "pending" ||
                    order.Id != source.Id && order.SubscriptionId != Guid.Empty &&
                    (order.Status == "active" || order.Status == "suspended")), cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var grant = TenantPurgeGrant.Create(Guid.NewGuid(), tenantId, subscriptionId, expectedSourceOrderId, approvalId, DateTimeOffset.UtcNow);
        db.Add(grant);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return grant.Id;
    }

    /// <summary>Records successful external purge and reconciliation. Repeated completion is idempotent.</summary>
    /// <param name="grantId">The stable identifier of the active purge grant.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when the grant was completed; otherwise, <see langword="false"/>.</returns>
    public async Task<bool> CompleteAsync(Guid grantId, CancellationToken cancellationToken)
    {
        if (grantId == Guid.Empty)
        {
            return false;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        var grant = await db.Set<TenantPurgeGrant>().SingleOrDefaultAsync(item => item.Id == grantId, cancellationToken).ConfigureAwait(false);
        if (grant is null)
        {
            return false;
        }

        await tenantLock.AcquireForCompletionAsync(grant.TenantId, cancellationToken).ConfigureAwait(false);

        grant.Complete(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}
