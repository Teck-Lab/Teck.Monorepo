using Pricing.Application.Pricing.Mapping;
using Pricing.Application.Pricing.ReadModels;
using Pricing.Application.Pricing.Responses;
using Pricing.Domain.Entities;
using SharedKernel.Core.Database;

namespace Pricing.Application.Pricing.Features.ListPriceLists.V1;

/// <summary>Handles <see cref="ListPriceListsQuery"/>.</summary>
public static class ListPriceListsHandler
{
    /// <summary>Lists all price lists mapped to DTOs.</summary>
    /// <param name="query">The query.</param>
    /// <param name="repository">The read repository.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The price lists.</returns>
    public static async Task<IReadOnlyList<PriceListDto>> Handle(
        ListPriceListsQuery query,
        IGenericReadRepository<PriceList, Guid> repository,
        CancellationToken ct)
    {
        IReadOnlyList<PriceList> lists = await repository.ListAsync(new AllPriceListsSpec(), ct).ConfigureAwait(false);
        return lists.Select(list => list.ToDto()).ToList();
    }
}
