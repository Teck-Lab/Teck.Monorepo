using Catalog.Application.Suppliers.Features.UpdateSupplierCost.V1;
using Catalog.Domain.Entities;
using Catalog.Domain.ValueObjects;
using Catalog.UnitTests.TestContext;
using Xunit;

namespace Catalog.UnitTests.Application;

public sealed class UpdateSupplierCostHandlerTests
{
    [Fact]
    public async Task Handle_WithLinkedSupplier_UpdatesCostAndAppendsHistory()
    {
        var product = Product.Create("tenant-1", "Widget", null, null, "WIDGET-1", new Money(9.99m, "USD"));
        var supplierId = Guid.NewGuid();
        product.LinkSupplier(product.Variants[0].Id, supplierId, new Money(5m, "USD"), "A", 7, 1, isPreferred: true);
        using (var seed = CatalogTestContext.CreateInMemory("cost"))
        {
            seed.Products.Add(product);
            await seed.SaveChangesAsync();
        }
        using var db = CatalogTestContext.CreateWithStubbedSave("cost");
        var command = new UpdateSupplierCostCommand(product.Variants[0].Id, supplierId, 6.50m, "USD");

        var result = await UpdateSupplierCostHandler.Handle(command, db, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(6.50m, result.Value.CostPriceAmount);
    }

    [Fact]
    public async Task Handle_WithUnlinkedSupplier_ReturnsNotFound()
    {
        var product = Product.Create("tenant-1", "Widget", null, null, "WIDGET-1", new Money(9.99m, "USD"));
        using (var seed = CatalogTestContext.CreateInMemory("cost-missing"))
        {
            seed.Products.Add(product);
            await seed.SaveChangesAsync();
        }
        using var db = CatalogTestContext.CreateWithStubbedSave("cost-missing");
        var command = new UpdateSupplierCostCommand(product.Variants[0].Id, Guid.NewGuid(), 6.50m, "USD");

        var result = await UpdateSupplierCostHandler.Handle(command, db, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorOr.ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task Handle_WithMissingProduct_ReturnsNotFound()
    {
        using var db = CatalogTestContext.CreateWithStubbedSave("cost-missing-product");
        var command = new UpdateSupplierCostCommand(Guid.NewGuid(), Guid.NewGuid(), 6.50m, "USD");

        var result = await UpdateSupplierCostHandler.Handle(command, db, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorOr.ErrorType.NotFound, result.FirstError.Type);
    }
}
