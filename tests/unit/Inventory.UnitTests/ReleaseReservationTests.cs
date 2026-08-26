using Ardalis.Specification;
using Inventories.Application.Inventory.Features.ReleaseReservation.V1;
using Inventories.Domain.Entities;
using Inventories.Domain.ValueObjects;
using NSubstitute;
using SharedKernel.Core.Database;
using Wolverine;
using Xunit;

namespace Inventories.UnitTests;

/// <summary>Regression tests for idempotent correlated reservation release.</summary>
public sealed class ReleaseReservationTests
{
    [Fact]
    public async Task Handle_ActiveOrderAndBasket_ReleasesBothWithOneCommit()
    {
        var tenantId = "tenant-1";
        var orderId = Guid.NewGuid();
        var basketId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var stock = StockItem.Create(productId, locationId, tenantId, 10, false, 0);
        stock.Reserve(4);
        Reservation order = Reservation.CreateCommitted(ReservationSource.Order, orderId, tenantId, [new ReservationLine(productId, 2, 0, [new Allocation(locationId, 2)])]);
        Reservation basket = Reservation.CreateHeld(ReservationSource.Basket, basketId, tenantId, DateTimeOffset.UtcNow.AddMinutes(1), [new ReservationLine(productId, 2, 0, [new Allocation(locationId, 2)])]);
        var reservations = Substitute.For<IGenericWriteRepository<Reservation, Guid>>();
        reservations.FirstOrDefaultAsync(Arg.Any<ISpecification<Reservation>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Reservation?>(order), Task.FromResult<Reservation?>(basket));
        var stocks = Substitute.For<IGenericWriteRepository<StockItem, Guid>>();
        stocks.FirstOrDefaultAsync(Arg.Any<ISpecification<StockItem>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StockItem?>(stock));
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IMessageBus>();
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(0));

        await ReleaseReservationHandler.Handle(
            new ReleaseReservationCommand(orderId, basketId, tenantId, "correlation", "release-1"),
            reservations,
            stocks,
            unitOfWork,
            bus,
            CancellationToken.None);

        Assert.Equal(ReservationStatus.Released, order.Status);
        Assert.Equal(ReservationStatus.Released, basket.Status);
        Assert.Equal(0, stock.QuantityReserved);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await bus.Received(1).PublishAsync(Arg.Is<SharedKernel.Events.StockReleasedIntegrationEvent>(evt =>
            evt.OrderId == orderId
            && evt.BasketId == basketId
            && evt.TenantId == tenantId
            && evt.SourceCorrelationId == "correlation"
            && evt.RequestId == "release-1"));

        await ReleaseReservationHandler.Handle(
            new ReleaseReservationCommand(orderId, basketId, tenantId, "correlation", "release-1"),
            reservations,
            stocks,
            unitOfWork,
            bus,
            CancellationToken.None);

        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await bus.Received(1).PublishAsync(Arg.Any<SharedKernel.Events.StockReleasedIntegrationEvent>());
    }
}
