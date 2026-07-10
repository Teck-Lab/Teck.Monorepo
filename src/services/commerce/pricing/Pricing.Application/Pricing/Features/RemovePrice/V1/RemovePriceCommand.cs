using ErrorOr;
using Pricing.Application.Pricing.Responses;
using SharedKernel.Core.CQRS;

namespace Pricing.Application.Pricing.Features.RemovePrice.V1;

/// <summary>Command that removes a product's price from a list.</summary>
/// <param name="PriceListId">The owning price list.</param>
/// <param name="ProductId">The product identifier.</param>
public sealed record RemovePriceCommand(Guid PriceListId, Guid ProductId) : ICommand<ErrorOr<PriceListDto>>;
