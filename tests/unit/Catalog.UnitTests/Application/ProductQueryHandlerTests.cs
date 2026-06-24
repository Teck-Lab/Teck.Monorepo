using Ardalis.Specification;
using Catalog.Application.Products.Features.GetProduct.V1;
using Catalog.Application.Products.Features.ListProducts.V1;
using Catalog.Domain.Entities;
using Catalog.Domain.ValueObjects;
using NSubstitute;
using Xunit;

namespace Catalog.UnitTests.Application;

public sealed class ProductQueryHandlerTests
{
    [Fact]
    public async Task GetProduct_WhenFound_ReturnsDto()
    {
        var product = Product.Create("tenant-1", "Widget", null, null, "WIDGET-1", new Money(9.99m, "USD"));
        var repository = Substitute.For<IRepositoryBase<Product>>();
        repository.FirstOrDefaultAsync(Arg.Any<ISpecification<Product>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Product?>(product));

        var result = await GetProductHandler.Handle(new GetProductQuery(product.Id), repository, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(product.Id, result.Value.Id);
    }

    [Fact]
    public async Task GetProduct_WhenMissing_ReturnsNotFound()
    {
        var repository = Substitute.For<IRepositoryBase<Product>>();
        repository.FirstOrDefaultAsync(Arg.Any<ISpecification<Product>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Product?>(null));

        var result = await GetProductHandler.Handle(new GetProductQuery(Guid.NewGuid()), repository, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorOr.ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task ListProducts_ReturnsSummaries()
    {
        var a = Product.Create("tenant-1", "A", null, null, "A-1", new Money(1m, "USD"));
        var b = Product.Create("tenant-1", "B", null, null, "B-1", new Money(2m, "USD"));
        var repository = Substitute.For<IRepositoryBase<Product>>();
        repository.ListAsync(Arg.Any<ISpecification<Product>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<List<Product>>([a, b]));

        var result = await ListProductsHandler.Handle(new ListProductsQuery(null), repository, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.Count);
    }
}
