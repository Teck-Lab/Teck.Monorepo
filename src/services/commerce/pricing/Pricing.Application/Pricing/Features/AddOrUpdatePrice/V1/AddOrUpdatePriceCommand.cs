using ErrorOr;
using Pricing.Application.Pricing.Responses;
using SharedKernel.Core.CQRS;

namespace Pricing.Application.Pricing.Features.AddOrUpdatePrice.V1;

/// <summary>Command that adds or updates a product's price within a list.</summary>
/// <param name="PriceListId">The owning price list.</param>
/// <param name="ProductId">The product identifier.</param>
/// <param name="Amount">The base unit amount (in the list's currency).</param>
/// <param name="Tiers">The quantity tiers.</param>
public sealed record AddOrUpdatePriceCommand(
    Guid PriceListId,
    Guid ProductId,
    decimal Amount,
    IReadOnlyList<PriceTierInput> Tiers) : ICommand<ErrorOr<PriceListDto>>;
