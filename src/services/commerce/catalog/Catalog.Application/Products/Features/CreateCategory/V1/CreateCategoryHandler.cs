using Catalog.Application.Products.Mapping;
using Catalog.Application.Products.Responses;
using Catalog.Domain.Entities;
using SharedKernel.Core.Database;

namespace Catalog.Application.Products.Features.CreateCategory.V1;

/// <summary>Handles <see cref="CreateCategoryCommand"/>.</summary>
public static class CreateCategoryHandler
{
    /// <summary>Creates and persists a category. TenantId is stamped by the Host interceptor on save.</summary>
    /// <param name="command">The command describing the category to create.</param>
    /// <param name="repository">The write repository for persisting the category.</param>
    /// <param name="unitOfWork">The unit of work used to commit changes.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public static async Task<CategoryDto> Handle(
        CreateCategoryCommand command,
        IGenericWriteRepository<Category, Guid> repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var category = Category.Create(tenantId: null, command.Name, command.Slug, command.ParentId);
        await repository.AddAsync(category, ct).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return category.ToDto();
    }
}
