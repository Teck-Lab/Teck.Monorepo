using Catalog.Application.Suppliers.Features.LinkVariantSupplier.V1;
using Catalog.Domain.Entities;
using Catalog.Domain.ValueObjects;
using Catalog.UnitTests.TestContext;
using Xunit;

namespace Catalog.UnitTests.Application;

public sealed class LinkVariantSupplierHandlerTests
{
    [Fact]
    public async Task Handle_WithExistingVariant_LinksSupplierWithInitialHistory()
    {
        var product = Product.Create("tenant-1", "Widget", null, null, "WIDGET-1", new Money(9.99m, "USD"));
        using (var seed = CatalogTestContext.CreateInMemory("link"))
        {
            seed.Products.Add(product);
            await seed.SaveChangesAsync();
        }
        using var db = CatalogTestContext.CreateWithStubbedSave("link");
        var supplierId = Guid.NewGuid();
        var command = new LinkVariantSupplierCommand(product.Variants[0].Id, supplierId, 5m, "USD", "ACME-9", 7, 10, true);

        var result = await LinkVariantSupplierHandler.Handle(command, db, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(supplierId, result.Value.SupplierId);
        Assert.Equal(5m, result.Value.CostPriceAmount);
        Assert.True(result.Value.IsPreferred);
    }

    [Fact]
    public async Task Handle_WithMissingVariant_ReturnsNotFound()
    {
        using var db = CatalogTestContext.CreateWithStubbedSave("link-missing");
        var command = new LinkVariantSupplierCommand(Guid.NewGuid(), Guid.NewGuid(), 5m, "USD", "X", 1, 1, false);

        var result = await LinkVariantSupplierHandler.Handle(command, db, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorOr.ErrorType.NotFound, result.FirstError.Type);
    }
}
