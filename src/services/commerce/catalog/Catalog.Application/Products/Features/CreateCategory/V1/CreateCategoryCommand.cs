using Catalog.Application.Products.Responses;
using SharedKernel.Core.CQRS;

namespace Catalog.Application.Products.Features.CreateCategory.V1;

/// <summary>Creates a category.</summary>
public sealed record CreateCategoryCommand(string Name, string Slug, Guid? ParentId) : ICommand<CategoryDto>;
