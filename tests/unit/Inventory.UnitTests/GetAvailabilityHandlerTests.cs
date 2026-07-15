using Ardalis.Specification;
using Inventories.Application.Inventory.Features.GetAvailability.V1;
using Inventories.Domain.Entities;
using NSubstitute;
using SharedKernel.Core.Database;
using Xunit;

namespace Inventories.UnitTests;

/// <summary>Tests for <see cref="GetAvailabilityHandler"/>.</summary>
public sealed class GetAvailabilityHandlerTests
{
    private static IGenericReadRepository<StockItem, Guid> RepositoryReturning(params StockItem[] items)
    {
        var repository = Substitute.For<IGenericReadRepository<StockItem, Guid>>();
        repository.ListAsync(Arg.Any<ISpecification<StockItem>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<StockItem>>(items));
        return repository;
    }

    private static IGenericReadRepository<Reservation, Guid> NoActiveReservations()
    {
        var repository = Substitute.For<IGenericReadRepository<Reservation, Guid>>();
        repository.ListAsync(Arg.Any<ISpecification<Reservation>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Reservation>>([]));
        return repository;
    }

    [Fact]
    public async Task Handle_ProductStockedAtTwoLocations_SumsAvailableAcrossLocations()
    {
        var productId = Guid.NewGuid();
        var locationA = StockItem.Create(productId, Guid.NewGuid(), "tenant-1", quantityOnHand: 3, allowBackorder: false, reorderThreshold: 0);
        var locationB = StockItem.Create(productId, Guid.NewGuid(), "tenant-1", quantityOnHand: 4, allowBackorder: false, reorderThreshold: 0);
        var repository = RepositoryReturning(locationA, locationB);

        var dto = await GetAvailabilityHandler.Handle(
            new GetAvailabilityQuery(productId, null),
            repository,
            NoActiveReservations(),
            TimeProvider.System,
            CancellationToken.None);

        Assert.Equal(productId, dto.ProductId);
        Assert.Equal(7, dto.Available);
        Assert.Equal(2, dto.ByLocation.Count);
    }
}
