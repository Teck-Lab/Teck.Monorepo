using Customers.Application.Database;
using Customers.Application.Tenants;
using Customers.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Customers.Host.Database;

/// <summary>PostgreSQL persistence for the Customer purge approval workflow.</summary>
/// <param name="context">The Customer write context enlisted in the handler transaction.</param>
public sealed class TenantPurgeApprovalRepository(CustomerDbContext context) : ITenantPurgeApprovalRepository
{
    /// <inheritdoc/>
    public async Task LockTenantAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM tenants WHERE \"Id\" = {tenantId} FOR UPDATE", cancellationToken).ConfigureAwait(false);

    /// <inheritdoc/>
    public Task<Tenant?> FindTenantAsync(Guid tenantId, CancellationToken cancellationToken) =>
        context.Tenants.SingleOrDefaultAsync(row => row.Id == tenantId, cancellationToken);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TenantPurgeApproval>> FindApprovalsAsync(Guid tenantId, Guid sourceOrderId,
        CancellationToken cancellationToken) =>
        await context.TenantPurgeApprovals
            .Where(row => row.TenantId == tenantId && row.SourceOrderId == sourceOrderId)
            .OrderBy(row => row.ApprovedAtUtc)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc/>
    public void AddApproval(TenantPurgeApproval approval) => context.TenantPurgeApprovals.Add(approval);
}
