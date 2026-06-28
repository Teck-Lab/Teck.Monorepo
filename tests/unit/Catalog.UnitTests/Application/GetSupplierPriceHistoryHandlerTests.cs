using Ardalis.Specification;
using Catalog.Application.Suppliers.Features.GetSupplierPriceHistory.V1;
using Catalog.Domain.Entities;
using Catalog.Domain.ValueObjects;
using NSubstitute;
using SharedKernel.Core.Database;
using Xunit;

namespace Catalog.UnitTests.Application;

public sealed class GetSupplierPriceHistoryHandlerTests
{
    [Fact]
    public async Task Handle_WithLinkedSupplier_ReturnsHistory()
    {
        var product = Product.Create("tenant-1", "Widget", null, null, "WIDGET-1", new Money(9.99m, "USD"));
        var variantId = product.Variants[0].Id;
        var supplierId = Guid.NewGuid();
        product.LinkSupplier(variantId, supplierId, new Money(5m, "USD"), "A", 7, 1, isPreferred: true);
        product.ChangeSupplierCost(variantId, supplierId, new Money(6.50m, "USD"));

        var repository = Substitute.For<IGenericReadRepository<Product, System.Guid>>();
        repository.FirstOrDefaultAsync(Arg.Any<ISpecification<Product>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Product?>(product));

        var result = await GetSupplierPriceHistoryHandler.Handle(
            new GetSupplierPriceHistoryQuery(variantId, supplierId), repository, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.Count);
    }

    [Fact]
    public async Task Handle_WithMissingProduct_ReturnsNotFound()
    {
        var repository = Substitute.For<IGenericReadRepository<Product, System.Guid>>();
        repository.FirstOrDefaultAsync(Arg.Any<ISpecification<Product>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Product?>(null));

        var result = await GetSupplierPriceHistoryHandler.Handle(
            new GetSupplierPriceHistoryQuery(Guid.NewGuid(), Guid.NewGuid()), repository, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorOr.ErrorType.NotFound, result.FirstError.Type);
    }
}
