using Billings.Application.Database;
using Billings.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Billings.Application.Billing.Purchases;

/// <summary>Resolves a renewal's server-persisted paid predecessor without trusting provider metadata or a caller's subscription ID.</summary>
internal static class PurchaseRenewalPredecessor
{
    internal static async Task<TenantPurchaseOrder?> FindAsync(
        BillingDbContext context, TenantPurchaseOrder renewal, CancellationToken cancellationToken)
    {
        if (renewal.PreviousPurchaseOrderId == Guid.Empty) return null;
        var preceding = await context.TenantPurchaseOrders.SingleOrDefaultAsync(
            item => item.Id == renewal.PreviousPurchaseOrderId, cancellationToken).ConfigureAwait(false);
        if (preceding is null || preceding.TenantId != renewal.TenantId ||
            !string.Equals(preceding.TenantIdentifier, renewal.TenantIdentifier, StringComparison.Ordinal) ||
            !string.Equals(preceding.BeneficiarySubjectId, renewal.BeneficiarySubjectId, StringComparison.Ordinal) ||
            preceding.Status is not ("active" or "suspended") ||
            preceding.SubscriptionId == Guid.Empty || preceding.ValidUntilUtc is null ||
            await context.TenantPurchaseOrders.AnyAsync(next =>
                next.PreviousPurchaseOrderId == preceding.Id && next.Id != renewal.Id &&
                (next.Status == "active" || next.Status == "suspended"), cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("The renewal does not follow the latest verified paid order.");
        }

        return preceding;
    }
}
