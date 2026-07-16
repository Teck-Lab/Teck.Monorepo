using Catalog.Application.Products.Features.AddVariant.V1;

namespace Catalog.Host.Endpoints.Products;

/// <summary>Request to add a variant to an existing product.</summary>
/// <param name="ProductId">The owning product identifier.</param>
/// <param name="Sku">The variant SKU.</param>
/// <param name="SellPriceAmount">The variant sell price amount.</param>
/// <param name="SellPriceCurrency">The ISO currency code.</param>
/// <param name="Attributes">The distinguishing attributes.</param>
public sealed record AddVariantRequest(
    Guid ProductId,
    string Sku,
    decimal SellPriceAmount,
    string SellPriceCurrency,
    IReadOnlyList<VariantAttributeInput> Attributes);
