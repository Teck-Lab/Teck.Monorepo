using Billings.Domain.Entities;
using Microsoft.Extensions.Options;
using SharedKernel.Core.Database;
using SharedKernel.Core.Pricing;

using Wolverine.Attributes;

namespace Billings.Application.Billing.Purchases;

/// <summary>Creates idempotent purchase orders with server-owned pricing.</summary>
/// <param name="orders">The global Billing purchase repository.</param>
/// <param name="unitOfWork">The Billing commit boundary.</param>
/// <param name="options">The server-owned purchase pricing configuration.</param>
/// <param name="tenantLock">The tenant-wide purchase and purge serialization lock.</param>
public sealed class CreateTenantPurchaseHandler(
    IGenericWriteRepository<TenantPurchaseOrder, Guid> orders,
    IUnitOfWork unitOfWork,
    IOptions<TenantPurchaseOptions> options,
    IPurchaseTenantLock tenantLock)
{
    /// <summary>Creates or returns the tenant's existing purchase order.</summary>
    /// <param name="command">The caller's purchase intent.</param>
    /// <param name="cancellationToken">Cancellation signal.</param>
    /// <returns>The durable quote for the tenant.</returns>
    [Transactional]
    public async Task<TenantPurchaseOrderDto> Handle(CreateTenantPurchaseCommand command, CancellationToken cancellationToken)
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

        if (!TenantPlan.TryFromName(command.PlanId, ignoreCase: true, out var plan))
        {
            throw new ArgumentException("Requested plan is not available.", nameof(command));
        }

        if (!await tenantLock.AcquireAsync(command.TenantId, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Tenant purge is active; purchase creation is denied.");
        }

        var existing = (await orders.FindAsync(order => order.TenantIdentifier == command.TenantIdentifier, false, cancellationToken)
            .ConfigureAwait(false)).OrderByDescending(order => order.CreatedAtUtc).ThenByDescending(order => order.Id).ToList();
        if (existing.Any(order => order.TenantId != command.TenantId ||
            !string.Equals(order.BeneficiarySubjectId, command.BeneficiarySubjectId, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Tenant identifier is already associated with another tenant.");
        }

        if (!command.IsRenewal && existing.Count > 0)
        {
            return TenantPurchaseOrderMapping.ToDto(existing[^1]);
        }

        if (command.IsRenewal)
        {
            var predecessor = existing.FirstOrDefault(order => order.Id == command.PreviousPurchaseOrderId);
            if (predecessor is null || predecessor.Status is not ("active" or "suspended") ||
                predecessor.SubscriptionId == Guid.Empty || predecessor.ValidUntilUtc is null ||
                existing.Any(order => (order.Status is "active" or "suspended") && order.CreatedAtUtc > predecessor.CreatedAtUtc))
            {
                throw new InvalidOperationException("Customer's current verified paid period is required to renew.");
            }

            var pending = existing.FirstOrDefault(order => order.Status == "pending");
            if (pending is not null)
            {
                if (pending.PreviousPurchaseOrderId == predecessor.Id && string.Equals(pending.PlanVersion, command.PlanVersion, StringComparison.Ordinal))
                {
                    return TenantPurchaseOrderMapping.ToDto(pending);
                }

                throw new InvalidOperationException("An existing renewal must be resolved before another renewal can be quoted.");
            }
        }

        if (plan.DatabaseStrategy == DatabaseStrategy.None)
        {
            throw new ArgumentException("Requested database configuration is not available.", nameof(command));
        }

        var databaseStrategy = plan.DatabaseStrategy;

        var order = TenantPurchaseOrder.Create(command.TenantId, command.TenantIdentifier, plan, command.PlanVersion,
            databaseStrategy, command.DatabaseProvider, command.HasReadReplicas, command.ReplicaCount,
            new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["read-replica"] = configuration.ReadReplicaMonthlyRate,
            }, DateTimeOffset.UtcNow, command.ActorSubjectId, command.BeneficiarySubjectId);
        if (command.IsRenewal)
        {
            order.BindPreviousPurchaseOrder(command.PreviousPurchaseOrderId);
        }

        await orders.AddAsync(order, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return TenantPurchaseOrderMapping.ToDto(order);
    }
}
