using Grpc.Core;
using Licensing.Application;
using Licensing.Host.Grpc.V1;

namespace Licensing.Host;

/// <summary>Serves tenant-bound signed enterprise policies to trusted internal services.</summary>
/// <param name="store">Durable enterprise policy storage.</param>
public sealed class EnterprisePolicyLookupService(IEnterprisePolicyStore store) : EnterprisePolicyLookup.EnterprisePolicyLookupBase
{
    /// <inheritdoc />
    public override async Task<GetEnterprisePolicyResponse> GetEnterprisePolicy(GetEnterprisePolicyRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.TenantId, out var tenantId) || !Guid.TryParse(request.SubscriptionId, out var subscriptionId) ||
            !Guid.TryParse(request.SourceOrderId, out var sourceOrderId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Tenant, subscription, and source-order IDs must be GUIDs."));
        }

        EnterprisePolicyState? state;
        try
        {
            state = await store.GetStateAsync(request.PolicyId, request.PolicyVersion, tenantId, subscriptionId, sourceOrderId,
                DateTimeOffset.UtcNow, context.CancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            throw new RpcException(new Status(StatusCode.Unavailable, "Enterprise policy store is unavailable."));
        }

        if (state is null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "Exact enterprise policy identity was not found."));
        }

        if (state.Status != EnterprisePolicyStatus.Active)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "Enterprise policy is not active."));
        }

        var policy = state.Policy;
        return new GetEnterprisePolicyResponse
        {
            Policy = new Licensing.Host.Grpc.V1.EnterprisePolicyDocument
            {
                PolicyId = policy.PolicyId,
                PolicyVersion = policy.PolicyVersion,
                TenantId = policy.TenantId.ToString(),
                SubscriptionId = policy.SubscriptionId.ToString(),
                SourceOrderId = policy.SourceOrderId.ToString(),
                GracePeriodDays = policy.GracePeriodDays,
                BackupRetentionDays = policy.BackupRetentionDays,
                ValidFromUtc = policy.ValidFromUtc.ToString("O"),
                ValidUntilUtc = policy.ValidUntilUtc.ToString("O"),
                LicenseKind = policy.LicenseKind,
                SigningKeyId = policy.SigningKeyId,
                Signature = policy.Signature,
            },
            Status = PolicyStatus.Active,
        };
    }
}
