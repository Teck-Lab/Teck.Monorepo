using Inventories.Application.Inventory.ReadModels;
using Inventories.Application.Inventory.Responses;
using Inventories.Domain.Entities;
using SharedKernel.Core.Database;

namespace Inventories.Application.Inventory.Features.GetAvailability.V1;

/// <summary>Handles the <see cref="GetAvailabilityQuery"/> by summing availability across a product's stock records.</summary>
public static class GetAvailabilityHandler
{
    /// <summary>
    /// Retrieves the total and per-location availability for a product, optionally filtered to a
    /// single location.
    /// </summary>
    /// <param name="query">The query identifying the product (and optional location) to check.</param>
    /// <param name="repository">The repository used to query stock items.</param>
    /// <param name="ct">A token used to cancel the operation.</param>
    /// <returns>The aggregate and per-location availability for the product.</returns>
    public static async Task<AvailabilityDto> Handle(
        GetAvailabilityQuery query,
        IGenericReadRepository<StockItem, Guid> repository,
        CancellationToken ct)
    {
        var spec = new AvailabilityByProductSpec(query.ProductId, query.LocationId);
        var items = await repository.ListAsync(spec, ct).ConfigureAwait(false);

        var byLocation = items
            .Select(item => new LocationAvailabilityDto(item.LocationId, item.Available))
            .ToList();

        return new AvailabilityDto(query.ProductId, byLocation.Sum(location => location.Available), byLocation);
    }
}
