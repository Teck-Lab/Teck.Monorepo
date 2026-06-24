using Catalog.Application.Products.Responses;
using Catalog.Domain.Entities;
using Riok.Mapperly.Abstractions;

namespace Catalog.Application.Products.Mapping;

/// <summary>Compile-time mapping for products, variants, and categories.</summary>
[Mapper]
public static partial class ProductMapper
{
    /// <summary>Maps a product (and its variant tree) to a DTO.</summary>
    public static partial ProductDto ToDto(this Product product);

    /// <summary>Maps a single variant to a DTO.</summary>
    public static partial VariantDto ToVariantDto(this Variant variant);

    /// <summary>Maps a category to a DTO.</summary>
    public static partial CategoryDto ToDto(this Category category);

    /// <summary>Maps products to lightweight summaries.</summary>
    public static partial IReadOnlyList<ProductSummaryDto> ToSummaries(this IEnumerable<Product> products);
}
