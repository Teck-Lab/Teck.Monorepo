using Catalog.Application.Suppliers.Features.SetPreferredSupplier.V1;
using Catalog.Domain.Entities;
using Catalog.Domain.ValueObjects;
using Catalog.UnitTests.TestContext;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Catalog.UnitTests.Application;

public sealed class SetPreferredSupplierHandlerTests
{
    [Fact]
    public async Task Handle_MakesExactlyOnePreferred()
    {
        var product = Product.Create("tenant-1", "Widget", null, null, "WIDGET-1", new Money(9.99m, "USD"));
        var variantId = product.Variants[0].Id;
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        product.LinkSupplier(variantId, a, new Money(5m, "USD"), "A", 7, 1, isPreferred: true);
        product.LinkSupplier(variantId, b, new Money(6m, "USD"), "B", 7, 1, isPreferred: false);
        using (var seed = CatalogTestContext.CreateInMemory("preferred"))
        {
            seed.Products.Add(product);
            await seed.SaveChangesAsync();
        }
        using var db = CatalogTestContext.CreateWithStubbedSave("preferred");

        var result = await SetPreferredSupplierHandler.Handle(
            new SetPreferredSupplierCommand(variantId, b), db, CancellationToken.None);

        Assert.False(result.IsError);
        // The handler loaded + mutated the product in `db`'s change tracker; re-querying `db`
        // returns that same tracked instance (identity map), reflecting the in-memory mutation.
        var tracked = await db.Products.FirstAsync();
        var suppliers = tracked.Variants[0].Suppliers;
        Assert.Equal(1, suppliers.Count(s => s.IsPreferred));
        Assert.True(suppliers.Single(s => s.SupplierId == b).IsPreferred);
    }

    [Fact]
    public async Task Handle_WithUnlinkedSupplier_ReturnsNotFound()
    {
        var product = Product.Create("tenant-1", "Widget", null, null, "WIDGET-1", new Money(9.99m, "USD"));
        using (var seed = CatalogTestContext.CreateInMemory("preferred-missing"))
        {
            seed.Products.Add(product);
            await seed.SaveChangesAsync();
        }
        using var db = CatalogTestContext.CreateWithStubbedSave("preferred-missing");

        var result = await SetPreferredSupplierHandler.Handle(
            new SetPreferredSupplierCommand(product.Variants[0].Id, Guid.NewGuid()), db, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorOr.ErrorType.NotFound, result.FirstError.Type);
    }
}
