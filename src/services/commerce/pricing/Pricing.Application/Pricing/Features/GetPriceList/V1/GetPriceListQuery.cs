using Pricing.Application.Pricing.Responses;
using SharedKernel.Core.CQRS;

namespace Pricing.Application.Pricing.Features.GetPriceList.V1;

/// <summary>Query that retrieves a single price list by identifier.</summary>
/// <param name="Id">The price list identifier.</param>
public sealed record GetPriceListQuery(Guid Id) : IQuery<PriceListDto>;
