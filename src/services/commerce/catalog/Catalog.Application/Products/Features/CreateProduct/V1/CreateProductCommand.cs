using Catalog.Application.Products.Responses;
using SharedKernel.Core.CQRS;

namespace Catalog.Application.Products.Features.CreateProduct.V1;

/// <summary>Creates a product with a single default variant.</summary>
public sealed record CreateProductCommand(
    string Name,
    string? Description,
    Guid? CategoryId,
    string Sku,
    decimal SellPriceAmount,
    string SellPriceCurrency) : ICommand<ProductDto>;
