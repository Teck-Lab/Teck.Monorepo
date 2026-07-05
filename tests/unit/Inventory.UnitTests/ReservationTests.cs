using Inventories.Domain.Entities;
using Inventories.Domain.ValueObjects;
using Xunit;

namespace Inventories.UnitTests;

public sealed class ReservationTests
{
    [Fact]
    public void CreateCommitted_SetsCommittedStatusWithNoExpiryAndPreservesLines()
    {
        var sourceId = Guid.NewGuid();
        IReadOnlyList<ReservationLine> lines =
        [
            new ReservationLine(
                Guid.NewGuid(),
                RequestedQuantity: 3,
                BackorderedQuantity: 0,
                Allocations: [new Allocation(Guid.NewGuid(), 3)]),
        ];

        var reservation = Reservation.CreateCommitted(ReservationSource.Order, sourceId, "tenant-1", lines);

        Assert.Equal(ReservationStatus.Committed, reservation.Status);
        Assert.Null(reservation.ExpiresAt);
        Assert.Equal(ReservationSource.Order, reservation.SourceType);
        Assert.Equal(sourceId, reservation.SourceId);
        Assert.Equal("tenant-1", reservation.TenantId);
        Assert.Equal(lines.Count, reservation.Lines.Count);
        Assert.Equal(lines[0].ProductId, reservation.Lines[0].ProductId);
        Assert.Equal(lines[0].RequestedQuantity, reservation.Lines[0].RequestedQuantity);
        Assert.Single(reservation.Lines[0].Allocations);
    }

    [Fact]
    public void Release_FromCommitted_SetsReleased()
    {
        var reservation = Reservation.CreateCommitted(
            ReservationSource.Order,
            Guid.NewGuid(),
            "tenant-1",
            [new ReservationLine(Guid.NewGuid(), 1, 0, [])]);

        reservation.Release();

        Assert.Equal(ReservationStatus.Released, reservation.Status);
    }

    [Fact]
    public void Expire_FromCommitted_Throws()
    {
        var reservation = Reservation.CreateCommitted(
            ReservationSource.Order,
            Guid.NewGuid(),
            "tenant-1",
            [new ReservationLine(Guid.NewGuid(), 1, 0, [])]);

        Assert.Throws<InvalidOperationException>(() => reservation.Expire());
    }
}
