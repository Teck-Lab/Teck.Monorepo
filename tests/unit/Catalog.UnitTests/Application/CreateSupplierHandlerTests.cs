using Catalog.Application.Suppliers.Features.CreateSupplier.V1;
using Catalog.UnitTests.TestContext;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Catalog.UnitTests.Application;

public sealed class CreateSupplierHandlerTests
{
    [Fact]
    public async Task Handle_WithValidCommand_PersistsAndReturnsDto()
    {
        using var db = CatalogTestContext.CreateInMemory();
        var command = new CreateSupplierCommand("Acme", "sales@acme.test", "+1-555-0100");

        var dto = await CreateSupplierHandler.Handle(command, db, CancellationToken.None);

        Assert.Equal("Acme", dto.Name);
        Assert.True(dto.IsActive);
        Assert.Equal(1, await db.Suppliers.CountAsync());
    }
}
