namespace Catalog.Host.Endpoints.Products;

/// <summary>Request to create a category.</summary>
/// <param name="Name">The category name.</param>
/// <param name="Slug">The URL slug.</param>
/// <param name="ParentId">The optional parent category.</param>
public sealed record CreateCategoryRequest(string Name, string Slug, Guid? ParentId);
