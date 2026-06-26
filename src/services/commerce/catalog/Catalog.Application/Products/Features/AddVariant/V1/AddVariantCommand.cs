using Catalog.Application.Products.Responses;
using ErrorOr;
using SharedKernel.Core.CQRS;

namespace Catalog.Application.Products.Features.AddVariant.V1;

/// <summary>Adds a non-default variant to an existing product.</summary>
/// <param name="ProductId">The identifier of the product the variant is added to.</param>
/// <param name="Sku">The stock-keeping unit that uniquely identifies the variant.</param>
/// <param name="SellPriceAmount">The numeric sell price amount for the variant.</param>
/// <param name="SellPriceCurrency">The ISO currency code for the sell price.</param>
/// <param name="Attributes">The attributes that distinguish this variant.</param>
public sealed record AddVariantCommand(
    Guid ProductId,
    string Sku,
    decimal SellPriceAmount,
    string SellPriceCurrency,
    IReadOnlyList<VariantAttributeInput> Attributes) : ICommand<ErrorOr<VariantDto>>;
