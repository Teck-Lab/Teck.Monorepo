using Pricing.Application.Pricing.Responses;
using SharedKernel.Core.CQRS;

namespace Pricing.Application.Pricing.Features.ResolvePrice.V1;

/// <summary>Query that resolves the effective price for a product in a request context.</summary>
/// <param name="ProductId">The product identifier.</param>
/// <param name="Currency">The requested ISO currency.</param>
/// <param name="Quantity">The requested quantity (>= 1).</param>
/// <param name="Country">The optional request country.</param>
/// <param name="CustomerGroupId">The optional request customer group.</param>
/// <param name="ChannelId">The optional request channel.</param>
/// <param name="At">The optional resolution moment (defaults to now).</param>
public sealed record ResolvePriceQuery(
    Guid ProductId,
    string Currency,
    int Quantity,
    string? Country,
    Guid? CustomerGroupId,
    Guid? ChannelId,
    DateTimeOffset? At) : IQuery<ResolvedPriceDto>;
