using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Core.Database;

namespace Licensing.Application.Database;

/// <summary>EF-backed immutable enterprise policy and audit store.</summary>
/// <param name="db">The Licensing write database context.</param>
/// <param name="unitOfWork">The transaction and commit boundary for policy/audit mutations.</param>
public sealed class EnterprisePolicyDbStore(LicensingDbContext db, IUnitOfWork unitOfWork) : IEnterprisePolicyStore
{
    /// <inheritdoc />
    public async Task AddAsync(EnterprisePolicyDocument policy, EnterprisePolicyAudit audit, CancellationToken cancellationToken)
    {
        var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        try
        {
            db.EnterprisePolicies.Add(new EnterprisePolicyRecord
            {
                PolicyId = policy.PolicyId,
                PolicyVersion = policy.PolicyVersion,
                Document = JsonSerializer.Serialize(policy),
                IssuedAtUtc = audit.AtUtc,
                TenantId = policy.TenantId,
                SubscriptionId = policy.SubscriptionId,
                SourceOrderId = policy.SourceOrderId,
            });
            db.EnterprisePolicyAudit.Add(new EnterprisePolicyAuditRecord
            {
                PolicyId = policy.PolicyId,
                PolicyVersion = policy.PolicyVersion,
                ActorSubjectId = audit.ActorSubjectId,
                Action = audit.Action,
                AtUtc = audit.AtUtc,
            });
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await unitOfWork.CommitTransactionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
        finally
        {
            transaction.Dispose();
        }
    }

    /// <inheritdoc />
    public async Task<EnterprisePolicyDocument?> GetAsync(string policyId, string policyVersion, CancellationToken cancellationToken)
    {
        var document = await db.EnterprisePolicies.AsNoTracking().Where(x => x.PolicyId == policyId && x.PolicyVersion == policyVersion)
            .Select(x => x.Document).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return document is null ? null : JsonSerializer.Deserialize<EnterprisePolicyDocument>(document);
    }

    /// <inheritdoc />
    public async Task<bool> RevokeAsync(string policyId, string policyVersion, string actorSubjectId, DateTimeOffset atUtc,
        CancellationToken cancellationToken)
    {
        var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        try
        {
            var record = await db.EnterprisePolicies.SingleOrDefaultAsync(x => x.PolicyId == policyId && x.PolicyVersion == policyVersion,
                cancellationToken).ConfigureAwait(false);
            if (record is null || record.RevokedAtUtc is not null)
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }

            record.RevokedAtUtc = atUtc;
            record.RevokedBySubjectId = actorSubjectId;
            db.EnterprisePolicyAudit.Add(new EnterprisePolicyAuditRecord
            {
                PolicyId = policyId,
                PolicyVersion = policyVersion,
                ActorSubjectId = actorSubjectId,
                Action = "revoked",
                AtUtc = atUtc,
            });
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await unitOfWork.CommitTransactionAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
        finally
        {
            transaction.Dispose();
        }
    }

    /// <inheritdoc />
    public async Task<EnterprisePolicyState?> GetStateAsync(string policyId, string policyVersion, Guid tenantId, Guid subscriptionId,
        Guid sourceOrderId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var record = await db.EnterprisePolicies.AsNoTracking().SingleOrDefaultAsync(x => x.PolicyId == policyId && x.PolicyVersion == policyVersion,
            cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return null;
        }

        var policy = JsonSerializer.Deserialize<EnterprisePolicyDocument>(record.Document);
        if (policy is null || policy.TenantId != tenantId || policy.SubscriptionId != subscriptionId || policy.SourceOrderId != sourceOrderId)
        {
            return null;
        }

        var latestIssueUtc = await db.EnterprisePolicies.AsNoTracking()
            .Where(x => x.PolicyId == policyId && x.TenantId == tenantId && x.SubscriptionId == subscriptionId && x.SourceOrderId == sourceOrderId)
            .MaxAsync(x => (DateTimeOffset?)x.IssuedAtUtc, cancellationToken).ConfigureAwait(false);
        var superseded = latestIssueUtc > record.IssuedAtUtc;
        var status = record.RevokedAtUtc is not null ? EnterprisePolicyStatus.Revoked : superseded ? EnterprisePolicyStatus.Superseded :
            nowUtc < policy.ValidFromUtc ? EnterprisePolicyStatus.NotYetValid : nowUtc >= policy.ValidUntilUtc ? EnterprisePolicyStatus.Expired : EnterprisePolicyStatus.Active;
        return new EnterprisePolicyState(policy, status);
    }
}
