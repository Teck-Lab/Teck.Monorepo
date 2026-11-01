using Billings.Application.Billing.Purchases;
using Billings.Application.Database;
using Microsoft.EntityFrameworkCore;

namespace Billings.Host.Database;

/// <summary>Uses a PostgreSQL transaction advisory lock to serialize Billing writes for a tenant.</summary>
/// <param name="context">The Billing write context enlisted in the operation.</param>
public sealed class BillingPurchaseTenantLock(BillingDbContext context) : IPurchaseTenantLock
{
    /// <inheritdoc />
    public async Task<bool> AcquireAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await AcquireForCompletionAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return !await context.TenantPurgeGrants.AnyAsync(grant => grant.TenantId == tenantId,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AcquireForCompletionAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A tenant ID is required.", nameof(tenantId));
        }

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({tenantId.ToString()}, 0))", cancellationToken).ConfigureAwait(false);
    }
}
