using Pricing.Application.Pricing.Responses;
using SharedKernel.Core.CQRS;

namespace Pricing.Application.Pricing.Features.ListPriceLists.V1;

/// <summary>Query that lists all price lists for the tenant.</summary>
public sealed record ListPriceListsQuery : IQuery<IReadOnlyList<PriceListDto>>;
