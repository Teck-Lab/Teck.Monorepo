using Ardalis.Specification;
using Inventories.Application.Inventory.Features.ExpireHeldReservations.V1;
using Inventories.Application.Inventory.ReadModels;
using Inventories.Domain.Entities;
using Inventories.Domain.ValueObjects;
using NSubstitute;
using SharedKernel.Core.Database;
using Xunit;

namespace Inventories.UnitTests;

/// <summary>
/// Tests for <see cref="ExpireHeldReservationsHandler"/> — the housekeeping sweep (Task 18) that
/// makes the stored <see cref="StockItem.QuantityReserved"/> counter truthful again by actually
/// transitioning lapsed <see cref="ReservationStatus.Held"/> reservations to
/// <see cref="ReservationStatus.Expired"/> and releasing their allocations.
/// </summary>
public sealed class ExpireHeldReservationsHandlerTests
{
    private const string Tenant = "tenant-1";
    private static readonly DateTimeOffset Now = new(2026, 7, 5, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Routes every call through the real <see cref="ISpecification{T}.Evaluate"/> so these tests
    /// exercise the actual <see cref="ExpiredHeldReservationsSpec"/> filtering, not just the
    /// handler's mutation logic — mirroring <c>LazyExpiryTests.ReservationRepoOver</c>.
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

    private static IGenericWriteRepository<StockItem, Guid> StockRepoReturning(params StockItem[] items)
    {
        var repository = Substitute.For<IGenericWriteRepository<StockItem, Guid>>();
        repository.FirstOrDefaultAsync(Arg.Any<ISpecification<StockItem>>(), true, Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var spec = callInfo.Arg<ISpecification<StockItem>>();
                return Task.FromResult(spec.Evaluate(items).FirstOrDefault());
            });
        return repository;
    }

    [Fact]
    public async Task Handle_ExpiredHeldReservation_ExpiresItReleasesAllocationsAndReturnsCount()
    {
        var productId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var stockItem = StockItem.Create(productId, locationId, Tenant, quantityOnHand: 10, allowBackorder: false, reorderThreshold: 0);
        stockItem.Reserve(3);

        var expired = Reservation.CreateHeld(
            ReservationSource.Basket,
            Guid.NewGuid(),
            Tenant,
            expiresAt: Now - TimeSpan.FromMinutes(1),
            lines: [new ReservationLine(productId, 3, 0, [new Allocation(locationId, 3)])]);

        var reservationRepo = ReservationRepoOver(expired);
        var stockRepo = StockRepoReturning(stockItem);
        var unitOfWork = Substitute.For<IUnitOfWork>();

        int count = await ExpireHeldReservationsHandler.Handle(
            new ExpireHeldReservationsCommand(),
            reservationRepo,
            stockRepo,
            unitOfWork,
            new FixedTimeProvider(Now),
            CancellationToken.None);

        Assert.Equal(1, count);
        Assert.Equal(ReservationStatus.Expired, expired.Status);
        Assert.Null(expired.ExpiresAt);
        Assert.Equal(0, stockItem.QuantityReserved);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_HeldReservationNotYetExpired_IsLeftAloneAndNothingIsSaved()
    {
        var productId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var stockItem = StockItem.Create(productId, locationId, Tenant, quantityOnHand: 10, allowBackorder: false, reorderThreshold: 0);
        stockItem.Reserve(3);

        var notYetExpired = Reservation.CreateHeld(
            ReservationSource.Basket,
            Guid.NewGuid(),
            Tenant,
            expiresAt: Now + TimeSpan.FromMinutes(1),
            lines: [new ReservationLine(productId, 3, 0, [new Allocation(locationId, 3)])]);

        var reservationRepo = ReservationRepoOver(notYetExpired);
        var stockRepo = StockRepoReturning(stockItem);
        var unitOfWork = Substitute.For<IUnitOfWork>();

        int count = await ExpireHeldReservationsHandler.Handle(
            new ExpireHeldReservationsCommand(),
            reservationRepo,
            stockRepo,
            unitOfWork,
            new FixedTimeProvider(Now),
            CancellationToken.None);

        Assert.Equal(0, count);
        Assert.Equal(ReservationStatus.Held, notYetExpired.Status);
        Assert.Equal(3, stockItem.QuantityReserved);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>A minimal <see cref="TimeProvider"/> stub returning a fixed instant, used to make expiry comparisons deterministic.</summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
