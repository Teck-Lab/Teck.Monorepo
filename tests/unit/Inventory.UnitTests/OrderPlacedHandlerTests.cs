using Ardalis.Specification;
using Inventories.Application.Inventory;
using Inventories.Application.Inventory.EventHandlers.IntegrationEvents;
using Inventories.Domain.Entities;
using Inventories.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;
using Xunit;

namespace Inventories.UnitTests;

public sealed class OrderPlacedHandlerTests
{
    private const string Tenant = "tenant-1";

    private static IGenericWriteRepository<StockItem, Guid> StockRepoReturning(params StockItem[] items)
    {
        var repository = Substitute.For<IGenericWriteRepository<StockItem, Guid>>();
        repository.ListAsync(Arg.Any<ISpecification<StockItem>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<StockItem>>(items.ToList()));
        return repository;
    }

    private static IGenericWriteRepository<Reservation, Guid> ReservationRepo(Reservation? existing = null)
    {
        var repository = Substitute.For<IGenericWriteRepository<Reservation, Guid>>();
        repository.FirstOrDefaultAsync(Arg.Any<ISpecification<Reservation>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(existing));
        return repository;
    }

    private static OrderPlacedIntegrationEvent OrderFor(Guid orderId, Guid productId, int quantity) => new()
    {
        OrderId = orderId,
        CustomerId = Guid.NewGuid(),
        TenantId = Tenant,
        Status = "Placed",
        Total = 10m,
        CreatedAt = DateTimeOffset.UtcNow,
        Lines = [new OrderPlacedLine(productId, "Widget", quantity, 5m, quantity * 5m)],
    };

    [Fact]
    public async Task Handle_EnoughStockAcrossTwoLocations_ReservesPublishesOnceAndCommitsOnce()
    {
        var productId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var locationA = StockItem.Create(productId, Guid.NewGuid(), Tenant, quantityOnHand: 3, allowBackorder: false, reorderThreshold: -100);
        var locationB = StockItem.Create(productId, Guid.NewGuid(), Tenant, quantityOnHand: 10, allowBackorder: false, reorderThreshold: -100);
        var stockRepo = StockRepoReturning(locationA, locationB);
        var reservationRepo = ReservationRepo();

        // Explicit priority (A before B) makes the two-location split deterministic: A fills first.
        var locationPriorities = Substitute.For<IGenericReadRepository<LocationPriority, Guid>>();
        locationPriorities.FirstOrDefaultAsync(Arg.Any<ISpecification<LocationPriority>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<LocationPriority?>(
                LocationPriority.Create(Tenant, [locationA.LocationId, locationB.LocationId])));
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IMessageBus>();

        await OrderPlacedHandler.Handle(
            OrderFor(orderId, productId, quantity: 6),
            stockRepo,
            reservationRepo,
            locationPriorities,
            unitOfWork,
            Substitute.For<IServiceScopeFactory>(),
            Options.Create(new InventoryOptions()),
            bus,
            CancellationToken.None);

        // Six units drawn across the two locations, committed in a single SaveChanges.
        Assert.Equal(6, locationA.QuantityReserved + locationB.QuantityReserved);
        Assert.Equal(3, locationA.QuantityReserved);
        Assert.Equal(3, locationB.QuantityReserved);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await reservationRepo.Received(1).AddAsync(
            Arg.Is<Reservation>(r =>
                r.Status == ReservationStatus.Committed
                && r.SourceType == ReservationSource.Order
                && r.SourceId == orderId
                && r.Lines.Count == 1),
            Arg.Any<CancellationToken>());
        await bus.Received(1).PublishAsync(Arg.Is<StockReservedIntegrationEvent>(evt =>
            evt.SourceId == orderId
            && evt.SourceType == "Order"
            && evt.TenantId == Tenant
            && evt.Lines.Count == 1));
        await bus.DidNotReceive().PublishAsync(Arg.Any<StockReservationRejectedIntegrationEvent>());
    }

    [Fact]
    public async Task Handle_RedeliveryOfSameOrder_PublishesNothingAndDoesNotCommit()
    {
        var productId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var existing = Reservation.CreateCommitted(ReservationSource.Order, orderId, Tenant, []);
        var stockRepo = StockRepoReturning(StockItem.Create(productId, Guid.NewGuid(), Tenant, 100, false, -100));
        var reservationRepo = ReservationRepo(existing);
        var locationPriorities = Substitute.For<IGenericReadRepository<LocationPriority, Guid>>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IMessageBus>();

        await OrderPlacedHandler.Handle(
            OrderFor(orderId, productId, quantity: 5),
            stockRepo,
            reservationRepo,
            locationPriorities,
            unitOfWork,
            Substitute.For<IServiceScopeFactory>(),
            Options.Create(new InventoryOptions()),
            bus,
            CancellationToken.None);

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await reservationRepo.DidNotReceive().AddAsync(Arg.Any<Reservation>(), Arg.Any<CancellationToken>());
        await bus.DidNotReceive().PublishAsync(Arg.Any<StockReservedIntegrationEvent>());
        await bus.DidNotReceive().PublishAsync(Arg.Any<StockReservationRejectedIntegrationEvent>());
    }

    [Fact]
    public async Task Handle_LineExceedsStockWithBackorderOff_PublishesRejectedAndDoesNotCommit()
    {
        var productId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var only = StockItem.Create(productId, Guid.NewGuid(), Tenant, quantityOnHand: 5, allowBackorder: false, reorderThreshold: -100);
        var stockRepo = StockRepoReturning(only);
        var reservationRepo = ReservationRepo();
        var locationPriorities = Substitute.For<IGenericReadRepository<LocationPriority, Guid>>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IMessageBus>();

        await OrderPlacedHandler.Handle(
            OrderFor(orderId, productId, quantity: 10),
            stockRepo,
            reservationRepo,
            locationPriorities,
            unitOfWork,
            Substitute.For<IServiceScopeFactory>(),
            Options.Create(new InventoryOptions()),
            bus,
            CancellationToken.None);

        Assert.Equal(0, only.QuantityReserved);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await reservationRepo.DidNotReceive().AddAsync(Arg.Any<Reservation>(), Arg.Any<CancellationToken>());
        await bus.DidNotReceive().PublishAsync(Arg.Any<StockReservedIntegrationEvent>());
        await bus.Received(1).PublishAsync(Arg.Is<StockReservationRejectedIntegrationEvent>(evt =>
            evt.SourceId == orderId
            && evt.SourceType == "Order"
            && evt.TenantId == Tenant
            && evt.Lines.Count == 1
            && evt.Lines[0].ProductId == productId
            && evt.Lines[0].RequestedQuantity == 10));
    }
}
