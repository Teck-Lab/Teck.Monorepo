using Billings.Application.Billing.Purchases;
using Billings.Application.Database;
using Billings.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Billings.Host.Database;

/// <summary>Executes the bounded expired-subscription query against the tracked write context.</summary>
/// <param name="context">The Billing write context.</param>
public sealed class BillingExpiredTenantPurchaseOrders(BillingDbContext context) : IExpiredTenantPurchaseOrders
{
    /// <inheritdoc/>
    public async Task<IReadOnlyList<TenantPurchaseOrder>> FindExpiredAsync(DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        await context.TenantPurchaseOrders
            .Where(order => order.Status == "active" && order.ValidUntilUtc <= nowUtc &&
                !context.TenantPurchaseOrders.Any(next => next.PreviousPurchaseOrderId == order.Id &&
                    next.Status == "active" && next.SubscriptionId == order.SubscriptionId &&
                    next.ValidUntilUtc > order.ValidUntilUtc))
            .OrderBy(order => order.ValidUntilUtc)
            .Take(64)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
}
