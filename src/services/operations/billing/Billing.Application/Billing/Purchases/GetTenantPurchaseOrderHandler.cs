using Billings.Application.Database;
using Microsoft.EntityFrameworkCore;

namespace Billings.Application.Billing.Purchases;

/// <summary>Reads the authenticated tenant's purchase lifecycle state.</summary>
/// <param name="context">The Billing write context.</param>
public sealed class GetTenantPurchaseOrderHandler(BillingDbContext context)
{
    /// <summary>Gets an order belonging to the verified reservation owner.</summary>
    /// <param name="query">The reservation and signed owner identity.</param>
    /// <param name="cancellationToken">Cancellation signal.</param>
    /// <returns>The owner's latest pending or confirmed annual order, if any.</returns>
    public async Task<TenantPurchaseOrderDto?> Handle(GetTenantPurchaseOrderQuery query, CancellationToken cancellationToken)
    {
        if (query.ReservationId == Guid.Empty || string.IsNullOrWhiteSpace(query.OwnerSubjectId))
        {
            return null;
        }

        var order = await context.TenantPurchaseOrders
            .Where(item => item.TenantId == query.ReservationId && item.BeneficiarySubjectId == query.OwnerSubjectId)
            .OrderByDescending(item => item.CreatedAtUtc).ThenByDescending(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return order is null ? null : TenantPurchaseOrderMapping.ToDto(order);
    }
}
