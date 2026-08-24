using Ardalis.Specification;
using Pricing.Domain.Entities;

namespace Pricing.Application.Pricing.ReadModels;

/// <summary>Selects a pending reconciliation by its stable request key.</summary>
public sealed class PendingPriceResolutionByRequestSpec : Specification<PendingPriceResolution>
{
    /// <summary>Initializes the request lookup.</summary>
    /// <param name="requestId">The stable pricing request key.</param>
    /// <param name="tenantId">The owning tenant identifier.</param>
    public PendingPriceResolutionByRequestSpec(string requestId, string tenantId) =>
        Query.Where(resolution => resolution.RequestId == requestId && resolution.TenantId == tenantId);
}
