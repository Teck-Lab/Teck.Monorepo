using Inventories.Application.Inventory.Responses;
using SharedKernel.Core.CQRS;

namespace Inventories.Application.Inventory.Features.GetAvailability.V1;

/// <summary>
/// Query that retrieves the total and per-location availability for a product, optionally
/// scoped to a single location.
/// </summary>
/// <param name="ProductId">The product identifier to check availability for.</param>
/// <param name="LocationId">An optional location identifier that, when supplied, restricts the result to that single location.</param>
public sealed record GetAvailabilityQuery(Guid ProductId, Guid? LocationId) : IQuery<AvailabilityDto>;
