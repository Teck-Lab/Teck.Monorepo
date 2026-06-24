using Ardalis.Specification;
using Catalog.Application.Suppliers.Features.GetSupplier.V1;
using Catalog.Domain.Entities;
using NSubstitute;
using Xunit;

namespace Catalog.UnitTests.Application;

public sealed class GetSupplierHandlerTests
{
    [Fact]
    public async Task Handle_WhenFound_ReturnsDto()
    {
        var supplier = Supplier.Create("tenant-1", "Acme");
        var repository = Substitute.For<IRepositoryBase<Supplier>>();
        repository.FirstOrDefaultAsync(Arg.Any<ISpecification<Supplier>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Supplier?>(supplier));

        var result = await GetSupplierHandler.Handle(new GetSupplierQuery(supplier.Id), repository, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(supplier.Id, result.Value.Id);
    }

    [Fact]
    public async Task Handle_WhenMissing_ReturnsNotFound()
    {
        var repository = Substitute.For<IRepositoryBase<Supplier>>();
        repository.FirstOrDefaultAsync(Arg.Any<ISpecification<Supplier>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Supplier?>(null));

        var result = await GetSupplierHandler.Handle(new GetSupplierQuery(Guid.NewGuid()), repository, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorOr.ErrorType.NotFound, result.FirstError.Type);
    }
}
