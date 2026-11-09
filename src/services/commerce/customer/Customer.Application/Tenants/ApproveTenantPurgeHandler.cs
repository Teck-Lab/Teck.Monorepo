using Customers.Domain.Entities;
using Microsoft.Extensions.Options;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;
using Wolverine.Attributes;

namespace Customers.Application.Tenants;

/// <summary>Writes append-only independent purge approvals and publishes only after two distinct decisions.</summary>
/// <param name="approvals">Customer persistence for the paid period and approval audit trail.</param>
/// <param name="unitOfWork">The shared commit boundary enlisted in the handler transaction.</param>
/// <param name="bus">The transactional Wolverine outbox.</param>
/// <param name="signingKey">Customer-only purge approval signing key.</param>
/// <param name="options">The recovery window established by operations.</param>
public sealed class ApproveTenantPurgeHandler(ITenantPurgeApprovalRepository approvals, IUnitOfWork unitOfWork,
    IMessageBus bus, TenantPurgeApprovalSigningKey signingKey, IOptions<TenantPurgeApprovalOptions> options)
{
    /// <summary>Serializes approvals against the current paid order and records the second decision atomically with the signed event.</summary>
    /// <param name="command">Keycloak-authorized operator and route-bound tenant identity.</param>
    /// <param name="ct">Cancellation signal.</param>
    /// <returns>The absent, first-approval, or signed final-approval result.</returns>
    [Transactional]
    public async Task<TenantPurgeApprovalResult> Handle(ApproveTenantPurgeCommand command, CancellationToken ct)
    {
        if (command.TenantId == Guid.Empty || string.IsNullOrWhiteSpace(command.OperatorSubjectId))
        {
            throw new ArgumentException("A paid tenant and authenticated operator are required.");
        }

        // The PostgreSQL tenant row remains locked in the transaction through audit save and outbox commit.
        // A concurrent renewal or suspension updating the same row cannot interleave with this decision.
        await approvals.LockTenantAsync(command.TenantId, ct).ConfigureAwait(false);
        Tenant? tenant = await approvals.FindTenantAsync(command.TenantId, ct).ConfigureAwait(false);
        if (tenant is null)
        {
            return new(false, null);
        }

        TimeSpan recovery = options.Value.RequireRecoveryWindow();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (!tenant.IsPurgeEligibleAt(now, recovery))
        {
            throw new TenantPurgeApprovalConflictException("Only a retained paid dedicated tenant can be approved for purge.");
        }

        DateTimeOffset suspendedAt = tenant.SuspendedAtUtc!.Value;
        IReadOnlyList<TenantPurgeApproval> prior = await approvals.FindApprovalsAsync(tenant.Id, tenant.PurchaseOrderId, ct).ConfigureAwait(false);
        if (prior.Count > 1 || prior.Any(row => row.SubscriptionId != tenant.SubscriptionId ||
            string.Equals(row.ApproverSubjectId, command.OperatorSubjectId, StringComparison.Ordinal)))
        {
            throw new TenantPurgeApprovalConflictException("A different operator must make the second and final purge approval.");
        }

        TenantPurgeApproval approval = TenantPurgeApproval.Create(Guid.NewGuid(), tenant.Id,
            tenant.PurchaseOrderId, tenant.SubscriptionId, command.OperatorSubjectId, now);
        approvals.AddApproval(approval);
        if (prior.Count == 0)
        {
            await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
            return new(true, null);
        }

        TenantPurgeApproval first = prior[0];
        var decision = new TenantPurgeApprovedIntegrationEvent
        {
            ApprovalId = approval.Id,
            TenantId = tenant.Id,
            TenantIdentifier = tenant.Identifier,
            SubscriptionId = tenant.SubscriptionId,
            SourceOrderId = tenant.PurchaseOrderId,
            FirstApproverSubjectId = first.ApproverSubjectId,
            SecondApproverSubjectId = approval.ApproverSubjectId,
            SuspendedAtUtc = suspendedAt,
            EligibleAtUtc = suspendedAt + recovery,
            ApprovedAtUtc = now,
        };
        decision.Proof = signingKey.Sign(decision);
        await bus.PublishAsync(decision).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return new(true, decision);
    }
}
