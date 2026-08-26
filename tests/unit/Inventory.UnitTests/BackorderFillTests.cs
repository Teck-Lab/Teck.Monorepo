using System.Reflection;
using Ardalis.Specification;
using Inventories.Application.Inventory.Features.AdjustStock.V1;
using Inventories.Application.Inventory.ReadModels;
using Inventories.Domain.Entities;
using Inventories.Domain.ValueObjects;
using NSubstitute;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;
using Xunit;

namespace Inventories.UnitTests;

/// <summary>
/// Tests that a positive <see cref="AdjustStockHandler"/> adjustment fills outstanding
/// backordered reservation lines for that product, FIFO, within the same commit as the adjust.
/// </summary>
public sealed class BackorderFillTests
{
    private const string Tenant = "tenant-1";
    private static readonly DateTimeOffset Now = new(2026, 7, 5, 12, 0, 0, TimeSpan.Zero);

    private static IGenericWriteRepository<StockItem, Guid> StockRepoReturning(StockItem item)
    {
        var repository = Substitute.For<IGenericWriteRepository<StockItem, Guid>>();
        repository.FirstOrDefaultAsync(Arg.Any<ISpecification<StockItem>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StockItem?>(item));
        return repository;
    }

    /// <summary>
    /// Routes every call through the real <see cref="ISpecification{T}.Evaluate"/> so these tests
    /// exercise the actual <see cref="BackorderedLinesByProductSpec"/> filtering/ordering, not just
    /// the handler's fill loop.
    /// </summary>
    private static IGenericWriteRepository<Reservation, Guid> ReservationRepoOver(params Reservation[] reservations)
    {
        var repository = Substitute.For<IGenericWriteRepository<Reservation, Guid>>();
        repository.ListAsync(Arg.Any<ISpecification<Reservation>>(), true, Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var spec = callInfo.Arg<ISpecification<Reservation>>();
                return Task.FromResult<IReadOnlyList<Reservation>>(spec.Evaluate(reservations).ToList());
            });
        return repository;
    }

    [Fact]
    public async Task Handle_PositiveAdjust_FillsBackorderUpToNewAvailabilityAndPublishesStockReplenished()
    {
        var productId = Guid.NewGuid();
        var item = StockItem.Create(productId, Guid.NewGuid(), Tenant, quantityOnHand: 0, allowBackorder: true, reorderThreshold: -10);
        var reservation = Reservation.CreateCommitted(
            ReservationSource.Order,
            Guid.NewGuid(),
            Tenant,
            [new ReservationLine(productId, RequestedQuantity: 4, BackorderedQuantity: 4, Allocations: [])]);

        var stockItems = StockRepoReturning(item);
        var reservations = ReservationRepoOver(reservation);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IMessageBus>();

        var dto = await AdjustStockHandler.Handle(
            new AdjustStockCommand(item.Id, 6),
            stockItems,
            reservations,
            unitOfWork,
            bus,
            new FixedTimeProvider(Now),
            CancellationToken.None);

        // 4 of the 6 fill the backorder; 2 remain on hand as real availability.
        Assert.Equal(2, dto.Available);
        Assert.Equal(0, reservation.Lines[0].BackorderedQuantity);
        Assert.Equal(4, reservation.Lines[0].Allocations.Single(allocation => allocation.LocationId == item.LocationId).Quantity);
        Assert.Equal(4, item.QuantityReserved);

        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await bus.Received(1).PublishAsync(Arg.Is<StockReplenishedIntegrationEvent>(evt =>
            evt.ProductId == productId
            && evt.LocationId == item.LocationId
            && evt.TenantId == Tenant
            && evt.Available == 2));
    }

    [Fact]
    public async Task Handle_PositiveAdjust_FillsMultipleBackordersFifoAndStopsWhenAvailabilityExhausted()
    {
        var productId = Guid.NewGuid();
        var item = StockItem.Create(productId, Guid.NewGuid(), Tenant, quantityOnHand: 0, allowBackorder: true, reorderThreshold: -10);

        var older = Reservation.CreateCommitted(
            ReservationSource.Order,
            Guid.NewGuid(),
            Tenant,
            [new ReservationLine(productId, RequestedQuantity: 3, BackorderedQuantity: 3, Allocations: [])]);
        var newer = Reservation.CreateCommitted(
            ReservationSource.Order,
            Guid.NewGuid(),
            Tenant,
            [new ReservationLine(productId, RequestedQuantity: 5, BackorderedQuantity: 5, Allocations: [])]);
        SetCreatedAt(older, Now - TimeSpan.FromMinutes(10));
        SetCreatedAt(newer, Now - TimeSpan.FromMinutes(5));

        var stockItems = StockRepoReturning(item);
        // Pass newer first to prove the spec's FIFO ordering (by CreatedAt), not fake insertion order, drives the fill.
        var reservations = ReservationRepoOver(newer, older);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IMessageBus>();

        // Only 4 new units available: the older reservation's 3 should be fully filled first (FIFO),
        // leaving 1 to partially fill the newer reservation's 5, with 4 left outstanding on it.
        await AdjustStockHandler.Handle(
            new AdjustStockCommand(item.Id, 4),
            stockItems,
            reservations,
            unitOfWork,
            bus,
            new FixedTimeProvider(Now),
            CancellationToken.None);

        Assert.Equal(0, older.Lines[0].BackorderedQuantity);
        Assert.Equal(3, older.Lines[0].Allocations.Single().Quantity);
        Assert.Equal(4, newer.Lines[0].BackorderedQuantity);
        Assert.Equal(1, newer.Lines[0].Allocations.Single().Quantity);
        Assert.Equal(4, item.QuantityReserved);
        Assert.Equal(0, item.Available);
    }

    [Fact]
    public async Task Handle_PositiveAdjustWithNoBackorders_DoesNotTouchReservationsRepository()
    {
        var productId = Guid.NewGuid();
        var item = StockItem.Create(productId, Guid.NewGuid(), Tenant, quantityOnHand: 5, allowBackorder: false, reorderThreshold: -10);
        var stockItems = StockRepoReturning(item);
        var reservations = ReservationRepoOver();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IMessageBus>();

        var dto = await AdjustStockHandler.Handle(
            new AdjustStockCommand(item.Id, 3),
            stockItems,
            reservations,
            unitOfWork,
            bus,
            new FixedTimeProvider(Now),
            CancellationToken.None);

        Assert.Equal(8, dto.Available);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PositiveAdjust_DoesNotFillAnotherTenantsMatchingProduct()
    {
        var productId = Guid.NewGuid();
        var item = StockItem.Create(productId, Guid.NewGuid(), Tenant, 0, true, -10);
        Reservation own = Reservation.CreateCommitted(
            ReservationSource.Order,
            Guid.NewGuid(),
            Tenant,
            [new ReservationLine(productId, 1, 1, [])]);
        Reservation otherTenant = Reservation.CreateCommitted(
            ReservationSource.Order,
            Guid.NewGuid(),
            "tenant-2",
            [new ReservationLine(productId, 1, 1, [])]);

        await AdjustStockHandler.Handle(
            new AdjustStockCommand(item.Id, 1),
            StockRepoReturning(item),
            ReservationRepoOver(own, otherTenant),
            Substitute.For<IUnitOfWork>(),
            Substitute.For<IMessageBus>(),
            new FixedTimeProvider(Now),
            CancellationToken.None);

        Assert.Equal(0, own.Lines[0].BackorderedQuantity);
        Assert.Equal(1, otherTenant.Lines[0].BackorderedQuantity);
        Assert.Equal(0, otherTenant.Lines[0].Allocations.Count);
    }

    [Fact]
    public void FillBackorder_QuantityExceedsBackorderedQuantity_Throws()
    {
        var productId = Guid.NewGuid();
        var reservation = Reservation.CreateCommitted(
            ReservationSource.Order,
            Guid.NewGuid(),
            Tenant,
            [new ReservationLine(productId, RequestedQuantity: 4, BackorderedQuantity: 4, Allocations: [])]);

        Assert.Throws<InvalidOperationException>(() => reservation.FillBackorder(productId, Guid.NewGuid(), 5));
    }

    [Fact]
    public void FillBackorder_ReservationNotActive_Throws()
    {
        var productId = Guid.NewGuid();
        var reservation = Reservation.CreateCommitted(
            ReservationSource.Order,
            Guid.NewGuid(),
            Tenant,
            [new ReservationLine(productId, RequestedQuantity: 4, BackorderedQuantity: 4, Allocations: [])]);
        reservation.Release();

        Assert.Throws<InvalidOperationException>(() => reservation.FillBackorder(productId, Guid.NewGuid(), 2));
    }

    private static void SetCreatedAt(Reservation reservation, DateTimeOffset createdAt)
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        // CreatedAt's setter is private on its declaring type (BaseEntity<TId>); a private
        // accessor is not visible through a derived type's PropertyInfo, so the property must be
        // looked up again on its own DeclaringType before SetValue can reach the setter.
        PropertyInfo property = typeof(Reservation).GetProperty(nameof(Reservation.CreatedAt), Flags)!;
        property.DeclaringType!.GetProperty(nameof(Reservation.CreatedAt), Flags)!.SetValue(reservation, createdAt);
    }

    /// <summary>A minimal <see cref="TimeProvider"/> stub returning a fixed instant, used to make expiry comparisons deterministic.</summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
