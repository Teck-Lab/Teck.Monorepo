using ErrorOr;
using Pricing.Application.Pricing.Mapping;
using Pricing.Application.Pricing.ReadModels;
using Pricing.Application.Pricing.Responses;
using Pricing.Domain.Entities;
using SharedKernel.Core.Database;

namespace Pricing.Application.Pricing.Features.GetPriceList.V1;

/// <summary>Handles <see cref="GetPriceListQuery"/>.</summary>
public static class GetPriceListHandler
{
    /// <summary>Loads a price list or returns not-found.</summary>
    /// <param name="query">The query.</param>
    /// <param name="repository">The read repository.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The list DTO or a not-found error.</returns>
    public static async Task<ErrorOr<PriceListDto>> Handle(
        GetPriceListQuery query,
        IGenericReadRepository<PriceList, Guid> repository,
        CancellationToken ct)
    {
        PriceList? list = await repository.FirstOrDefaultAsync(new PriceListByIdSpec(query.Id), ct).ConfigureAwait(false);
        return list is null
            ? Error.NotFound(description: $"Price list '{query.Id}' was not found.")
            : list.ToDto();
    }
}
