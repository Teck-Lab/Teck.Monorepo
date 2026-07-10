using ErrorOr;
using Pricing.Application.Pricing.Responses;
using SharedKernel.Core.CQRS;

namespace Pricing.Application.Pricing.Features.ActivatePriceList.V1;

/// <summary>Command that activates a price list.</summary>
/// <param name="Id">The list identifier.</param>
public sealed record ActivatePriceListCommand(Guid Id) : ICommand<ErrorOr<PriceListDto>>;
