namespace Catalog.Application.Products.Responses;

/// <summary>A sellable variant with its flattened sell price.</summary>
public sealed record VariantDto(
    Guid Id,
    string Sku,
    decimal SellPriceAmount,
    string SellPriceCurrency,
    bool IsDefault,
    bool IsActive,
    IReadOnlyList<VariantAttributeDto> Attributes);
