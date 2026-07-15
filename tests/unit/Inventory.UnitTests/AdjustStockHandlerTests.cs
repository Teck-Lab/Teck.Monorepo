using Ardalis.Specification;
using Inventories.Application.Inventory.Features.AdjustStock.V1;
using Inventories.Domain.Entities;
using NSubstitute;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;
using Xunit;

namespace Inventories.UnitTests;

public sealed class AdjustStockHandlerTests
{
    private static IGenericWriteRepository<StockItem, Guid> RepositoryReturning(StockItem item)
    {
        var repository = Substitute.For<IGenericWriteRepository<StockItem, Guid>>();
        repository.FirstOrDefaultAsync(Arg.Any<ISpecification<StockItem>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StockItem?>(item));
        return repository;
    }

    private static IGenericWriteRepository<Reservation, Guid> NoBackorderedReservations()
    {
        var repository = Substitute.For<IGenericWriteRepository<Reservation, Guid>>();
        repository.ListAsync(Arg.Any<ISpecification<Reservation>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Reservation>>([]));
        return repository;
    }

    [Fact]
    public async Task Handle_NegativeAdjustThatDepletes_PublishesStockDepletedAndCommitsOnce()
    {
        var item = StockItem.Create(Guid.NewGuid(), Guid.NewGuid(), "tenant-1", quantityOnHand: 5, allowBackorder: false, reorderThreshold: -10);
        var repository = RepositoryReturning(item);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IMessageBus>();

        var dto = await AdjustStockHandler.Handle(new AdjustStockCommand(item.Id, -5), repository, NoBackorderedReservations(), unitOfWork, bus, TimeProvider.System, CancellationToken.None);

        Assert.Equal(0, dto.Available);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await bus.Received(1).PublishAsync(Arg.Is<StockDepletedIntegrationEvent>(evt =>
            evt.ProductId == item.ProductId
            && evt.LocationId == item.LocationId
            && evt.TenantId == "tenant-1"
            && evt.Available == 0));
    }

    [Fact]
    public async Task Handle_PositiveAdjustThatCrossesBack_PublishesStockReplenished()
    {
        var item = StockItem.Create(Guid.NewGuid(), Guid.NewGuid(), "tenant-1", quantityOnHand: 0, allowBackorder: false, reorderThreshold: -10);
        var repository = RepositoryReturning(item);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IMessageBus>();

        var dto = await AdjustStockHandler.Handle(new AdjustStockCommand(item.Id, 10), repository, NoBackorderedReservations(), unitOfWork, bus, TimeProvider.System, CancellationToken.None);

        Assert.Equal(10, dto.Available);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await bus.Received(1).PublishAsync(Arg.Is<StockReplenishedIntegrationEvent>(evt =>
            evt.ProductId == item.ProductId
            && evt.LocationId == item.LocationId
            && evt.TenantId == "tenant-1"
            && evt.Available == 10));
    }

    [Fact]
    public async Task Handle_AdjustThatLandsAtOrBelowThreshold_PublishesReorderTriggered()
    {
        var item = StockItem.Create(Guid.NewGuid(), Guid.NewGuid(), "tenant-1", quantityOnHand: 20, allowBackorder: false, reorderThreshold: 10);
        var repository = RepositoryReturning(item);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IMessageBus>();

        var dto = await AdjustStockHandler.Handle(new AdjustStockCommand(item.Id, -12), repository, NoBackorderedReservations(), unitOfWork, bus, TimeProvider.System, CancellationToken.None);

        Assert.Equal(8, dto.Available);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await bus.Received(1).PublishAsync(Arg.Is<ReorderTriggeredIntegrationEvent>(evt =>
            evt.ProductId == item.ProductId
            && evt.LocationId == item.LocationId
            && evt.TenantId == "tenant-1"
            && evt.Available == 8
            && evt.ReorderThreshold == 10));
    }

    [Fact]
    public async Task Handle_AdjustWhileAlreadyBelowThreshold_DoesNotReFireReorderTriggered()
    {
        // Already below the reorder point (available 8 <= 10). A further negative adjust must NOT
        // re-emit ReorderTriggered — the event fires on the crossing, not on every adjustment.
        var item = StockItem.Create(Guid.NewGuid(), Guid.NewGuid(), "tenant-1", quantityOnHand: 8, allowBackorder: false, reorderThreshold: 10);
        var repository = RepositoryReturning(item);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IMessageBus>();

        await AdjustStockHandler.Handle(new AdjustStockCommand(item.Id, -1), repository, NoBackorderedReservations(), unitOfWork, bus, TimeProvider.System, CancellationToken.None);

        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await bus.DidNotReceive().PublishAsync(Arg.Any<ReorderTriggeredIntegrationEvent>());
    }
}
