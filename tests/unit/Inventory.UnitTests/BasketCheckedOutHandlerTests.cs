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

public sealed class BasketCheckedOutHandlerTests
{
    private const string Tenant = "tenant-1";
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 5, 12, 0, 0, TimeSpan.Zero);

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

    private static BasketCheckedOutIntegrationEvent BasketFor(Guid basketId, Guid productId, int quantity) => new()
    {
        BasketId = basketId,
        CustomerId = Guid.NewGuid(),
        TenantId = Tenant,
        Subtotal = quantity * 5m,
        CheckedOutAt = DateTimeOffset.UtcNow,
        Items = [new BasketCheckedOutLine(productId, "Widget", 5m, quantity, quantity * 5m)],
    };

    [Fact]
    public async Task Handle_EnoughStockAcrossTwoLocations_HoldsReservesPublishesOnceAndCommitsOnce()
    {
        var productId = Guid.NewGuid();
        var basketId = Guid.NewGuid();
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
        var options = new InventoryOptions { HoldTtl = TimeSpan.FromMinutes(15) };
        var timeProvider = new FixedTimeProvider(FixedNow);

        await BasketCheckedOutHandler.Handle(
            BasketFor(basketId, productId, quantity: 6),
            stockRepo,
            reservationRepo,
            locationPriorities,
            unitOfWork,
            Substitute.For<IServiceScopeFactory>(),
            Options.Create(options),
            timeProvider,
            bus,
            CancellationToken.None);

        // Six units drawn across the two locations, committed in a single SaveChanges.
        Assert.Equal(6, locationA.QuantityReserved + locationB.QuantityReserved);
        Assert.Equal(3, locationA.QuantityReserved);
        Assert.Equal(3, locationB.QuantityReserved);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await reservationRepo.Received(1).AddAsync(
            Arg.Is<Reservation>(r =>
                r.Status == ReservationStatus.Held
                && r.ExpiresAt == FixedNow + options.HoldTtl
                && r.SourceType == ReservationSource.Basket
                && r.SourceId == basketId
                && r.Lines.Count == 1),
            Arg.Any<CancellationToken>());
        await bus.Received(1).PublishAsync(Arg.Is<StockReservedIntegrationEvent>(evt =>
            evt.SourceId == basketId
            && evt.SourceType == "Basket"
            && evt.TenantId == Tenant
            && evt.Lines.Count == 1));
        await bus.DidNotReceive().PublishAsync(Arg.Any<StockReservationRejectedIntegrationEvent>());
    }

    [Fact]
    public async Task Handle_RedeliveryOfSameBasket_PublishesNothingAndDoesNotCommit()
    {
        var productId = Guid.NewGuid();
        var basketId = Guid.NewGuid();
        var existing = Reservation.CreateHeld(ReservationSource.Basket, basketId, Tenant, FixedNow, []);
        var stockRepo = StockRepoReturning(StockItem.Create(productId, Guid.NewGuid(), Tenant, 100, false, -100));
        var reservationRepo = ReservationRepo(existing);
        var locationPriorities = Substitute.For<IGenericReadRepository<LocationPriority, Guid>>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IMessageBus>();

        await BasketCheckedOutHandler.Handle(
            BasketFor(basketId, productId, quantity: 5),
            stockRepo,
            reservationRepo,
            locationPriorities,
            unitOfWork,
            Substitute.For<IServiceScopeFactory>(),
            Options.Create(new InventoryOptions()),
            new FixedTimeProvider(FixedNow),
            bus,
            CancellationToken.None);

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await reservationRepo.DidNotReceive().AddAsync(Arg.Any<Reservation>(), Arg.Any<CancellationToken>());
        await bus.DidNotReceive().PublishAsync(Arg.Any<StockReservedIntegrationEvent>());
        await bus.DidNotReceive().PublishAsync(Arg.Any<StockReservationRejectedIntegrationEvent>());
    }

    /// <summary>A minimal <see cref="TimeProvider"/> stub returning a fixed instant, used to make hold-expiry assertions deterministic.</summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
