using Ardalis.Specification;
using Inventories.Application.Inventory.Features.GetAvailability.V1;
using Inventories.Application.Inventory.ReadModels;
using Inventories.Domain.Entities;
using Inventories.Domain.ValueObjects;
using NSubstitute;
using SharedKernel.Core.Database;
using Xunit;

namespace Inventories.UnitTests;

/// <summary>
/// Tests that reads compute EFFECTIVE availability that discounts expired holds immediately —
/// self-healing the instant a <see cref="ReservationStatus.Held"/> reservation's
/// <see cref="Reservation.ExpiresAt"/> lapses, with no dependency on the (dormant) expiry sweep
/// (Task 18).
/// </summary>
public sealed class LazyExpiryTests
{
    private const string Tenant = "tenant-1";
    private static readonly DateTimeOffset Now = new(2026, 7, 5, 12, 0, 0, TimeSpan.Zero);

    private static IGenericReadRepository<StockItem, Guid> StockRepoReturning(params StockItem[] items)
    {
        var repository = Substitute.For<IGenericReadRepository<StockItem, Guid>>();
        repository.ListAsync(Arg.Any<ISpecification<StockItem>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<StockItem>>(items));
        return repository;
    }

    /// <summary>
    /// Routes every call through the real <see cref="ISpecification{T}.Evaluate"/> so these tests
    /// exercise the actual <see cref="ActiveReservationsByProductSpec"/> filtering, not just the
    /// handler's aggregation.
    /// </summary>
    private static IGenericReadRepository<Reservation, Guid> ReservationRepoOver(params Reservation[] reservations)
    {
        var repository = Substitute.For<IGenericReadRepository<Reservation, Guid>>();
        repository.ListAsync(Arg.Any<ISpecification<Reservation>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var spec = callInfo.Arg<ISpecification<Reservation>>();
                return Task.FromResult<IReadOnlyList<Reservation>>(spec.Evaluate(reservations).ToList());
            });
        return repository;
    }

    [Fact]
    public void ActiveReservationsByProductSpec_ExcludesAHeldReservationThatHasExpired()
    {
        var productId = Guid.NewGuid();
        var expired = Reservation.CreateHeld(
            ReservationSource.Basket, Guid.NewGuid(), Tenant, Now - TimeSpan.FromMinutes(1),
            [new ReservationLine(productId, 5, 0, [new Allocation(Guid.NewGuid(), 5)])]);
        var active = Reservation.CreateHeld(
            ReservationSource.Basket, Guid.NewGuid(), Tenant, Now + TimeSpan.FromMinutes(1),
            [new ReservationLine(productId, 5, 0, [new Allocation(Guid.NewGuid(), 5)])]);
        var committed = Reservation.CreateCommitted(
            ReservationSource.Order, Guid.NewGuid(), Tenant,
            [new ReservationLine(productId, 5, 0, [new Allocation(Guid.NewGuid(), 5)])]);

        var result = new ActiveReservationsByProductSpec(productId, Now)
            .Evaluate([expired, active, committed])
            .ToList();

        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result, reservation => reservation.Id == expired.Id);
    }

    [Fact]
    public async Task Handle_HeldReservationExpiresInThePast_IsIgnoredSoStockIsFullyAvailable()
    {
        var productId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var stockItem = StockItem.Create(productId, locationId, Tenant, quantityOnHand: 5, allowBackorder: false, reorderThreshold: 0);
        var expiredHold = Reservation.CreateHeld(
            ReservationSource.Basket,
            Guid.NewGuid(),
            Tenant,
            expiresAt: Now - TimeSpan.FromMinutes(1),
            lines: [new ReservationLine(productId, 5, 0, [new Allocation(locationId, 5)])]);

        var dto = await GetAvailabilityHandler.Handle(
            new GetAvailabilityQuery(productId, null),
            StockRepoReturning(stockItem),
            ReservationRepoOver(expiredHold),
            new FixedTimeProvider(Now),
            CancellationToken.None);

        Assert.Equal(5, dto.Available);
    }

    [Fact]
    public async Task Handle_HeldReservationExpiresInTheFuture_StillCountsAsReserved()
    {
        var productId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var stockItem = StockItem.Create(productId, locationId, Tenant, quantityOnHand: 5, allowBackorder: false, reorderThreshold: 0);
        var activeHold = Reservation.CreateHeld(
            ReservationSource.Basket,
            Guid.NewGuid(),
            Tenant,
            expiresAt: Now + TimeSpan.FromMinutes(1),
            lines: [new ReservationLine(productId, 5, 0, [new Allocation(locationId, 5)])]);

        var dto = await GetAvailabilityHandler.Handle(
            new GetAvailabilityQuery(productId, null),
            StockRepoReturning(stockItem),
            ReservationRepoOver(activeHold),
            new FixedTimeProvider(Now),
            CancellationToken.None);

        Assert.Equal(0, dto.Available);
    }

    /// <summary>A minimal <see cref="TimeProvider"/> stub returning a fixed instant, used to make expiry comparisons deterministic.</summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
