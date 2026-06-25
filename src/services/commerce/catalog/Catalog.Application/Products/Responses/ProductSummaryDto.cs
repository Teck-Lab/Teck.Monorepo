namespace Catalog.Application.Products.Responses;

/// <summary>A lightweight product list item.</summary>
public sealed record ProductSummaryDto(Guid Id, string Name, bool IsActive, Guid? CategoryId);
