namespace Catalog.Host.Endpoints.Products;

/// <summary>Request to create a product with a single default variant.</summary>
/// <param name="Name">The product name.</param>
/// <param name="Description">The optional product description.</param>
/// <param name="CategoryId">The optional owning category.</param>
/// <param name="Sku">The default variant SKU.</param>
/// <param name="SellPriceAmount">The default variant sell price amount.</param>
/// <param name="SellPriceCurrency">The ISO currency code for the sell price.</param>
public sealed record CreateProductRequest(
    string Name,
    string? Description,
    Guid? CategoryId,
    string Sku,
    decimal SellPriceAmount,
    string SellPriceCurrency);
