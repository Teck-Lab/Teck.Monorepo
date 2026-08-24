using Inventories.Domain.Entities;
using Inventories.Domain.ValueObjects;
using Xunit;

namespace Inventories.UnitTests;

/// <summary>Regression tests for bounded order-backorder transitions.</summary>
public sealed class BackorderLifecycleTests
{
    [Fact]
    public void FillBackorder_FinalAllocation_ClearsDeadlineAndCreatesStableReadyKey()
    {
        var productId = Guid.NewGuid();
        var reservation = Reservation.CreateCommitted(
            ReservationSource.Order,
            Guid.NewGuid(),
            "tenant-1",
            [new ReservationLine(productId, 2, 2, [])],
            DateTimeOffset.UtcNow.AddDays(1));

        bool transitioned = reservation.FillBackorder(productId, Guid.NewGuid(), 2);

        Assert.True(transitioned);
        Assert.Null(reservation.BackorderExpiresAt);
        Assert.False(reservation.HasOutstandingBackorder);
        Assert.Equal($"backorder-ready:{reservation.Id:N}", reservation.BackorderReadyOutcomeKey);
    }

    [Fact]
    public void FillBackorder_PartialAllocation_DoesNotEmitReadyKeyUntilTheFinalFill()
    {
        var productId = Guid.NewGuid();
        var reservation = Reservation.CreateCommitted(
            ReservationSource.Order,
            Guid.NewGuid(),
            "tenant-1",
            [new ReservationLine(productId, 2, 2, [])],
            DateTimeOffset.UtcNow.AddDays(1));

        bool partial = reservation.FillBackorder(productId, Guid.NewGuid(), 1);
        bool final = reservation.FillBackorder(productId, Guid.NewGuid(), 1);

        Assert.False(partial);
        Assert.True(final);
        Assert.Equal(0, reservation.Lines.Single().BackorderedQuantity);
        Assert.Equal($"backorder-ready:{reservation.Id:N}", reservation.BackorderReadyOutcomeKey);
    }

    [Fact]
    public void ExpireBackorder_DueReservation_ExpiresAndKeepsStableOutcomeKey()
    {
        var reservation = Reservation.CreateCommitted(
            ReservationSource.Order,
            Guid.NewGuid(),
            "tenant-1",
            [new ReservationLine(Guid.NewGuid(), 2, 1, [])],
            DateTimeOffset.UtcNow.AddMinutes(-1));

        string outcomeKey = reservation.ExpireBackorder();

        Assert.Equal(ReservationStatus.Expired, reservation.Status);
        Assert.Null(reservation.BackorderExpiresAt);
        Assert.Equal(outcomeKey, reservation.BackorderExpiredOutcomeKey);
    }

    [Fact]
    public void ExpireBackorder_ReplayIsRejectedWithoutChangingItsStableOutcomeKey()
    {
        var reservation = Reservation.CreateCommitted(
            ReservationSource.Order,
            Guid.NewGuid(),
            "tenant-1",
            [new ReservationLine(Guid.NewGuid(), 1, 1, [])],
            DateTimeOffset.UtcNow.AddMinutes(-1));

        string outcomeKey = reservation.ExpireBackorder();

        Assert.Throws<InvalidOperationException>(() => reservation.ExpireBackorder());
        Assert.Equal(outcomeKey, reservation.BackorderExpiredOutcomeKey);
    }
}
