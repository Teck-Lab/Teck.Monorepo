namespace Catalog.Application.Products.Responses;

/// <summary>A category in the hierarchy.</summary>
public sealed record CategoryDto(Guid Id, string Name, string Slug, Guid? ParentId);
