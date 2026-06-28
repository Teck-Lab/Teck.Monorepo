using Catalog.Application.Products.Features.CreateCategory.V1;
using Catalog.UnitTests.TestContext;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Catalog.UnitTests.Application;

public sealed class CreateCategoryHandlerTests
{
    [Fact]
    public async Task Handle_WithValidCommand_PersistsAndReturnsDto()
    {
        using var db = CatalogTestContext.CreateInMemory();
        var repository = CatalogTestContext.WriteRepo<Catalog.Domain.Entities.Category>(db);
        var unitOfWork = CatalogTestContext.UnitOfWork(db);
        var command = new CreateCategoryCommand("Beverages", "beverages", null);

        var dto = await CreateCategoryHandler.Handle(command, repository, unitOfWork, CancellationToken.None);

        Assert.Equal("Beverages", dto.Name);
        Assert.Equal("beverages", dto.Slug);
        Assert.NotEqual(Guid.Empty, dto.Id);
        Assert.Equal(1, await db.Categories.CountAsync());
    }
}
