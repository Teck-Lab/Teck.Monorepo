namespace Catalog.Application.Products.Responses;

/// <summary>A product with its variants.</summary>
public sealed record ProductDto(
    Guid Id,
    string Name,
    string? Description,
    Guid? CategoryId,
    bool IsActive,
    IReadOnlyList<VariantDto> Variants);
