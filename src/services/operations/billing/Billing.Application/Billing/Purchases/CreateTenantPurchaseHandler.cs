using Billings.Application.Database;
using Billings.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel.Core.Pricing;

namespace Billings.Application.Billing.Purchases;

/// <summary>Creates idempotent purchase orders with server-owned pricing.</summary>
/// <param name="context">The Billing write context.</param>
/// <param name="options">The server-owned purchase pricing configuration.</param>
public sealed class CreateTenantPurchaseHandler(BillingDbContext context, IOptions<TenantPurchaseOptions> options)
{
    /// <summary>Creates or returns the tenant's existing purchase order.</summary>
    /// <param name="command">The caller's purchase intent.</param>
    /// <param name="cancellationToken">Cancellation signal.</param>
    /// <returns>The durable quote for the tenant.</returns>
    public async Task<TenantPurchaseOrder> Handle(CreateTenantPurchaseCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var configuration = options.Value;
        if (!string.Equals(configuration.PlanVersion, command.PlanVersion, StringComparison.Ordinal))
        {
            throw new ArgumentException("Requested plan version is not current.", nameof(command));
        }

        if (!configuration.AllowedDatabaseProviders.Contains(command.DatabaseProvider, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Database provider is not offered.", nameof(command));
        }

        var existing = await context.TenantPurchaseOrders
            .Where(order => order.TenantIdentifier == command.TenantIdentifier)
            .OrderByDescending(order => order.CreatedAtUtc).ThenByDescending(order => order.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (existing.Any(order => order.TenantId != command.TenantId ||
            !string.Equals(order.BeneficiarySubjectId, command.BeneficiarySubjectId, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Tenant identifier is already associated with another tenant.");
        }

        if (!command.IsRenewal)
        {
            if (existing.Count > 0) return existing[^1];
        }
        else
        {
            var predecessor = existing.FirstOrDefault(order => order.Id == command.PreviousPurchaseOrderId);
            if (predecessor is null || predecessor.Status is not ("active" or "suspended") ||
                predecessor.SubscriptionId == Guid.Empty || predecessor.ValidUntilUtc is null ||
                existing.Any(order => order.Status is "active" or "suspended" &&
                    order.CreatedAtUtc > predecessor.CreatedAtUtc))
            {
                throw new InvalidOperationException("Customer's current verified paid period is required to renew.");
            }

            var pending = existing.FirstOrDefault(order => order.Status == "pending");
            if (pending is not null)
            {
                if (pending.PreviousPurchaseOrderId != predecessor.Id)
                    throw new InvalidOperationException("Pending renewal targets a different paid period.");
                return pending;
            }
        }

        if (!TenantPlan.TryFromName(command.PlanId, ignoreCase: true, out var plan))
        {
            throw new ArgumentException("Unknown plan.", nameof(command));
        }

        var order = TenantPurchaseOrder.Create(
            command.TenantId,
            command.TenantIdentifier,
            plan,
            configuration.PlanVersion,
            plan.DatabaseStrategy,
            command.DatabaseProvider.ToLowerInvariant(),
            command.HasReadReplicas,
            command.ReplicaCount,
            new Dictionary<string, decimal> { ["read-replica"] = configuration.ReadReplicaMonthlyRate },
            DateTimeOffset.UtcNow, command.ActorSubjectId, command.BeneficiarySubjectId);
        if (command.IsRenewal) order.BindPreviousPurchaseOrder(command.PreviousPurchaseOrderId);
        context.TenantPurchaseOrders.Add(order);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return order;
    }
}
