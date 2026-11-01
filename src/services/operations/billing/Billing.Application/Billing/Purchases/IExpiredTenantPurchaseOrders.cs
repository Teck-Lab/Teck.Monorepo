using Billings.Domain.Entities;

namespace Billings.Application.Billing.Purchases;

/// <summary>Finds a bounded batch of tracked paid periods whose expiry was not superseded.</summary>
public interface IExpiredTenantPurchaseOrders
{
    /// <summary>Finds active periods past their deadline without a paid successor.</summary>
    /// <param name="nowUtc">Reconciliation cutoff.</param>
    /// <param name="cancellationToken">Cancellation signal.</param>
    /// <returns>At most 64 tracked, unsuperseded paid orders.</returns>
    Task<IReadOnlyList<TenantPurchaseOrder>> FindExpiredAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken);
}
