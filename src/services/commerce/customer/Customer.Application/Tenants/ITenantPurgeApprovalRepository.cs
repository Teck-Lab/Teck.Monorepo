using Customers.Domain.Entities;

namespace Customers.Application.Tenants;

/// <summary>Customer persistence operations needed to serialize purge approvals for a paid tenant period.</summary>
public interface ITenantPurgeApprovalRepository
{
    /// <summary>Locks the tenant registry row until the surrounding unit of work commits.</summary>
    /// <param name="tenantId">The tenant whose paid order is being approved.</param>
    /// <param name="cancellationToken">Cancellation signal.</param>
    /// <returns>A task completed when the tenant row is locked.</returns>
    Task LockTenantAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>Loads the tracked tenant registry row while the lock is held.</summary>
    /// <param name="tenantId">The tenant whose paid order is being approved.</param>
    /// <param name="cancellationToken">Cancellation signal.</param>
    /// <returns>The tracked tenant or null if absent.</returns>
    Task<Tenant?> FindTenantAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>Loads the approval audit records for the current paid order.</summary>
    /// <param name="tenantId">The approved tenant.</param>
    /// <param name="sourceOrderId">The current paid order.</param>
    /// <param name="cancellationToken">Cancellation signal.</param>
    /// <returns>Existing audit records for this paid period.</returns>
    Task<IReadOnlyList<TenantPurgeApproval>> FindApprovalsAsync(Guid tenantId, Guid sourceOrderId, CancellationToken cancellationToken);

    /// <summary>Adds an immutable operator approval to the current unit of work.</summary>
    /// <param name="approval">The operator's immutable approval.</param>
    void AddApproval(TenantPurgeApproval approval);
}
