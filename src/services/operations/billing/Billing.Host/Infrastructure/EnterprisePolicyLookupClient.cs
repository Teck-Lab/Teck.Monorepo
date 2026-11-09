using Grpc.Net.Client;
using Licensing.Host.Grpc.V1;
using Microsoft.Extensions.Options;

namespace Billings.Host.Infrastructure;

/// <summary>Calls Licensing's internal gRPC policy lookup over the configured service network.</summary>
/// <param name="options">Licensing service discovery options.</param>
public sealed class EnterprisePolicyLookupClient(IOptions<EnterprisePolicyOptions> options)
{
    private readonly EnterprisePolicyLookup.EnterprisePolicyLookupClient client = new(GrpcChannel.ForAddress(options.Value.Address));

    /// <summary>Retrieves one exact active policy bound to an existing paid subscription order.</summary>
    /// <param name="policyId">The requested immutable policy ID.</param>
    /// <param name="policyVersion">The exact requested policy version.</param>
    /// <param name="tenantId">The tenant identity to bind.</param>
    /// <param name="subscriptionId">The subscription identity to bind.</param>
    /// <param name="sourceOrderId">The paid source order identity to bind.</param>
    /// <param name="cancellationToken">The cancellation signal.</param>
    /// <returns>The exact active policy lookup response.</returns>
    public Task<GetEnterprisePolicyResponse> GetAsync(string policyId, string policyVersion, Guid tenantId, Guid subscriptionId,
        Guid sourceOrderId, CancellationToken cancellationToken) => client.GetEnterprisePolicyAsync(new GetEnterprisePolicyRequest
        {
            PolicyId = policyId,
            PolicyVersion = policyVersion,
            TenantId = tenantId.ToString(),
            SubscriptionId = subscriptionId.ToString(),
            SourceOrderId = sourceOrderId.ToString(),
        }, cancellationToken: cancellationToken).ResponseAsync;
}
