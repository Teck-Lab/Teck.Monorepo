using Billings.Domain.Entities;
using SharedKernel.Core.Database;

namespace Billings.Application.Billing.Purchases;

/// <summary>Reads the authenticated tenant's purchase lifecycle state.</summary>
/// <param name="orders">The global Billing purchase repository.</param>
public sealed class GetTenantPurchaseOrderHandler(IGenericReadRepository<TenantPurchaseOrder, Guid> orders)
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

        var order = (await orders.FindAsync(item => item.TenantId == query.ReservationId && item.BeneficiarySubjectId == query.OwnerSubjectId,
            false, cancellationToken).ConfigureAwait(false))
            .OrderByDescending(item => item.CreatedAtUtc).ThenByDescending(item => item.Id).FirstOrDefault();
        return order is null ? null : TenantPurchaseOrderMapping.ToDto(order);
    }
}
