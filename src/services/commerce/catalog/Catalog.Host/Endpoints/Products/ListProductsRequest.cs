namespace Catalog.Host.Endpoints.Products;

/// <summary>Request to list products, optionally filtered by category.</summary>
/// <param name="CategoryId">The optional category filter.</param>
public sealed record ListProductsRequest(Guid? CategoryId);
