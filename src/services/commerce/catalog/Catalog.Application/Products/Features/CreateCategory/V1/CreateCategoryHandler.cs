using Catalog.Application.Database;
using Catalog.Application.Products.Mapping;
using Catalog.Application.Products.Responses;
using Catalog.Domain.Entities;

namespace Catalog.Application.Products.Features.CreateCategory.V1;

/// <summary>Handles <see cref="CreateCategoryCommand"/>.</summary>
public static class CreateCategoryHandler
{
    /// <summary>Creates and persists a category. TenantId is stamped by the Host interceptor on save.</summary>
    /// <param name="command">The command describing the category to create.</param>
    /// <param name="db">The catalog write context.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public static async Task<CategoryDto> Handle(
        CreateCategoryCommand command,
        CatalogDbContext db,
        CancellationToken ct)
    {
        var category = Category.Create(string.Empty, command.Name, command.Slug, command.ParentId);
        db.Categories.Add(category);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return category.ToDto();
    }
}
