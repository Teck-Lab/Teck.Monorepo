namespace Catalog.Host.Endpoints.Products;

/// <summary>Request to fetch a product by identifier.</summary>
/// <param name="ProductId">The product identifier.</param>
public sealed record GetProductRequest(Guid ProductId);
